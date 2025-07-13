using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using static Monovera.frmMain;

/// <summary>
/// Generates a hierarchical HTML report for Jira issues.
/// Traverses the issue tree, fetches descriptions and attachments, and produces a styled HTML file.
/// </summary>
public class JiraHtmlReportGenerator
{
    // Lookup for all issues by key
    private readonly Dictionary<string, JiraIssue> issueDict;
    // Maps parent issue keys to their child issues
    private readonly Dictionary<string, List<JiraIssue>> childrenByParent;
    // Jira authentication details
    private readonly string jiraEmail;
    private readonly string jiraToken;
    private readonly string jiraBaseUrl;
    // Reference to the tree view for icons and navigation
    private readonly TreeView tree;

    /// <summary>
    /// Initializes the report generator with required data and UI references.
    /// </summary>
    public JiraHtmlReportGenerator(
        Dictionary<string, JiraIssue> issueDict,
        Dictionary<string, List<JiraIssue>> childrenByParent,
        string jiraEmail,
        string jiraToken,
        string jiraBaseUrl,
        TreeView tree)
    {
        this.issueDict = issueDict;
        this.childrenByParent = childrenByParent;
        this.jiraEmail = jiraEmail;
        this.jiraToken = jiraToken;
        this.jiraBaseUrl = jiraBaseUrl;
        this.tree = tree;
    }

    /// <summary>
    /// Generates the HTML report for the hierarchy starting from the given root issue key.
    /// </summary>
    /// <param name="rootKey">The root issue key to start the report from.</param>
    /// <param name="progress">Optional progress reporter for UI feedback.</param>
    /// <returns>Path to the generated HTML file.</returns>
    public async Task<string> GenerateAsync(string rootKey, IProgress<string>? progress = null)
    {
        progress?.Report("Collecting issues...");
        var flatList = new List<(JiraIssue issue, string html, int level)>();
        await CollectIssuesRecursively(rootKey, 0, flatList, progress);

        // Assign outline numbers for hierarchical display (e.g. 1, 1.1, 1.2, 2, ...)
        var numbered = GenerateOutlineNumbers(flatList);

        progress?.Report("Creating HTML report...");
        return await CreateHtmlReport(numbered);
    }

    /// <summary>
    /// Generates outline numbers for each issue in the hierarchy (e.g. 1, 1.1, 1.2, 2, ...).
    /// </summary>
    /// <param name="flatList">Flat list of issues with their HTML and hierarchy level.</param>
    /// <returns>List of issues with outline numbers.</returns>
    private List<(JiraIssue issue, string html, string number)> GenerateOutlineNumbers(List<(JiraIssue issue, string html, int level)> flatList)
    {
        var numbers = new int[10];
        var result = new List<(JiraIssue issue, string html, string number)>();

        foreach (var (issue, html, level) in flatList)
        {
            numbers[level]++;
            for (int i = level + 1; i < numbers.Length; i++) numbers[i] = 0;
            string number = string.Join('.', numbers.Take(level + 1).Where(n => n > 0));
            result.Add((issue, html, number));
        }

        return result;
    }

    /// <summary>
    /// Recursively collects issues and their HTML descriptions, starting from the given key.
    /// Adds each issue to the result list with its hierarchy level.
    /// </summary>
    /// <param name="key">Issue key to start from.</param>
    /// <param name="level">Hierarchy level (depth).</param>
    /// <param name="result">List to accumulate results.</param>
    /// <param name="progress">Optional progress reporter.</param>
    private async Task CollectIssuesRecursively(string key, int level, List<(JiraIssue, string, int)> result, IProgress<string>? progress)
    {
        if (!issueDict.TryGetValue(key, out var issue)) return;

        // Fetch description HTML, attachments, and related issue keys
        var (html, attachments, relatedKeys) = await FetchDescriptionAndAttachmentsAsync(key);
        html = ReplaceAttachmentImageUrls(html, attachments);
        html = RewriteImageUrls(html);

        // Assign related keys to the issue for later rendering
        issue.RelatedIssueKeys = relatedKeys;

        result.Add((issue, html, level));
        progress?.Report($"Report generation in progress : Added {issue.Key}...");

        // Recursively collect child issues
        if (childrenByParent.TryGetValue(key, out var children))
        {
            foreach (var child in children)
                await CollectIssuesRecursively(child.Key, level + 1, result, progress);
        }
    }

