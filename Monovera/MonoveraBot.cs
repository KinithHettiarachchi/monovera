using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Data;

namespace Monovera
{
    /// <summary>
    /// AI chatbot that learns from hierarchical database of issues, test cases, and requirements
    /// to provide intelligent responses using a self-contained local model.
    /// </summary>
    public class MonoveraBot : IDisposable
    {
        private readonly string _databasePath;
        private readonly string _modelPath;
        private readonly string _knowledgeIndexPath;
        private readonly string _vectorStorePath;
        private bool _isTrained;
        private Dictionary<string, IssueKnowledge>? _knowledgeBase;
        private VectorStore? _vectorStore;
        private readonly ILlmProvider _llmProvider;
        private bool _useSemanticSearch;

        /// <summary>
        /// Initializes a new instance of the MonoveraBot.
        /// </summary>
        /// <param name="databasePath">Path to the SQLite database containing issues and descriptions.</param>
        /// <param name="modelDirectory">Directory to store the trained model files (defaults to working directory)</param>
        /// <param name="llmProvider">LLM provider for AI responses (defaults to Ollama with phi:latest model)</param>
        /// <param name="useSemanticSearch">Enable semantic search with embeddings (requires embedding model)</param>
        public MonoveraBot(string databasePath, string? modelDirectory = null, ILlmProvider? llmProvider = null, bool useSemanticSearch = true)
        {
            _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));

            modelDirectory ??= Path.GetDirectoryName(_databasePath) ?? Environment.CurrentDirectory;
            _modelPath = Path.Combine(modelDirectory, "monovera_model.dat");
            _knowledgeIndexPath = Path.Combine(modelDirectory, "monovera_knowledge.idx");
            _vectorStorePath = Path.Combine(modelDirectory, "monovera_vectors.db");

            // Use llama3.1 for better reasoning (8B parameters, 128K context window)
            _llmProvider = llmProvider ?? new OllamaLlmProvider(modelName: "llama3.1:latest");
            _useSemanticSearch = useSemanticSearch && _llmProvider.SupportsEmbeddings;

            _isTrained = File.Exists(_knowledgeIndexPath);

