using System;
using System.Collections.Generic;
using System.Linq;

namespace Monovera
{
    /// <summary>
    /// Graph-aware search helper for MonoveraBot.
    /// Expands semantic search results by traversing the requirement hierarchy.
    /// This gives the LLM full context of parent/child/sibling relationships.
    /// </summary>
    public class GraphSearchHelper
    {
        private readonly Dictionary<string, IssueKnowledge> _knowledgeBase;

        public GraphSearchHelper(Dictionary<string, IssueKnowledge> knowledgeBase)
        {
            _knowledgeBase = knowledgeBase ?? throw new ArgumentNullException(nameof(knowledgeBase));
        }

        /// <summary>
        /// Expands semantic search results by traversing the hierarchy graph.
        /// For each result, includes: parents (up to root), children (all), siblings, and related issues.
        /// </summary>
        /// <param name="semanticResults">Initial results from vector/semantic search</param>
        /// <param name="maxDepth">Maximum depth to traverse for children (default: 2)</param>
        /// <returns>Expanded list of issues with full hierarchical context</returns>
        public List<IssueKnowledge> ExpandHierarchy(
            List<(string key, float score)> semanticResults,
            int maxDepth = 2)
        {
            var expanded = new Dictionary<string, (IssueKnowledge issue, float score, string source)>();

            foreach (var (key, score) in semanticResults)
            {
                if (!_knowledgeBase.TryGetValue(key, out var issue))
                    continue;

                // Add the semantic match itself (highest priority)
                expanded[key] = (issue, score, "semantic");

                // 1. Add parent chain (up to root)
                AddParentChain(key, expanded, score * 0.8f);

                // 2. Add all children (recursive, up to maxDepth)
                AddChildren(key, expanded, score * 0.9f, depth: 0, maxDepth: maxDepth);

                // 3. Add siblings (same parent)
                AddSiblings(key, expanded, score * 0.7f);

                // 4. Add related issues
                AddRelated(key, expanded, score * 0.6f);
            }

            // Sort by relevance (score + graph distance)
            return expanded.Values
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.source == "semantic" ? 0 : 1) // Semantic matches first
                .Select(x => x.issue)
                .ToList();
        }

        /// <summary>
        /// Builds a hierarchical tree structure from the expanded results.
        /// Used to generate structured prompts for the LLM.
        /// </summary>
        public string BuildHierarchyTree(List<IssueKnowledge> issues)
        {
            var roots = issues.Where(i => string.IsNullOrEmpty(i.ParentSummary)).ToList();
            if (roots.Count == 0)
            {
                // No roots found, find the highest-level issues
                roots = issues.OrderBy(i => GetHierarchyLevel(i.Key)).Take(3).ToList();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("REQUIREMENT HIERARCHY:");
            sb.AppendLine();

            foreach (var root in roots)
            {
                BuildTreeRecursive(root, issues, sb, indent: 0);
            }

            return sb.ToString();
        }

        private void BuildTreeRecursive(
            IssueKnowledge issue,
            List<IssueKnowledge> allIssues,
            System.Text.StringBuilder sb,
            int indent)
        {
            string prefix = new string(' ', indent * 2);
            string connector = indent > 0 ? "├── " : "• ";
            
            sb.AppendLine($"{prefix}{connector}{issue.Key}: {issue.Summary} ({issue.IssueType})");

            // Add children
            var children = allIssues.Where(i => i.ParentSummary == issue.Summary).ToList();
            foreach (var child in children)
            {
                BuildTreeRecursive(child, allIssues, sb, indent + 1);
            }

            // Add related issues (at same level)
            if (indent < 2) // Only show related for top 2 levels
            {
                foreach (var relatedKey in issue.RelatedIssues.Take(3))
                {
                    if (_knowledgeBase.TryGetValue(relatedKey, out var related) && allIssues.Contains(related))
                    {
                        string relatedPrefix = new string(' ', (indent + 1) * 2);
                        sb.AppendLine($"{relatedPrefix}↔ {related.Key}: {related.Summary} (Related)");
                    }
                }
            }
        }

        /// <summary>
        /// Builds a structured prompt that explains the hierarchy to the LLM.
        /// This helps the LLM understand parent/child/sibling relationships.
        /// </summary>
        public string BuildStructuredPrompt(string question, List<IssueKnowledge> issues)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("You are an AI assistant helping with software requirements and test cases.");
            sb.AppendLine("The data below shows a hierarchical structure of requirements, tests, and related issues.");
            sb.AppendLine();

            // 1. Show hierarchy tree
            sb.AppendLine(BuildHierarchyTree(issues));
            sb.AppendLine();

            // 2. Show detailed information for each issue
            sb.AppendLine("DETAILED INFORMATION:");
            sb.AppendLine();

            int count = 0;
            foreach (var issue in issues.Take(20)) // Limit to avoid token overflow
            {
                count++;
                sb.AppendLine($"[{count}] {issue.Key} - {issue.Summary}");
                sb.AppendLine($"    Type: {issue.IssueType}");
                sb.AppendLine($"    Status: {issue.Status}");
                sb.AppendLine($"    Project: {issue.ProjectName}");

                if (!string.IsNullOrEmpty(issue.ParentSummary))
                    sb.AppendLine($"    Parent: {issue.ParentSummary}");

                if (issue.RelatedIssues.Any())
                    sb.AppendLine($"    Related: {string.Join(", ", issue.RelatedIssues.Take(5))}");

                if (!string.IsNullOrWhiteSpace(issue.Description))
                {
                    var desc = issue.Description.Length > 300
                        ? issue.Description.Substring(0, 300) + "..."
                        : issue.Description;
                    sb.AppendLine($"    Description: {desc}");
                }

                sb.AppendLine();
            }

            // 3. Add instructions
            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("- Analyze the hierarchy structure above");
            sb.AppendLine("- Reference specific issue keys (e.g., REQ-123, TST-456)");
            sb.AppendLine("- Explain parent/child relationships when relevant");
            sb.AppendLine("- Be comprehensive but organized");
            sb.AppendLine("- Use bullet points and clear structure");
            sb.AppendLine();
            sb.AppendLine($"USER QUESTION: {question}");
            sb.AppendLine();
            sb.AppendLine("Please provide a detailed answer based on the hierarchy above:");

            return sb.ToString();
        }

