using HtmlAgilityPack;
using Monovera;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static Monovera.frmMain;

public class JiraHtmlReportGenerator
{
    // Lookup for all issues by key
    private readonly Dictionary<string, JiraIssue> issueDict;
    // Maps parent issue keys to their child issues
    private readonly Dictionary<string, List<JiraIssue>> childrenByParent;

    // Kept to preserve constructor signature; not used in offline report
    private readonly string jiraEmail;
    private readonly string jiraToken;
    private readonly string jiraBaseUrl;

    // Reference to the tree view (for icons only)
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
        var flatList = new List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, int level)>();

        await CollectIssuesRecursively(rootKey, 0, flatList, progress);

        var numbered = GenerateOutlineNumbers(flatList.Select(x => (x.issue, x.descHtml, x.attachmentsHtml, x.relatedKeys, x.level)).ToList());

        progress?.Report("Creating HTML report...");
        return await CreateHtmlReport(numbered);
    }

    private async Task CollectIssuesRecursively(
        string key,
        int level,
        List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, int level)> result,
        IProgress<string>? progress)
    {
        if (!issueDict.TryGetValue(key, out var issue)) return;

        // Load description, attachments and related keys from local DB
        var (descHtml, attachmentsHtml, relatedKeys) = await FetchOfflineSectionsAsync(key);

        // Optional: rewrite in-description issue anchors to point within the report (local anchors)
        descHtml = RewriteIssueAnchorsToLocal(descHtml);

        // Save for rendering
        issue.RelatedIssueKeys = relatedKeys;
        result.Add((issue, descHtml, attachmentsHtml, relatedKeys, level));
        progress?.Report($"Report generation in progress : Added {issue.Key}...");

        // Sort children using SortingField (natural/alpha via comparer)
        if (childrenByParent.TryGetValue(key, out var children) && children.Count > 0)
        {
            var comparer = new frmMain.AlphanumericComparer();
            var sortedChildren = children.OrderBy(child =>
            {
                if (frmMain.FlatJiraIssueDictionary != null &&
                    frmMain.FlatJiraIssueDictionary.TryGetValue(child.Key, out var dto))
                    return dto.SortingField;
                return child.Summary ?? "";
            }, comparer).ToList();

            foreach (var child in sortedChildren)
                await CollectIssuesRecursively(child.Key, level + 1, result, progress);
        }
    }

    private List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, string number)> GenerateOutlineNumbers(
        List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, int level)> flatList)
    {
        var numbers = new int[32];
        var result = new List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, string number)>();

        foreach (var (issue, descHtml, attachmentsHtml, relatedKeys, level) in flatList)
        {
            numbers[level]++;
            for (int i = level + 1; i < numbers.Length; i++) numbers[i] = 0;
            string number = string.Join('.', numbers.Take(level + 1).Where(n => n > 0));
            result.Add((issue, descHtml, attachmentsHtml, relatedKeys, number));
        }

        return result;
    }

    // Use DB values; do not call Jira REST
    private Task<(string descHtml, string attachmentsHtml, List<string> relatedKeys)> FetchOfflineSectionsAsync(string issueKey)
    {
        string desc = frmMain.GetFieldValueByKey(issueKey, "DESCRIPTION") ?? "";
        string atts = frmMain.GetFieldValueByKey(issueKey, "ATTACHMENTS") ?? "";

        // RELATESKEYS: comma/space-separated
        string relatesRaw = frmMain.GetFieldValueByKey(issueKey, "RELATESKEYS") ?? "";
        var related = !string.IsNullOrWhiteSpace(relatesRaw)
            ? relatesRaw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrWhiteSpace(k))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
            : new List<string>();

        // If description is empty, make it explicit
        if (string.IsNullOrWhiteSpace(desc))
            desc = "<i>No description available</i>";

        return Task.FromResult((desc, atts, related));
    }

    private string RewriteIssueAnchorsToLocal(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        // Convert <a class='issue-link' data-key='KEY'>...</a> to <a href='#issue-KEY'>...</a>
        html = Regex.Replace(
            html,
            @"<a\s+[^>]*class=['""]issue-link['""][^>]*data-key=['""](?<key>[A-Za-z0-9\-]+)['""][^>]*>(?<inner>.*?)</a>",
            m => $"<a href='#issue-{m.Groups["key"].Value}'>{m.Groups["inner"].Value}</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Convert raw links to Jira browse pages that might remain to local anchors if key can be extracted
        html = Regex.Replace(
            html,
            @"<a\s+[^>]*href=['""][^'""]*/browse/(?<key>[A-Za-z0-9\-]+)['""][^>]*>(?<inner>.*?)</a>",
            m => $"<a href='#issue-{m.Groups["key"].Value}'>{m.Groups["inner"].Value}</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return html;
    }

    private async Task<string> CreateHtmlReport(List<(JiraIssue issue, string descHtml, string attachmentsHtml, List<string> relatedKeys, string number)> issues)
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string reportsDir = Path.Combine(appDir, "reports");
        Directory.CreateDirectory(reportsDir);

        string issueKey = issues[0].issue.Key;
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        string filename = Path.Combine(reportsDir, $"Report_{issueKey}_{timestamp}.html");

        // Read monovera.css content
        string cssPath = Path.Combine(appDir, "monovera.css");
        string cssContent = File.Exists(cssPath) ? await File.ReadAllTextAsync(cssPath) : "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset='UTF-8'>");
        // Ensure relative paths would resolve from appDir (but we embed images anyway)
        sb.AppendLine($"  <base href='{new Uri(appDir).AbsoluteUri}' />");
        sb.AppendLine("  <link href='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css' rel='stylesheet' />");
        sb.AppendLine("  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js'></script>");
        sb.AppendLine("  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js'></script>");
        sb.AppendLine("  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-json.min.js'></script>");
        sb.AppendLine("  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet' />");
        sb.AppendLine("  <style>");
        sb.AppendLine(cssContent);
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // TOC
        sb.AppendLine($"<details open style=\"margin-left:0em;\" id=\"TOC\" class=\"issue\">");
        sb.AppendLine($"  <summary>{Escape(issues[0].issue.Summary)} [{issues[0].issue.Key}]</summary>");
        sb.AppendLine($"  <section class=\"desc\">");
        sb.AppendLine("    <ul>");

        int previousLevel = 0;
        foreach (var (issue, _, _, _, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string anchor = $"issue-{issue.Key}";
            string title = Escape(issue.Summary);
            string iconBase64 = GetIconBase64(issue.Key);
            string iconImg = string.IsNullOrEmpty(iconBase64)
                ? ""
                : $"<img class='icon' title='{Escape(issue.Type)}' src=\"data:image/png;base64,{iconBase64}\" style='height:18px;width:18px;vertical-align:middle;margin-right:6px;'>";

            if (level > previousLevel)
            {
                for (int i = previousLevel; i < level; i++)
                    sb.AppendLine("    <ul>");
            }
            else if (level < previousLevel)
            {
                for (int i = level; i < previousLevel; i++)
                    sb.AppendLine("    </ul>");
            }

            sb.AppendLine($"    <li><a href='#{anchor}'>{number} {iconImg} {title} [{issue.Key}] ({Escape(issue.Type)})</a></li>");
            previousLevel = level;
        }
        for (int i = 0; i < previousLevel; i++)
            sb.AppendLine("    </ul>");
        sb.AppendLine("  </section>");
        sb.AppendLine("</details>");
        sb.AppendLine("<hr>");

        // Issue sections
        foreach (var (issue, descHtml, attachmentsHtml, relatedKeys, number) in issues)
        {
            int level = number.Count(c => c == '.');
            string title = Escape(issue.Summary);
            string key = issue.Key;
            string anchor = $"issue-{key}";
            string iconBase64 = GetIconBase64(key);
            string iconImg = string.IsNullOrEmpty(iconBase64)
                ? ""
                : $"<img class='icon' title='{Escape(issue.Type)}' src=\"data:image/png;base64,{iconBase64}\" style='height:36px;width:36px;vertical-align:middle;margin-right:6px;'>";

            // Embed description-local attachments/images as data URIs
            string embeddedDescHtml = EmbedLocalAttachmentsAsDataUris(descHtml);
            // Embed attachments HTML assets (thumbnails, lightbox images, download links) as data URIs
            string embeddedAttachmentsHtml = EmbedLocalAttachmentsAsDataUris(attachmentsHtml);

            sb.AppendLine($"""
<details open style="margin-left:{level * 2}em;" id="{anchor}" class="issue">
  <summary>{iconImg}<span class="number">{number} </span>{title} [{key}]</summary>
  <section class="desc">{embeddedDescHtml}</section>
""");

            //// Attachments block (if any HTML present)
            //if (!string.IsNullOrWhiteSpace(embeddedAttachmentsHtml) &&
            //    embeddedAttachmentsHtml.IndexOf("no-attachments", StringComparison.OrdinalIgnoreCase) < 0)
            //{
            //    sb.AppendLine("<section class='subsection'>");
            //    sb.AppendLine("<details open>");
            //    sb.AppendLine("<summary>Attachments</summary>");
            //    sb.AppendLine(embeddedAttachmentsHtml);
            //    sb.AppendLine("</details>");
            //    sb.AppendLine("</section>");
            //}

            // Related issues
            if (relatedKeys?.Count > 0)
            {
                sb.AppendLine("<section class='subsection'>");
                sb.AppendLine($"<details><summary>Related Issues ({relatedKeys.Count})</summary>");
                sb.AppendLine("<ul>");
                foreach (var relatedKey in relatedKeys)
                {
                    if (issueDict.TryGetValue(relatedKey, out var relatedIssue))
                    {
                        string relatedAnchor = $"issue-{relatedIssue.Key}";
                        string relatedTitle = Escape(relatedIssue.Summary);
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

    // Convert any attachments/... references in src, href, data-src, data-filepath to data: URIs
    private static string EmbedLocalAttachmentsAsDataUris(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;

        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            string attachmentsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "attachments");

            string ToAbs(string value)
            {
                if (string.IsNullOrWhiteSpace(value)) return null;
                string v = value.Trim();

                if (v.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return null; // already embedded

                if (v.StartsWith("./", StringComparison.Ordinal)) v = v.Substring(2);
                if (!v.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase))
                    return null;

                string tail = v.Substring("attachments/".Length).Replace('/', Path.DirectorySeparatorChar);
                string abs = Path.GetFullPath(Path.Combine(attachmentsRoot, tail));
                string rootFull = Path.GetFullPath(attachmentsRoot);
                if (!abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return null; // guard
                return abs;
            }

            string FileToDataUri(string abs)
            {
                if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs)) return null;
                try
                {
                    var bytes = File.ReadAllBytes(abs);
                    string mime = GetMimeTypeByExtension(Path.GetExtension(abs));
                    string b64 = Convert.ToBase64String(bytes);
                    return $"data:{mime};base64,{b64}";
                }
                catch { return null; }
            }

            void ProcessAttr(HtmlNode node, string attrName)
            {
                var val = node.GetAttributeValue(attrName, null);
                var abs = ToAbs(val);
                if (abs == null) return;

                var data = FileToDataUri(abs);
                if (data != null)
                    node.SetAttributeValue(attrName, data);
            }

            foreach (var n in doc.DocumentNode.SelectNodes("//*[@src]") ?? Enumerable.Empty<HtmlNode>())
                ProcessAttr(n, "src");
            foreach (var n in doc.DocumentNode.SelectNodes("//*[@href]") ?? Enumerable.Empty<HtmlNode>())
                ProcessAttr(n, "href");
            foreach (var n in doc.DocumentNode.SelectNodes("//*[@data-src]") ?? Enumerable.Empty<HtmlNode>())
                ProcessAttr(n, "data-src");
            foreach (var n in doc.DocumentNode.SelectNodes("//*[@data-filepath]") ?? Enumerable.Empty<HtmlNode>())
                ProcessAttr(n, "data-filepath");

            return doc.DocumentNode.InnerHtml;
        }
        catch
        {
            return html; // best effort
        }
    }

    private static string GetMimeTypeByExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return "application/octet-stream";
        ext = ext.ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".rar" => "application/vnd.rar",
            ".7z" => "application/x-7z-compressed",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            _ => "application/octet-stream"
        };
    }

    private static string Escape(string? s) => System.Web.HttpUtility.HtmlEncode(s ?? "");


    private string GetIconBase64(string key)
    {
        if (!issueDict.TryGetValue(key, out var issue) || string.IsNullOrEmpty(issue.Type))
            return "";

        var keyPrefix = key.Split('-')[0];
        var projectConfig = frmMain.config?.Projects?.FirstOrDefault(
            p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)
        );
        if (projectConfig == null || projectConfig.Types == null)
            return "";

        string iconFileName = null;
        if (!projectConfig.Types.TryGetValue(issue.Type, out iconFileName))
        {
            var match = projectConfig.Types
                .FirstOrDefault(kvp => kvp.Key.Equals(issue.Type, StringComparison.OrdinalIgnoreCase));
            iconFileName = match.Value;
        }

        if (string.IsNullOrEmpty(iconFileName))
            return "";

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