using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using static Monovera.frmMain;

public class JiraHtmlReportGenerator
{
    private readonly Dictionary<string, JiraIssue> issueDict;
    private readonly Dictionary<string, List<JiraIssue>> childrenByParent;
    private readonly string jiraEmail;
    private readonly string jiraToken;
    private readonly string jiraBaseUrl;
    private readonly TreeView tree;

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

    public async Task<string> GenerateAsync(string rootKey, IProgress<string>? progress = null)
    {
        progress?.Report("Collecting issues...");
        var flatList = new List<(JiraIssue issue, string html, int level)>();
        await CollectIssuesRecursively(rootKey, 0, flatList, progress);

        // Assign outline numbers
        var numbered = GenerateOutlineNumbers(flatList);

        progress?.Report("Creating HTML report...");
        return await CreateHtmlReport(numbered);
    }

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


    private async Task CollectIssuesRecursively(string key, int level, List<(JiraIssue, string, int)> result, IProgress<string>? progress)
    {
        if (!issueDict.TryGetValue(key, out var issue)) return;

        // Unpack the related keys as well
        var (html, attachments, relatedKeys) = await FetchDescriptionAndAttachmentsAsync(key);
        html = ReplaceAttachmentImageUrls(html, attachments);
        html = RewriteImageUrls(html);

        // Assign related keys to issue property
        issue.RelatedIssueKeys = relatedKeys;

        result.Add((issue, html, level));
        progress?.Report($"Report generation in progress : Added {issue.Key}...");

        if (childrenByParent.TryGetValue(key, out var children))
        {
            foreach (var child in children)
                await CollectIssuesRecursively(child.Key, level + 1, result, progress);
        }
    }

    private async Task<(string html, Dictionary<string, string> attachmentUrls, List<string> relatedIssueKeys)> FetchDescriptionAndAttachmentsAsync(string issueKey)
    {
        var attachmentUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relatedIssueKeys = new List<string>();

        try
        {
            var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
            using var client = new HttpClient();
            client.BaseAddress = new Uri(jiraBaseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            var response = await client.GetAsync($"/rest/api/3/issue/{issueKey}?expand=renderedFields&fields=summary,description,attachment,issuelinks");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string? html = null;

            if (doc.RootElement.TryGetProperty("renderedFields", out var rendered) &&
                rendered.TryGetProperty("description", out var desc) &&
                desc.ValueKind == JsonValueKind.String)
            {
                html = desc.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(html))
                html = "<i>No description available</i>";


            // Trim leading/trailing whitespace but keep content otherwise
            html = html.Trim();

            // Get attachments
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

            // Get only "Relates" issue links
            if (doc.RootElement.TryGetProperty("fields", out fields) &&
                fields.TryGetProperty("issuelinks", out var issueLinks) &&
                issueLinks.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in issueLinks.EnumerateArray())
                {
                    // Filter by link type name
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


private string ReplaceAttachmentImageUrls(string html, Dictionary<string, string> attachmentUrls)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "<i>No description available</i>";

        // Replace img src that is exactly an attachment filename with full URL
        foreach (var kvp in attachmentUrls)
        {
            string filename = Regex.Escape(kvp.Key);
            string url = kvp.Value;

            // This pattern matches <img src="filename" ...> or <img src='filename' ...>
            var pattern = $"(<img[^>]+src=[\"']){filename}([\"'][^>]*>)";
            html = Regex.Replace(html, pattern, $"$1{url}$2", RegexOptions.IgnoreCase);
        }

        return html;
    }

  
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

        // Generate TOC with two levels
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

        // Close remaining open <ul>
        for (int i = 0; i < previousLevel; i++)
            sb.AppendLine("</ul>");

        sb.AppendLine("</ul><hr>");


        // Render issues with collapsible sections
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

            // Add related issues section here
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