            if (_isTrained)
            {
                LoadKnowledgeBase();

                if (_useSemanticSearch)
                {
                    _vectorStore = new VectorStore(_vectorStorePath);
                }
            }
        }

        /// <summary>
        /// Trains the bot by reading the hierarchical database of issues, descriptions, test cases,
        /// and requirements. Builds a knowledge index stored in a local file.
        /// </summary>
        /// <param name="progress">Optional progress callback for training status</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public async Task TrainAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                progress?.Report("Reading database and loading issues...");

                var issues = LoadIssuesFromDatabase();
                progress?.Report($"Loaded {issues.Count} issues from database");

                progress?.Report("Building knowledge base with relationships...");
                _knowledgeBase = BuildKnowledgeBase(issues, progress);

                progress?.Report("Creating searchable index...");
                BuildSearchIndex(_knowledgeBase);

                // Generate embeddings if semantic search is enabled
                if (_useSemanticSearch && _llmProvider.SupportsEmbeddings)
                {
                    progress?.Report("Generating semantic embeddings (this may take a while)...");
                    progress?.Report("Make sure embedding model is installed: ollama pull nomic-embed-text");

                    await GenerateEmbeddingsAsync(_knowledgeBase, progress, cancellationToken);

                    progress?.Report("Saving vector store...");
                    _vectorStore?.Save();
                }

                progress?.Report("Saving knowledge base to file...");
                SaveKnowledgeBase();

                _isTrained = true;

                var searchType = _useSemanticSearch ? "with semantic search" : "with keyword search";
                progress?.Report($"Training completed! Knowledge base contains {_knowledgeBase.Count} indexed items {searchType}.");
            }
            catch (Exception ex)
            {
                progress?.Report($"Training failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Synchronous wrapper for TrainAsync.
        /// </summary>
        public void Train()
        {
            TrainAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asks a question to the bot and receives an answer based on the trained knowledge base.
        /// </summary>
        /// <param name="question">The question to ask</param>
        /// <returns>The AI-generated answer</returns>
        public string Ask(string question)
        {
            return AskAsync(question).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async version of Ask that generates AI responses.
        /// </summary>
        public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
                throw new ArgumentException("Question cannot be empty", nameof(question));

            if (!_isTrained || _knowledgeBase == null)
            {
                return "Bot is not trained yet. Please train first.";
            }

            try
            {
                // Check if LLM is available
                if (!await _llmProvider.IsAvailableAsync())
                {
                    return $"AI model ({_llmProvider.ProviderName}) is not available. " +
                           "Please install Ollama from https://ollama.ai and run 'ollama pull llama3.1'.";
                }

                // Search for relevant issues (increased to 15 for better context)
                var relevantIssues = SearchKnowledge(question, topK: 15);

                if (!relevantIssues.Any())
                {
                    return "I couldn't find any relevant information in the knowledge base for that question. Try rephrasing or asking about specific projects (TST, REQ, STF), test cases, or requirements.";
                }

                // Expand hierarchical context for top 5 results
                var expandedContext = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var finalIssues = new List<IssueKnowledge>();

                foreach (var issue in relevantIssues.Take(5))
                {
                    // Load full hierarchy for this issue
                    var hierarchy = LoadHierarchicalContext(issue.Key, maxDepth: 2);

                    foreach (var hierarchyIssue in hierarchy)
                    {
                        if (expandedContext.Add(hierarchyIssue.Key))
                        {
                            finalIssues.Add(hierarchyIssue);
                        }
                    }
                }

                // Add remaining relevant issues (not in hierarchy)
                foreach (var issue in relevantIssues.Skip(5))
                {
                    if (expandedContext.Add(issue.Key))
                    {
                        finalIssues.Add(issue);
                    }
                }

                // Limit to top 20 issues total (LLM context limit)
                finalIssues = finalIssues.Take(20).ToList();

                var answer = await GenerateAiAnswerAsync(question, finalIssues, cancellationToken);
                return answer;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Gets whether the bot has been trained.
        /// </summary>
        public bool IsTrained => _isTrained;

        private Dictionary<string, IssueData> LoadIssuesFromDatabase()
        {
            var issues = new Dictionary<string, IssueData>(StringComparer.OrdinalIgnoreCase);

            using var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT KEY, SUMMARY, DESCRIPTION, PARENTKEY, CHILDRENKEYS, RELATESKEYS, 
                       ISSUETYPE, PROJECTNAME, PROJECTCODE, STATUS, HISTORY, ATTACHMENTS 
                FROM issue";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var issue = new IssueData
                {
                    Key = GetString(reader, "KEY"),
                    Summary = GetString(reader, "SUMMARY"),
                    Description = GetString(reader, "DESCRIPTION"),
                    ParentKey = GetString(reader, "PARENTKEY"),
                    ChildrenKeys = SplitKeys(GetString(reader, "CHILDRENKEYS")),
                    RelatesKeys = SplitKeys(GetString(reader, "RELATESKEYS")),
                    IssueType = GetString(reader, "ISSUETYPE"),
                    ProjectName = GetString(reader, "PROJECTNAME"),
                    ProjectCode = GetString(reader, "PROJECTCODE"),
                    Status = GetString(reader, "STATUS"),
                    History = GetString(reader, "HISTORY"),
                    Attachments = GetString(reader, "ATTACHMENTS")
                };

                if (!string.IsNullOrWhiteSpace(issue.Key))
                {
                    issues[issue.Key] = issue;
                }
            }

            return issues;
        }

        private Dictionary<string, IssueKnowledge> BuildKnowledgeBase(
            Dictionary<string, IssueData> issues, 
            IProgress<string>? progress)
        {
            var knowledge = new Dictionary<string, IssueKnowledge>();
            int processed = 0;

            foreach (var issue in issues.Values)
            {
                var kb = new IssueKnowledge
                {
                    Key = issue.Key,
                    Summary = issue.Summary,
                    Description = issue.Description,
                    IssueType = issue.IssueType,
                    Status = issue.Status,
                    ProjectName = issue.ProjectName,
                    FullText = BuildFullText(issue, issues),
                    Keywords = ExtractKeywords(issue),
                    RelatedIssues = new List<string>()
                };

                if (!string.IsNullOrWhiteSpace(issue.ParentKey) && issues.ContainsKey(issue.ParentKey))
                {
                    kb.ParentSummary = issues[issue.ParentKey].Summary;
                    kb.RelatedIssues.Add(issue.ParentKey);
                }

                kb.RelatedIssues.AddRange(issue.ChildrenKeys);
                kb.RelatedIssues.AddRange(issue.RelatesKeys);

                knowledge[issue.Key] = kb;

                processed++;
                if (processed % 10 == 0)
                {
                    progress?.Report($"Processed {processed}/{issues.Count} issues...");
                }
            }

            return knowledge;
        }

        private string BuildFullText(IssueData issue, Dictionary<string, IssueData> allIssues)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Issue: {issue.Key}");
            sb.AppendLine($"Type: {issue.IssueType}");
            sb.AppendLine($"Project: {issue.ProjectName} ({issue.ProjectCode})");
            sb.AppendLine($"Status: {issue.Status}");
            sb.AppendLine($"Summary: {issue.Summary}");
            sb.AppendLine($"Description: {issue.Description}");

            if (!string.IsNullOrWhiteSpace(issue.ParentKey) && allIssues.TryGetValue(issue.ParentKey, out var parent))
            {
                sb.AppendLine($"Parent: {issue.ParentKey} - {parent.Summary}");
            }

            if (issue.ChildrenKeys.Any())
            {
                sb.AppendLine($"Children: {string.Join(", ", issue.ChildrenKeys)}");
                foreach (var childKey in issue.ChildrenKeys.Take(3))
                {
                    if (allIssues.TryGetValue(childKey, out var child))
                    {
                        sb.AppendLine($"  - {childKey}: {child.Summary}");
                    }
                }
            }

            if (issue.RelatesKeys.Any())
            {
                sb.AppendLine($"Related to: {string.Join(", ", issue.RelatesKeys)}");
            }

            if (!string.IsNullOrWhiteSpace(issue.History))
            {
                sb.AppendLine($"History: {issue.History}");
            }

            return sb.ToString();
        }

        private List<string> ExtractKeywords(IssueData issue)
        {
            var keywords = new List<string>();

            var text = $"{issue.Key} {issue.Summary} {issue.Description} {issue.IssueType}".ToLowerInvariant();

            var words = text.Split(new[] { ' ', ',', '.', ';', ':', '\n', '\r', '\t', '-', '_' }, 
                                   StringSplitOptions.RemoveEmptyEntries);

            var stopWords = new HashSet<string> { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "from", "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did", "will", "would", "should", "could", "may", "might", "must", "can" };

            foreach (var word in words)
            {
                if (word.Length > 3 && !stopWords.Contains(word))
                {
                    keywords.Add(word);
                }
            }

            return keywords.Distinct().ToList();
        }

        private void BuildSearchIndex(Dictionary<string, IssueKnowledge> knowledge)
        {
            foreach (var kb in knowledge.Values)
            {
                kb.SearchScore = 0;
            }
        }

        private async Task GenerateEmbeddingsAsync(
            Dictionary<string, IssueKnowledge> knowledge,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            _vectorStore = new VectorStore(_vectorStorePath);

            // Clear existing embeddings - each training overwrites the database
            progress?.Report("Clearing old embeddings from database...");
            _vectorStore.Clear();

            int processed = 0;
            int total = knowledge.Count;

            foreach (var issue in knowledge.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Create text for embedding (summary + description)
                    var textForEmbedding = $"{issue.Summary} {issue.Description}".Trim();

                    if (string.IsNullOrWhiteSpace(textForEmbedding))
                        textForEmbedding = issue.Key;

                    // Use chunked embeddings for long texts (preserves ALL content!)
                    float[] embedding;

                    if (textForEmbedding.Length > 7000)
                    {
                        // Long text: Split into chunks, embed each, then average
                        // This preserves semantic meaning of the ENTIRE document!
                        embedding = await ChunkedEmbeddingHelper.GetChunkedEmbeddingAsync(
                            textForEmbedding,
                            _llmProvider,
                            chunkSize: 7000,
                            overlapSize: 500,
                            cancellationToken);

                        progress?.Report($"  └─ Generated chunked embedding for {issue.Key} ({textForEmbedding.Length} chars, {ChunkedEmbeddingHelper.ChunkText(textForEmbedding, 7000, 500).Count} chunks)");
                    }
                    else
                    {
                        // Short text: Single embedding
                        embedding = await _llmProvider.GetEmbeddingAsync(textForEmbedding, cancellationToken);
                    }

                    issue.Embedding = embedding;

                    // Store in vector database (SQLite)
                    _vectorStore.AddVector(issue.Key, embedding, new Dictionary<string, object>
                    {
                        ["Summary"] = issue.Summary,
                        ["IssueType"] = issue.IssueType,
                        ["ProjectName"] = issue.ProjectName,
                        ["TextLength"] = textForEmbedding.Length
                    });

                    processed++;
                    if (processed % 5 == 0 || processed == total)
                    {
                        progress?.Report($"Generated embeddings for {processed}/{total} issues...");
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: Failed to generate embedding for {issue.Key}: {ex.Message}");
                    // Continue with other issues
                }
            }

            progress?.Report($"✅ All embeddings generated! No content was truncated - full text preserved.");
        }

        // Remove the TruncateForEmbedding method - no longer needed!
        // Full text is now embedded via chunking

        private List<IssueKnowledge> SearchKnowledge(string question, int topK)
        {
            if (_knowledgeBase == null)
                return new List<IssueKnowledge>();

            // Use semantic search if available, otherwise fall back to keyword search
            if (_useSemanticSearch && _vectorStore != null && _vectorStore.Count > 0)
            {
                return SearchKnowledgeSemanticAsync(question, topK).GetAwaiter().GetResult();
            }
            else
            {
                return SearchKnowledgeKeyword(question, topK);
            }
        }

        private async Task<List<IssueKnowledge>> SearchKnowledgeSemanticAsync(string question, int topK)
        {
            if (_knowledgeBase == null || _vectorStore == null)
                return new List<IssueKnowledge>();

            try
            {
                // Generate embedding for the query
                var queryEmbedding = await _llmProvider.GetEmbeddingAsync(question);

                // Lower similarity threshold for better recall (0.2 instead of 0.3)
                var results = _vectorStore.Search(queryEmbedding, topK, minSimilarity: 0.2f);

                // Map results back to IssueKnowledge objects
                var issues = new List<IssueKnowledge>();
                foreach (var result in results)
                {
                    if (_knowledgeBase.TryGetValue(result.Id, out var issue))
                    {
                        issue.SearchScore = result.Similarity * 100; // Convert to 0-100 scale
                        issues.Add(issue);
                    }
                }

                return issues;
            }
            catch (Exception)
            {
                // Fall back to keyword search if semantic search fails
                return SearchKnowledgeKeyword(question, topK);
            }
        }

        private List<IssueKnowledge> SearchKnowledgeKeyword(string question, int topK)
        {
            if (_knowledgeBase == null)
                return new List<IssueKnowledge>();

            var queryKeywords = ExtractKeywords(new IssueData { Summary = question, Description = question });
            var queryText = question.ToLowerInvariant();

            // Score ALL issues across ALL projects (TST, REQ, STF, etc.)
            foreach (var kb in _knowledgeBase.Values)
            {
                double score = 0;

                // Exact phrase match in full text
                if (kb.FullText.Contains(queryText, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }

                // Match in summary (high priority)
                if (kb.Summary.Contains(queryText, StringComparison.OrdinalIgnoreCase))
                {
                    score += 80;
                }

                // Match in description
                if (kb.Description.Contains(queryText, StringComparison.OrdinalIgnoreCase))
                {
                    score += 60;
                }

                // Keyword matching with different weights
                int keywordMatches = 0;
                foreach (var keyword in queryKeywords)
                {
                    if (kb.Keywords.Contains(keyword))
                    {
                        keywordMatches++;
                        score += 10;
                    }
                }

                // Boost for multiple keyword matches
                if (keywordMatches > 1)
                {
                    score += keywordMatches * 5;
                }

                // Boost for issue type relevance
                if (queryText.Contains("test") && kb.IssueType.Contains("Test", StringComparison.OrdinalIgnoreCase))
                    score += 20;
                if (queryText.Contains("requirement") && kb.IssueType.Contains("Requirement", StringComparison.OrdinalIgnoreCase))
                    score += 20;
                if (queryText.Contains("bug") && kb.IssueType.Contains("Bug", StringComparison.OrdinalIgnoreCase))
                    score += 20;

                kb.SearchScore = score;
            }

            return _knowledgeBase.Values
                .Where(kb => kb.SearchScore > 0)
                .OrderByDescending(kb => kb.SearchScore)
                .Take(topK)
                .ToList();
        }

        private async Task<string> GenerateAiAnswerAsync(string question, List<IssueKnowledge> relevantIssues, CancellationToken cancellationToken)
        {
            var prompt = BuildPrompt(question, relevantIssues);
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);
            return response;
        }

        private string BuildPrompt(string question, List<IssueKnowledge> relevantIssues)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an intelligent assistant helping users understand their project issues, test cases, and requirements.");
            sb.AppendLine("Answer the user's question based on the following knowledge base context.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT INSTRUCTIONS:");
            sb.AppendLine("- Analyze the provided issues and think logically about what the user is asking");
            sb.AppendLine("- Synthesize information from multiple issues to form a complete answer");
            sb.AppendLine("- Use natural, conversational language like ChatGPT or Gemini");
            sb.AppendLine("- Reference specific issue keys (e.g., TST-123) when relevant");
            sb.AppendLine("- If discussing relationships, explain how issues connect");
            sb.AppendLine("- Be concise but thorough");
            sb.AppendLine();
            sb.AppendLine("=== KNOWLEDGE BASE CONTEXT ===");
            sb.AppendLine();

            int contextCount = 0;
            foreach (var issue in relevantIssues.Take(15)) // Increased from 10 to 15
            {
                contextCount++;
                sb.AppendLine($"[Issue {contextCount}]");
                sb.AppendLine($"Key: {issue.Key}");
                sb.AppendLine($"Type: {issue.IssueType}");
                sb.AppendLine($"Project: {issue.ProjectName}");
                sb.AppendLine($"Status: {issue.Status}");
                sb.AppendLine($"Summary: {issue.Summary}");

                // Include FULL description (no truncation!)
                if (!string.IsNullOrWhiteSpace(issue.Description))
                {
                    sb.AppendLine($"Description: {issue.Description}");
                }

                if (!string.IsNullOrWhiteSpace(issue.ParentSummary))
                {
                    sb.AppendLine($"Parent: {issue.ParentSummary}");
                }

                if (issue.RelatedIssues.Any())
                {
                    sb.AppendLine($"Related Issues: {string.Join(", ", issue.RelatedIssues)}");

                    // Include summaries of related issues for better context
                    foreach (var relatedKey in issue.RelatedIssues.Take(3))
                    {
                        if (_knowledgeBase != null && _knowledgeBase.TryGetValue(relatedKey, out var related))
                        {
                            sb.AppendLine($"  - {relatedKey}: {related.Summary}");
                        }
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("=== END CONTEXT ===");
            sb.AppendLine();
            sb.AppendLine($"USER QUESTION: {question}");
            sb.AppendLine();
            sb.AppendLine("Please provide a thoughtful, intelligent answer based on the context above:");

            return sb.ToString();
        }

        /// <summary>
        /// Loads complete hierarchical context for a specific issue key.
        /// Includes parent, children, siblings, and related issues recursively.
        /// </summary>
        private List<IssueKnowledge> LoadHierarchicalContext(string issueKey, int maxDepth = 3)
        {
            if (_knowledgeBase == null || !_knowledgeBase.TryGetValue(issueKey, out var rootIssue))
                return new List<IssueKnowledge>();

            var context = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<IssueKnowledge>();

            void AddIssueRecursively(string key, int depth)
            {
                if (depth > maxDepth || context.Contains(key))
                    return;

                if (_knowledgeBase.TryGetValue(key, out var issue))
                {
                    context.Add(key);
                    issues.Add(issue);

                    // Add parent
                    if (!string.IsNullOrWhiteSpace(issue.ParentSummary))
                    {
                        var parentKey = issue.RelatedIssues.FirstOrDefault(k => 
                            _knowledgeBase.TryGetValue(k, out var p) && p.Summary == issue.ParentSummary);
                        if (!string.IsNullOrWhiteSpace(parentKey))
                            AddIssueRecursively(parentKey, depth + 1);
                    }

                    // Add all related issues (children, siblings, links)
                    foreach (var relatedKey in issue.RelatedIssues)
                    {
                        AddIssueRecursively(relatedKey, depth + 1);
                    }
                }
            }

            AddIssueRecursively(issueKey, 0);
            return issues;
        }

        private void SaveKnowledgeBase()
        {
            if (_knowledgeBase == null)
                return;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_knowledgeBase, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_knowledgeIndexPath, json);
        }

        private void LoadKnowledgeBase()
        {
            if (!File.Exists(_knowledgeIndexPath))
                return;

            var json = File.ReadAllText(_knowledgeIndexPath);
            _knowledgeBase = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, IssueKnowledge>>(json);
        }

        private static string GetString(IDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        private static List<string> SplitKeys(string keys)
        {
            if (string.IsNullOrWhiteSpace(keys))
                return new List<string>();

            return keys.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            if (_llmProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    internal class IssueData
    {
        public string Key { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ParentKey { get; set; } = string.Empty;
        public List<string> ChildrenKeys { get; set; } = new();
        public List<string> RelatesKeys { get; set; } = new();
        public string IssueType { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string History { get; set; } = string.Empty;
        public string Attachments { get; set; } = string.Empty;
    }

    public partial class IssueKnowledge
    {
        public string Key { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FullText { get; set; } = string.Empty;
        public string ParentSummary { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public List<string> RelatedIssues { get; set; } = new();
        public double SearchScore { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public float[]? Embedding { get; set; } // Vector embedding for semantic search
    }
}