    /// <summary>
    /// Fetches the rendered HTML description, attachment URLs, and related issue keys for a Jira issue.
    /// </summary>
    /// <param name="issueKey">The Jira issue key.</param>
    /// <returns>Tuple of HTML description, attachment URLs, and related issue keys.</returns>
    private async Task<(string html, Dictionary<string, string> attachmentUrls, List<string> relatedIssueKeys)> FetchDescriptionAndAttachmentsAsync(string issueKey)
    {
        var attachmentUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relatedIssueKeys = new List<string>();

        try
        {
            // Prepare Jira REST API client
            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
            using var client = new HttpClient();
            client.BaseAddress = new Uri(jiraBaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            // Fetch issue details with rendered description and attachments
            var response = await client.GetAsync($"/rest/api/3/issue/{issueKey}?expand=renderedFields&fields=summary,description,attachment,issuelinks");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string? html = null;

            // Extract rendered HTML description
            if (doc.RootElement.TryGetProperty("renderedFields", out var rendered) &&
                rendered.TryGetProperty("description", out var desc) &&
                desc.ValueKind == JsonValueKind.String)
            {
                html = desc.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(html))
                html = "<i>No description available</i>";

            html = html.Trim();

            // Extract attachment URLs
            if (doc.RootElement.TryGetProperty("fields", out var fields) &&
                fields.TryGetProperty("attachment", out var attachments) &&
                attachments.ValueKind == JsonValueKind.Array)
            {
                foreach (var att in attachments.EnumerateArray())
                {
                    if (att.TryGetProperty("filename", out var filenameProp) &&
                        att.TryGetProperty("content", out var contentProp))
                    {
                        var filename = filenameProp.GetString();
                        var contentUrl = contentProp.GetString();
                        if (!string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(contentUrl))
                        {
                            attachmentUrls[filename] = contentUrl;
                        }
                    }
                }
            }

            // Extract related issue keys ("Relates" links)
            if (doc.RootElement.TryGetProperty("fields", out fields) &&
                fields.TryGetProperty("issuelinks", out var issueLinks) &&
                issueLinks.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in issueLinks.EnumerateArray())
                {
                    if (link.TryGetProperty("type", out var type) &&
                        type.TryGetProperty("name", out var typeNameProp) &&
                        typeNameProp.GetString()?.Equals("Relates", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        if (link.TryGetProperty("inwardIssue", out var inwardIssue) &&
                            inwardIssue.TryGetProperty("key", out var keyPropIn))
                        {
                            relatedIssueKeys.Add(keyPropIn.GetString()!);
                        }
                        else if (link.TryGetProperty("outwardIssue", out var outwardIssue) &&
                                 outwardIssue.TryGetProperty("key", out var keyPropOut))
                        {
                            relatedIssueKeys.Add(keyPropOut.GetString()!);
                        }
                    }
                }
            }

            return (html, attachmentUrls, relatedIssueKeys);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error fetching description for {issueKey}: {ex.Message}");
        }

        return ("", new Dictionary<string, string>(), new List<string>());
    }

    /// <summary>
    /// Replaces image URLs in the HTML description with full attachment URLs.
    /// </summary>
    /// <param name="html">HTML description string.</param>
    /// <param name="attachmentUrls">Dictionary of attachment filenames to URLs.</param>
    /// <returns>HTML with image src attributes replaced by full URLs.</returns>
    private string ReplaceAttachmentImageUrls(string html, Dictionary<string, string> attachmentUrls)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "<i>No description available</i>";

        // Replace <img src="filename"> with <img src="full_url">
        foreach (var kvp in attachmentUrls)
        {
            string filename = Regex.Escape(kvp.Key);
            string url = kvp.Value;
            var pattern = $"(<img[^>]+src=[\"']){filename}([\"'][^>]*>)";
            html = Regex.Replace(html, pattern, $"$1{url}$2", RegexOptions.IgnoreCase);
        }

        return html;
    }

