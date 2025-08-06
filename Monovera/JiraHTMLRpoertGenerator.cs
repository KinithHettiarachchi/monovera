using Monovera;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
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

        // Use in-memory hierarchy, not tree nodes
        await CollectIssuesRecursively(rootKey, 0, flatList, progress);

        var numbered = GenerateOutlineNumbers(flatList);

        progress?.Report("Creating HTML report...");
        return await CreateHtmlReport(numbered);
    }

    private async Task CollectIssuesRecursively(string key, int level, List<(JiraIssue, string, int)> result, IProgress<string>? progress)
    {
        if (!issueDict.TryGetValue(key, out var issue)) return;

        // Fetch description HTML, attachments, and related issue keys
        var (html, attachments, relatedKeys) = await FetchDescriptionAndAttachmentsAsync(key);
        html = ReplaceAttachmentImageUrls(html, attachments);
        html = await EmbedImagesInHtmlAsync(html);

        // Use frmMain.HandleLinksOfDescriptionSection to process description
        html = frmMain.HandleLinksOfDescriptionSection(html, key);

        // Assign related keys to the issue for later rendering
        issue.RelatedIssueKeys = relatedKeys;

        result.Add((issue, html, level));
        progress?.Report($"Report generation in progress : Added {issue.Key}...");

        // Find project config for this issue
        var keyPrefix = key.Split('-')[0];
        var projectConfig = frmMain.config.Projects.FirstOrDefault(
            p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
        );
        string sortingField = projectConfig?.SortingField ?? "summary";

        // Sort children using sortingField
        if (childrenByParent.TryGetValue(key, out var children) && children.Count > 0)
        {
            var comparer = new frmMain.AlphanumericComparer();
            var sortedChildren = children.OrderBy(child =>
            {
                if (frmMain.issueDtoDict != null && frmMain.issueDtoDict.TryGetValue(child.Key, out var dto))
                    return dto.SortingField ?? "";
                return child.Summary ?? "";
            }, comparer).ToList();

            foreach (var child in sortedChildren)
                await CollectIssuesRecursively(child.Key, level + 1, result, progress);
        }
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
    /// Downloads images referenced in the issue description HTML, replaces their URLs with base64-encoded data URIs.
    /// </summary>
    /// <param name="html">HTML description string.</param>
    /// <returns>HTML with embedded images as base64 data URIs.</returns>
    private async Task<string> EmbedImagesInHtmlAsync(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        // Regex to find <img src="...">
        var pattern = "<img\\s+[^>]*src=[\"']([^\"']+)[\"'][^>]*>";
        var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            var src = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(src))
                continue;

            try
            {
                // Handle relative URLs
                string imageUrl = src.StartsWith("/") ? jiraBaseUrl.TrimEnd('/') + src : src;

                // Download image with Jira authentication
                var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                var response = await client.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                    continue;

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
                var base64 = Convert.ToBase64String(imageBytes);
                var dataUri = $"data:{contentType};base64,{base64}";

                // Replace src in HTML with base64 data URI
                html = html.Replace(src, dataUri);
            }
            catch
            {
                // Ignore image download errors, keep original src
            }
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
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string reportsDir = Path.Combine(appDir, "reports");
        Directory.CreateDirectory(reportsDir);

        string issueKey = issues[0].issue.Key;
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string filename = Path.Combine(reportsDir, $"Report_{issueKey}_{timestamp}.html");

        // Read monovera.css content
        string cssPath = Path.Combine(appDir, "monovera.css");
        string cssContent = "";
        if (File.Exists(cssPath))
        {
            cssContent = await File.ReadAllTextAsync(cssPath);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html><head><meta charset='UTF-8'><title>Report [{issues[0].issue.Key}]</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(cssContent);
        sb.AppendLine("</style>");
        sb.AppendLine("</head><body>");

        // Generate table of contents (TOC) with outline numbers
        sb.AppendLine($"<details open style=\"margin-left:0em;\" id=\"TOC\" class=\"issue\">\r\n  <summary>{issues[0].issue.Summary} [{issues[0].issue.Key}]</summary>\r\n  <section class=\"desc\">");
        sb.AppendLine("<ul>");

        int previousLevel = 0;
        foreach (var (issue, _, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string anchor = $"issue-{issue.Key}";
            string title = System.Web.HttpUtility.HtmlEncode(issue.Summary);
            string iconBase64 = GetIconBase64(issue.Key);
            string iconImg = string.IsNullOrEmpty(iconBase64)? "" : $"<img class='icon' title='{issue.Type}' src=\"data:image/png;base64,{iconBase64}\" style='height:18px;width:18px;vertical-align:middle;margin-right:6px;'>";

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

            sb.AppendLine($"<li><a href='#{anchor}'>{number} {iconImg} {title} [{issue.Key}] ({issue.Type})</a></li>");
            previousLevel = level;
        }
        for (int i = 0; i < previousLevel; i++)
            sb.AppendLine("</ul>");
        sb.AppendLine("\r\n</section>\r\n</details>\r\n<hr>");

        // Render each issue as a collapsible section with description and related issues
        foreach (var (issue, html, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string title = System.Web.HttpUtility.HtmlEncode(issue.Summary);
            string key = issue.Key;
            string anchor = $"issue-{key}";
            string iconBase64 = GetIconBase64(key);
            string iconImg = string.IsNullOrEmpty(iconBase64)? "" : $"<img class='icon' title='{issue.Type}' src=\"data:image/png;base64,{iconBase64}\" style='height:36px;width:36px;vertical-align:middle;margin-right:6px;'>";

            sb.AppendLine($"""
<details open style="margin-left:{level * 2}em;" id="{anchor}" class="issue">
  <summary>{iconImg}<span class="number">{number} </span>{title} [{key}]</summary>
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
        // Get issue type
        if (!issueDict.TryGetValue(key, out var issue) || string.IsNullOrEmpty(issue.Type))
            return "";

        // Find project config by key prefix
        var keyPrefix = key.Split('-')[0];
        var projectConfig = frmMain.config?.Projects?.FirstOrDefault(
            p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
        );
        if (projectConfig == null || projectConfig.Types == null)
            return "";

        // Find icon filename for issue type
        string iconFileName = null;
        if (projectConfig.Types.TryGetValue(issue.Type, out iconFileName))
        {
            // Direct match
        }
        else
        {
            // Fallback: case-insensitive search
            var match = projectConfig.Types
                .FirstOrDefault(kvp => kvp.Key.Equals(issue.Type, StringComparison.OrdinalIgnoreCase));
            iconFileName = match.Value;
        }

        if (string.IsNullOrEmpty(iconFileName))
            return "";

        // Load image from images folder
        string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", iconFileName);
        if (!File.Exists(imagePath))
            return "";

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return "";
        }
    }
}