        // Helper methods for graph traversal
        private void AddParentChain(
            string key,
            Dictionary<string, (IssueKnowledge issue, float score, string source)> expanded,
            float baseScore)
        {
            if (!_knowledgeBase.TryGetValue(key, out var issue))
                return;

            var parentKey = FindParentKey(issue);
            int level = 0;
            while (!string.IsNullOrEmpty(parentKey) && level < 5) // Max 5 levels up
            {
                if (_knowledgeBase.TryGetValue(parentKey, out var parent))
                {
                    if (!expanded.ContainsKey(parentKey))
                    {
                        float score = baseScore * (1f / (level + 1)); // Decay with distance
                        expanded[parentKey] = (parent, score, $"parent-{level}");
                    }

                    parentKey = FindParentKey(parent);
                    level++;
                }
                else
                {
                    break;
                }
            }
        }

        private void AddChildren(
            string key,
            Dictionary<string, (IssueKnowledge issue, float score, string source)> expanded,
            float baseScore,
            int depth,
            int maxDepth)
        {
            if (depth >= maxDepth)
                return;

            var children = _knowledgeBase.Values.Where(i => FindParentKey(i) == key).ToList();

            foreach (var child in children)
            {
                if (!expanded.ContainsKey(child.Key))
                {
                    float score = baseScore * (1f / (depth + 1));
                    expanded[child.Key] = (child, score, $"child-{depth}");
                }

                // Recursive: add grandchildren
                AddChildren(child.Key, expanded, baseScore * 0.9f, depth + 1, maxDepth);
            }
        }

        private void AddSiblings(
            string key,
            Dictionary<string, (IssueKnowledge issue, float score, string source)> expanded,
            float baseScore)
        {
            if (!_knowledgeBase.TryGetValue(key, out var issue))
                return;

            var parentKey = FindParentKey(issue);
            if (string.IsNullOrEmpty(parentKey))
                return;

            // Find all issues with same parent
            var siblings = _knowledgeBase.Values
                .Where(i => FindParentKey(i) == parentKey && i.Key != key)
                .Take(5); // Limit siblings to avoid explosion

            foreach (var sibling in siblings)
            {
                if (!expanded.ContainsKey(sibling.Key))
                {
                    expanded[sibling.Key] = (sibling, baseScore, "sibling");
                }
            }
        }

        private void AddRelated(
            string key,
            Dictionary<string, (IssueKnowledge issue, float score, string source)> expanded,
            float baseScore)
        {
            if (!_knowledgeBase.TryGetValue(key, out var issue))
                return;

            foreach (var relatedKey in issue.RelatedIssues.Take(5)) // Limit to avoid explosion
            {
                if (_knowledgeBase.TryGetValue(relatedKey, out var related))
                {
                    if (!expanded.ContainsKey(relatedKey))
                    {
                        expanded[relatedKey] = (related, baseScore, "related");
                    }
                }
            }
        }

        private string? FindParentKey(IssueKnowledge issue)
        {
            // Try to find parent from ParentSummary
            if (!string.IsNullOrEmpty(issue.ParentSummary))
            {
                var parent = _knowledgeBase.Values
                    .FirstOrDefault(i => i.Summary == issue.ParentSummary);
                return parent?.Key;
            }
            return null;
        }

        private int GetHierarchyLevel(string key)
        {
            int level = 0;
            var currentKey = key;

            while (!string.IsNullOrEmpty(currentKey) && level < 10)
            {
                if (!_knowledgeBase.TryGetValue(currentKey, out var issue))
                    break;

                var parentKey = FindParentKey(issue);
                if (string.IsNullOrEmpty(parentKey))
                    break;

                currentKey = parentKey;
                level++;
            }

            return level;
        }
    }

    // Extension to IssueKnowledge to track graph metadata
    public partial class IssueKnowledge
    {
        public int HierarchyLevel { get; set; } // Calculated: 0=root, 1=child, etc.
        public string HierarchyPath { get; set; } = ""; // "REQ-01 > REQ-89 > TST-145"
    }
}