    /// <summary>
    /// Creates the final HTML report file from the collected issues.
    /// Includes a table of contents, collapsible sections, and related issues.
    /// </summary>
    /// <param name="issues">List of issues with HTML and outline numbers.</param>
    /// <returns>Path to the generated HTML file.</returns>
    private async Task<string> CreateHtmlReport(List<(JiraIssue issue, string html, string number)> issues)
    {
        string filename = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"{issues[0].issue.Key}_Report.html");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html><head><meta charset='UTF-8'><title>Monovera Report - {issues[0].issue.Key}</title>");

        var css = @"
body {
  font-family: 'IBM Plex Sans', sans-serif;
  margin: 30px;
  background: #f8fcf8;
  color: #1a1a1a;
  font-size: 18px;
  line-height: 1.7;
}

h2 {
  color: #2e4d2e;
  font-size: 1.9em;
  margin-bottom: 20px;
}

details {
  margin-bottom: 30px;
  border: 1px solid #cde0cd;
  border-radius: 6px;
  box-shadow: 0 2px 6px rgba(0, 64, 0, 0.05);
}

summary {
  padding: 14px 20px;
  background-color: #edf7ed;
  cursor: pointer;
  font-weight: 600;
  font-size: 1.2em;
  border-bottom: 1px solid #d0e8d0;
  color: #2e4d2e;
}

.number {
  display: inline-block;
  min-width: 3em;
  color: #5a7f5a;
  font-weight: bold;
  margin-right: 8px;
}

section {
  padding: 16px 20px;
  background-color: #f8fcf8;
}

.subsection h4 {
  margin-top: 20px;
  margin-bottom: 10px;
  font-size: 1.1em;
  color: #345e34;
}

table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  border-radius: 8px;
  background: #f8fcf8;
  box-shadow: 0 2px 8px rgba(0, 64, 0, 0.04);
  margin-bottom: 20px;
  overflow: hidden;
}

th {
  background-color: #e7f5e7;
  color: #204020;
  text-align: left;
  padding: 12px 16px;
  font-weight: bold;
  border-bottom: 2px solid #c4dcc4;
}

td {
  padding: 12px 16px;
  border-bottom: 1px solid #e0eae0;
  color: #2a2a2a;
}

tr:hover td {
  background-color: #f0f8f0;
}

a {
  color: #2e7d32;
  text-decoration: none;
}

a:hover {
  text-decoration: underline;
  color: #1b5e20;
}

pre[class*='language-'] {
  background: #f1f6f1;
  padding: 16px;
  border-radius: 6px;
  overflow-x: auto;
  font-size: 0.9em;
  color: #1b3a1b;
}

.issue {
  margin-bottom: 24px;
}

.issue summary {
  display: flex;
  align-items: center;
}

.issue .icon {
  width: 18px;
  height: 18px;
  margin-right: 6px;
  vertical-align: middle;
}

.number {
  display: inline-block;
  min-width: 3em;
  color: #5a7f5a;
  font-weight: bold;
  margin-right: 8px;
}

";

        sb.AppendLine("<style>");
        sb.AppendLine(css);
        sb.AppendLine("</style>");

        // Generate table of contents (TOC) with outline numbers
        sb.AppendLine($"<h1>Monovera Report - {issues[0].issue.Summary} [{issues[0].issue.Key}]</h1>");
        sb.AppendLine("<ul>");

        int previousLevel = 0;
        foreach (var (issue, _, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string anchor = $"issue-{issue.Key}";
            string title = System.Web.HttpUtility.HtmlEncode(issue.Summary);

            if (level > previousLevel)
            {
                for (int i = previousLevel; i < level; i++)
                    sb.AppendLine("<ul>");
            }
            else if (level < previousLevel)
            {
                for (int i = level; i < previousLevel; i++)
                    sb.AppendLine("</ul>");
            }

            sb.AppendLine($"<li><a href='#{anchor}'>{number} {title} [{issue.Key}]</a></li>");
            previousLevel = level;
        }
        for (int i = 0; i < previousLevel; i++)
            sb.AppendLine("</ul>");
        sb.AppendLine("</ul><hr>");

        // Render each issue as a collapsible section with description and related issues
        foreach (var (issue, html, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string title = System.Web.HttpUtility.HtmlEncode(issue.Summary);
            string key = issue.Key;
            string anchor = $"issue-{key}";
            string iconBase64 = GetIconBase64(key);
            string iconImg = string.IsNullOrEmpty(iconBase64) ? "" : $"<img class='icon' src=\"data:image/png;base64,{iconBase64}\" style='vertical-align:middle;margin-right:6px;'>";

            sb.AppendLine($"""
<details open style="margin-left:{level * 2}em;" id="{anchor}" class="issue">
  <summary>{iconImg}<span class="number">{number}</span>{title} [{key}]</summary>
  <section class="desc">{html}</section>
""");

            // Add related issues section
            if (issue.RelatedIssueKeys?.Count > 0)
            {
                int count = issue.RelatedIssueKeys.Count;
                sb.AppendLine("<section class='subsection'>");
                sb.AppendLine($"<details><summary>Related Issues ({count})</summary>");
                sb.AppendLine("<ul>");
                foreach (var relatedKey in issue.RelatedIssueKeys)
                {
                    if (issueDict.TryGetValue(relatedKey, out var relatedIssue))
                    {
                        string relatedAnchor = $"issue-{relatedIssue.Key}";
                        string relatedTitle = System.Web.HttpUtility.HtmlEncode(relatedIssue.Summary);
                        sb.AppendLine($"<li><a href='#{relatedAnchor}'>{relatedTitle} [{relatedIssue.Key}]</a></li>");
                    }
                    else
                    {
                        sb.AppendLine($"<li>{relatedKey}</li>");
                    }
                }
                sb.AppendLine("</ul>");
                sb.AppendLine("</details>");
                sb.AppendLine("</section>");
            }

            sb.AppendLine("</details>");
        }

        sb.AppendLine("</body></html>");
        await File.WriteAllTextAsync(filename, sb.ToString());
        return filename;
    }

    /// <summary>
    /// Gets the base64-encoded PNG icon for the given issue key from the tree's ImageList.
    /// </summary>
    /// <param name="key">Issue key to find the icon for.</param>
    /// <returns>Base64 string of the icon image, or empty if not found.</returns>
    private string GetIconBase64(string key)
    {
        var node = FindTreeNode(tree.Nodes, key);
        if (node?.ImageKey is string imgKey && tree.ImageList?.Images.ContainsKey(imgKey) == true)
        {
            using var ms = new MemoryStream();
            tree.ImageList.Images[imgKey].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        return "";
    }

    /// <summary>
    /// Recursively searches the tree for a node with the given issue key.
    /// </summary>
    /// <param name="nodes">TreeNodeCollection to search.</param>
    /// <param name="key">Issue key to find.</param>
    /// <returns>The matching TreeNode, or null if not found.</returns>
    private TreeNode? FindTreeNode(TreeNodeCollection nodes, string key)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is string tag && tag == key)
                return node;
            var found = FindTreeNode(node.Nodes, key);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Rewrites relative image URLs in the HTML to absolute URLs using the Jira base URL.
    /// </summary>
    /// <param name="html">HTML string to process.</param>
    /// <returns>HTML with image src attributes rewritten to absolute URLs.</returns>
    private string RewriteImageUrls(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "<i>No description</i>";

        // Use Regex to find <img src="..."> and rewrite relative URLs
        var pattern = "<img\\s+[^>]*src=[\"']([^\"']+)[\"'][^>]*>";
        var replaced = Regex.Replace(html, pattern, match =>
        {
            var src = match.Groups[1].Value;
            if (src.StartsWith("/"))
            {
                var absoluteUrl = jiraBaseUrl.TrimEnd('/') + src;
                return match.Value.Replace(src, absoluteUrl);
            }
            return match.Value;
        }, RegexOptions.IgnoreCase);

        return replaced;
    }
}