using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using SharpSvn;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using System.Drawing.Drawing2D;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using static System.Net.Mime.MediaTypeNames;
using System.Security.Policy;
using System.Drawing;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Monovera
{
    public partial class frmMain : Form
    {
        public static Dictionary<string, List<JiraIssue>> childrenByParent = new();
        public static Dictionary<string, JiraIssue> issueDict = new();
        public static string root_key = "";
        public static List<string> projectList = new();
        public static string jiraBaseUrl = "";
        public static string jiraEmail = "";
        public static string jiraToken = "";
        public static Dictionary<string, string> typeIcons;
        public static Dictionary<string, string> statusIcons;

        private TabControl tabDetails;


        private class JiraConfigRoot
        {
            public JiraAuth Jira { get; set; }
            public List<JiraProjectConfig> Projects { get; set; }
        }

        private class JiraAuth
        {
            public string Url { get; set; }
            public string Email { get; set; }
            public string Token { get; set; }
        }

        private class JiraProjectConfig
        {
            public string Project { get; set; }
            public string Root { get; set; }
            public Dictionary<string, string> Types { get; set; }
            public Dictionary<string, string> Status { get; set; }
        }


        public class JiraIssue
        {
            public string Key { get; set; }
            public string Summary { get; set; }
            public string Type { get; set; }
            public string ParentKey { get; set; }
        }

        public class JiraIssueLink
        {
            public string LinkTypeName { get; set; } = "";
            public string OutwardIssueKey { get; set; } = "";
            public string OutwardIssueSummary { get; set; } = "";
            public string OutwardIssueType { get; set; } = "";
        }

        public class JiraIssueDto
        {
            public string Key { get; set; }
            public string Summary { get; set; }
            public string Type { get; set; }
            public List<JiraIssueLink> IssueLinks { get; set; } = new();
        }

        public frmMain()
        {
            InitializeComponent();

            //Tabs for right side
            tabDetails = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "tabDetails"
            };

            tabDetails.SelectedIndexChanged += TabDetails_SelectedIndexChanged;
            tabDetails.ShowToolTips = true;
            tabDetails.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabDetails.DrawItem += TabDetails_DrawItem;
            tabDetails.MouseDown += tabDetails_MouseDown;
            tabDetails.ItemSize = new Size(200, 30); // ← Set your custom width and height
            tabDetails.Padding = new Point(40, 5); // space for X button
            panelTabs.Controls.Add(tabDetails);
        }

        private void TabDetails_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tabControl = sender as TabControl;
            var tabPage = tabControl.TabPages[e.Index];
            var tabRect = tabControl.GetTabRect(e.Index);

            using var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool isSelected = tabControl.SelectedIndex == e.Index;

            // Background
            using var background = new SolidBrush(isSelected ? Color.White : Color.LightGray);
            g.FillRectangle(background, tabRect);

            int padding = 6;
            int iconSize = 16;
            int closeSize = 16;
            int spacing = 6;

            int xOffset = tabRect.X + padding;

            // Draw icon if available
            if (tabControl.ImageList != null &&
                !string.IsNullOrEmpty(tabPage.ImageKey) &&
                tabControl.ImageList.Images.ContainsKey(tabPage.ImageKey))
            {
                g.DrawImage(tabControl.ImageList.Images[tabPage.ImageKey], xOffset, tabRect.Y + (tabRect.Height - iconSize) / 2, iconSize, iconSize);
                xOffset += iconSize + spacing;
            }

            // Draw tab text
            string text = tabPage.Text;
            using var font = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Regular);
            using var textBrush = new SolidBrush(Color.Black);

            SizeF textSize = g.MeasureString(text, font);
            float textY = tabRect.Y + (tabRect.Height - textSize.Height) / 2;
            g.DrawString(text, font, textBrush, xOffset, textY);
            xOffset += (int)textSize.Width + spacing;

            // Draw close "X" button as square with rounded corners
            int closeX = tabRect.Right - closeSize - padding;
            int closeY = tabRect.Y + (tabRect.Height - closeSize) / 2;
            var closeRect = new Rectangle(closeX, closeY, closeSize, closeSize);

            using var closeBg = new SolidBrush(Color.FromArgb(220, 50, 50));
            using var closeFg = new SolidBrush(Color.White);
            using (var path = RoundedRect(closeRect, 4)) // Rounded square with 4px corner radius
            {
                g.FillPath(closeBg, path);
            }

            using var closeFont = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);
            var stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("×", closeFont, closeFg, closeRect, stringFormat);

            // Store the close rect for MouseDown event
            tabPage.Tag = closeRect;
        }

        private GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.StartFigure();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void tabDetails_MouseDown(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < tabDetails.TabPages.Count; i++)
            {
                var tab = tabDetails.TabPages[i];
                if (tab.Tag is Rectangle closeRect && closeRect.Contains(e.Location))
                {
                    tabDetails.TabPages.Remove(tab);
                    break;
                }
            }
        }

        private void InitializeIcons()
        {
            ImageList icons = new ImageList();
            icons.ImageSize = new Size(18, 18);
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");

            var addedImages = new HashSet<string>();
            foreach (var kv in typeIcons.Concat(statusIcons))
            {
                string key = kv.Key;
                string fileName = kv.Value;
                string fullPath = Path.Combine(basePath, fileName);

                if (File.Exists(fullPath) && !addedImages.Contains(key))
                {
                    icons.Images.Add(key, System.Drawing.Image.FromFile(fullPath));
                    addedImages.Add(key);
                }
            }

            tree.ImageList = icons;
        }


        public static string GetIconForType(string issueType)
        {
            if (string.IsNullOrWhiteSpace(issueType))
                return "";

            // Try full match
            if (typeIcons.ContainsKey(issueType))
                return issueType;

            // Try startsWith (fallback, case-insensitive)
            foreach (var key in typeIcons.Keys)
            {
                if (issueType.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return "";
        }

        public static string GetIconForStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "";

            if (statusIcons.ContainsKey(status))
                return status;

            foreach (var key in statusIcons.Keys)
            {
                if (status.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return "";
        }

        private void LoadConfigurationFromJson()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.json");
            if (!File.Exists(path))
            {
                MessageBox.Show("Missing configuration.json file.");
                return;
            }

            var configText = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<JiraConfigRoot>(configText);

            jiraBaseUrl = config.Jira.Url;
            jiraEmail = config.Jira.Email;
            jiraToken = config.Jira.Token;

            projectList = config.Projects.Select(p => p.Project).ToList();
            root_key = string.Join(",", config.Projects.Select(p => p.Root));

            // Default to first project for type/status icons
            typeIcons = config.Projects[0].Types;
            statusIcons = config.Projects[0].Status;
        }


        private async void frmMain_Load(object sender, EventArgs e)
        {
            LoadConfigurationFromJson();
            InitializeIcons();
            await LoadAllProjectsToTreeAsync();
        }

        private async Task LoadAllProjectsToTreeAsync(bool forceSync = false)
        {
            pbProgress.Visible = true;
            pbProgress.Value = 0;
            lblProgress.Visible = true;
            lblProgress.Text = "Loading...";

            issueDict.Clear();
            childrenByParent.Clear();

            foreach (var project in projectList)
            {
                string cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{project}.json");
                List<JiraIssueDto> issues;

                if (!forceSync && File.Exists(cacheFile))
                {
                    // Load from cache
                    string cachedJson = await File.ReadAllTextAsync(cacheFile);
                    issues = ParseIssuesFromJson(cachedJson);
                }
                else
                {
                    // Delete cache if it exists
                    if (File.Exists(cacheFile))
                        File.Delete(cacheFile);

                    var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(jiraBaseUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                    string jql = $"project={project} ORDER BY key ASC";

                    var progress = new Progress<(int completed, int total)>(p =>
                    {
                        pbProgress.Maximum = p.total;
                        pbProgress.Value = Math.Min(p.completed, p.total);
                        lblProgress.Text = $"{p.completed} / {p.total} ({(p.completed * 100 / p.total)}%)";
                    });

                    string allJson = await DownloadAllIssuesJson(client, jql, progress);
                    await File.WriteAllTextAsync(cacheFile, allJson);
                    issues = ParseIssuesFromJson(allJson);
                }

                BuildDictionaries(issues);
            }

            pbProgress.Visible = false;
            lblProgress.Visible = false;

            tree.Invoke(() =>
            {
                tree.Nodes.Clear();
                tree.BeginUpdate();

                var rootKeys = root_key.Split(',').Select(k => k.Trim()).ToHashSet();

                foreach (var rootIssue in issueDict.Values.Where(i => i.ParentKey == null || !issueDict.ContainsKey(i.ParentKey)))
                {
                    if (!rootKeys.Contains(rootIssue.Key)) continue;

                    var rootNode = CreateTreeNode(rootIssue);
                    AddChildNodesRecursively(rootNode, rootIssue.Key);
                    tree.Nodes.Add(rootNode);
                    rootNode.Expand();
                }

                tree.EndUpdate();
            });
        }


        private void BuildDictionaries(List<JiraIssueDto> issues)
        {
            foreach (var issue in issues)
            {
                issueDict[issue.Key] = new JiraIssue
                {
                    Key = issue.Key,
                    Summary = issue.Summary,
                    Type = issue.Type,
                    ParentKey = null
                };
            }

            foreach (var issue in issues)
            {
                foreach (var link in issue.IssueLinks)
                {
                    if (link.LinkTypeName == "Parent/Child")
                    {
                        if (issueDict.TryGetValue(link.OutwardIssueKey, out var child))
                        {
                            if (string.IsNullOrEmpty(child.ParentKey))
                                child.ParentKey = issue.Key;

                            if (!childrenByParent.ContainsKey(issue.Key))
                                childrenByParent[issue.Key] = new List<JiraIssue>();

                            if (!childrenByParent[issue.Key].Any(c => c.Key == child.Key))
                                childrenByParent[issue.Key].Add(child);
                        }
                    }
                }
            }
        }

        private void AddChildNodesRecursively(TreeNode parentNode, string parentKey)
        {
            if (childrenByParent.TryGetValue(parentKey, out var children))
            {
                foreach (var child in children)
                {
                    var childNode = CreateTreeNode(child);
                    AddChildNodesRecursively(childNode, child.Key);
                    parentNode.Nodes.Add(childNode);
                }
            }
        }

        private List<JiraIssueDto> ParseIssuesFromJson(string json)
        {
            var issues = new List<JiraIssueDto>();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var issuesArray = root.GetProperty("issues").EnumerateArray();

            foreach (var issue in issuesArray)
            {
                var key = issue.GetProperty("key").GetString();
                var fields = issue.GetProperty("fields");
                var summary = fields.GetProperty("summary").GetString();
                var type = fields.GetProperty("issuetype").GetProperty("name").GetString();

                var issueLinksList = new List<JiraIssueLink>();

                if (fields.TryGetProperty("issuelinks", out var links) && links.ValueKind == JsonValueKind.Array)
                {
                    foreach (var link in links.EnumerateArray())
                    {
                        var linkTypeName = link.GetProperty("type").GetProperty("name").GetString();
                        if (linkTypeName == "Parent/Child" && link.TryGetProperty("outwardIssue", out var outward))
                        {
                            var outwardKey = outward.GetProperty("key").GetString();
                            var outwardFields = outward.GetProperty("fields");
                            var outwardSummary = outwardFields.GetProperty("summary").GetString();
                            var outwardType = outwardFields.GetProperty("issuetype").GetProperty("name").GetString();

                            issueLinksList.Add(new JiraIssueLink
                            {
                                LinkTypeName = linkTypeName,
                                OutwardIssueKey = outwardKey,
                                OutwardIssueSummary = outwardSummary,
                                OutwardIssueType = outwardType
                            });
                        }
                    }
                }

                issues.Add(new JiraIssueDto
                {
                    Key = key,
                    Summary = summary,
                    Type = type,
                    IssueLinks = issueLinksList
                });
            }

            return issues;
        }

        private async Task<string> DownloadAllIssuesJson(HttpClient client, string jql, IProgress<(int completed, int total)> progress)
        {
            const int pageSize = 100;
            const int maxParallelism = 5;
            const int maxRequestsPerMinute = 300;
            const int delayBetweenBatchesMs = 60000 / (maxRequestsPerMinute / maxParallelism);

            var totalResponse = await client.GetAsync($"/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt=0&maxResults=1");
            totalResponse.EnsureSuccessStatusCode();
            var totalJson = await totalResponse.Content.ReadAsStringAsync();
            using var totalDoc = JsonDocument.Parse(totalJson);
            int totalIssues = totalDoc.RootElement.GetProperty("total").GetInt32();
            int totalPages = (int)Math.Ceiling(totalIssues / (double)pageSize);

            var allIssues = new List<JsonElement>();
            int completed = 0;

            for (int batchStart = 0; batchStart < totalPages; batchStart += maxParallelism)
            {
                var batchTasks = new List<Task<JsonElement[]>>();

                for (int i = 0; i < maxParallelism && (batchStart + i) < totalPages; i++)
                {
                    int startAt = (batchStart + i) * pageSize;
                    batchTasks.Add(Task.Run(async () =>
                    {
                        var res = await client.GetAsync($"/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={pageSize}&fields=summary,issuetype,issuelinks");
                        res.EnsureSuccessStatusCode();
                        var json = await res.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        return doc.RootElement.GetProperty("issues")
                            .EnumerateArray()
                            .Select(e => JsonDocument.Parse(e.GetRawText()).RootElement.Clone())
                            .ToArray();
                    }));
                }

                var batchResults = await Task.WhenAll(batchTasks);
                foreach (var result in batchResults)
                {
                    allIssues.AddRange(result);
                    completed += result.Length;
                    progress?.Report((completed, totalIssues));
                }

                if ((batchStart + maxParallelism) < totalPages)
                    await Task.Delay(delayBetweenBatchesMs);
            }

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("issues");
                writer.WriteStartArray();
                foreach (var issue in allIssues)
                    issue.WriteTo(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private TreeNode CreateTreeNode(JiraIssue issue)
        {
            //string typeKey = GetIconForType(issue.Type);
            //string statusKey = GetIconForStatus(issue.Status);
            //string iconKey = !string.IsNullOrEmpty(statusKey) ? statusKey : typeKey;

            string iconKey = GetIconForType(issue.Type); // or combine with status if needed
            return new TreeNode($"{issue.Summary} [{issue.Key}]")
            {
                Tag = issue.Key,
                ImageKey = iconKey,
                SelectedImageKey = iconKey
            };
        }

        private async void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not string issueKey || string.IsNullOrWhiteSpace(issueKey))
                return;

            string iconUrl = null;
            if (tree.ImageList != null && e.Node.ImageKey != null && tree.ImageList.Images.ContainsKey(e.Node.ImageKey))
            {
                using var ms = new MemoryStream();
                tree.ImageList.Images[e.Node.ImageKey].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                string base64 = Convert.ToBase64String(ms.ToArray());
                iconUrl = $"data:image/png;base64,{base64}";
            }

            foreach (TabPage page in tabDetails.TabPages)
            {
                if (page.Text == issueKey)
                {
                    tabDetails.SelectedTab = page;
                    return;
                }
            }

            try
            {
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                using var client = new HttpClient();
                client.BaseAddress = new Uri(jiraBaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                var response = await client.GetAsync($"/rest/api/3/issue/{issueKey}?expand=renderedFields,changelog");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Defensive get for fields
                if (!root.TryGetProperty("fields", out var fields))
                    throw new Exception("Missing 'fields' in response.");

                string summary = "";
                if (fields.TryGetProperty("summary", out var summaryProp) && summaryProp.ValueKind == JsonValueKind.String)
                    summary = summaryProp.GetString();

                string status = fields.TryGetProperty("status", out var statusProp) &&
                statusProp.TryGetProperty("name", out var statusName)
                ? statusName.GetString() ?? ""
                : "";

                string lastUpdated = fields.TryGetProperty("updated", out var updatedProp)
                                ? DateTime.TryParse(updatedProp.GetString(), out var dt)
                                    ? dt.ToString("yyyy-MM-dd HH:mm")
                                    : updatedProp.GetString()
                                : "N/A";

                string statusIcon = "";
                string iconKeyStatus = GetIconForStatus(status);
                if (!string.IsNullOrEmpty(iconKeyStatus) && tree.ImageList.Images.ContainsKey(iconKeyStatus))
                {
                    using var ms = new MemoryStream();
                    tree.ImageList.Images[iconKeyStatus].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    statusIcon = $"<img src='data:image/png;base64,{base64}' style='height: 16px; vertical-align: middle; margin-right: 6px;'>";
                }

                string issueUrl = $"{jiraBaseUrl}/browse/{issueKey}";


                string htmlDesc = "";
                if (root.TryGetProperty("renderedFields", out var renderedFields) &&
                    renderedFields.TryGetProperty("description", out var descProp) &&
                    descProp.ValueKind == JsonValueKind.String)
                {
                    htmlDesc = descProp.GetString() ?? "";
                }
                string resolvedDesc = ReplaceJiraLinksAndSVNFeatures(htmlDesc);

                string encodedSummary = WebUtility.HtmlEncode(summary);
                string iconImg = string.IsNullOrEmpty(iconUrl) ? "" : $"<img src='{iconUrl}' style='height: 24px; vertical-align: middle; margin-right: 8px;'>";
                string headerLine = $"{iconImg}<strong>{encodedSummary} [{issueKey}]</strong>";

                string encodedJson = WebUtility.HtmlEncode(FormatJson(json));

                string BuildLinksTable(string title, string linkType, string prop)
                {
                    var sb = new StringBuilder();
                    int matchCount = 0;

                    sb.AppendLine($"<div class='subsection'><h4>{title}</h4>");

                    if (fields.TryGetProperty("issuelinks", out var links))
                    {
                        var tableRows = new StringBuilder();

                        foreach (var link in links.EnumerateArray())
                        {
                            if (link.TryGetProperty("type", out var typeProp) &&
                                typeProp.TryGetProperty("name", out var nameProp) &&
                                nameProp.GetString() == linkType)
                            {
                                JsonElement issueElem = default;

                                if (prop == null)
                                {
                                    if (!link.TryGetProperty("inwardIssue", out issueElem))
                                        issueElem = link.TryGetProperty("outwardIssue", out var outw) ? outw : default;
                                }
                                else
                                {
                                    link.TryGetProperty(prop, out issueElem);
                                }

                                if (issueElem.ValueKind == JsonValueKind.Object)
                                {
                                    var key = issueElem.GetProperty("key").GetString() ?? "";
                                    var sum = issueElem.GetProperty("fields").TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";

                                    TreeNode foundNode = FindNodeByKey(tree.Nodes, key);
                                    string iconImgInner = "";
                                    if (foundNode != null &&
                                        !string.IsNullOrEmpty(foundNode.ImageKey) &&
                                        tree.ImageList != null &&
                                        tree.ImageList.Images.ContainsKey(foundNode.ImageKey))
                                    {
                                        using var ms = new MemoryStream();
                                        tree.ImageList.Images[foundNode.ImageKey].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                        var base64 = Convert.ToBase64String(ms.ToArray());
                                        iconImgInner = $"<img src='data:image/png;base64,{base64}' style='height:16px; vertical-align:middle; margin-right:6px;' />";
                                    }

                                    tableRows.AppendLine($"<tr><td><a href='#' data-key='{key}'>{iconImgInner}{WebUtility.HtmlEncode(sum)} [{key}]</a></td></tr>");
                                    matchCount++;
                                }
                            }
                        }

                        if (matchCount > 0)
                        {
                            sb.AppendLine("<table><tbody>");
                            sb.Append(tableRows);
                            sb.AppendLine("</tbody></table>");
                        }
                        else
                        {
                            sb.AppendLine($"<div class='no-links'>No {title} issues found.</div>");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"<div class='no-links'>No {title} issues found.</div>");
                    }

                    sb.AppendLine("</div>");
                    return sb.ToString();
                }



                string attachmentsHtml = "";
                if (root.TryGetProperty("fields", out var fieldsAttachment) &&
                    fieldsAttachment.TryGetProperty("attachment", out var attachmentsArray) &&
                    attachmentsArray.ValueKind == JsonValueKind.Array)
                {
                    if (!attachmentsArray.EnumerateArray().Any())
                    {
                        attachmentsHtml = "<div class='no-attachments'>No attachments found.</div>";
                    }
                    else
                    {
                        var authTokenAttachment = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                        using var clientAttachment = new HttpClient();
                        clientAttachment.BaseAddress = new Uri(jiraBaseUrl);
                        clientAttachment.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authTokenAttachment);

                        var tempDir = Path.Combine(Path.GetTempPath(), "JiraAttachments");
                        Directory.CreateDirectory(tempDir);

                        var sb = new StringBuilder();
                        string uniqueId = Guid.NewGuid().ToString("N");

                        sb.AppendLine($@"
<div class='attachments-wrapper' id='wrapper-{uniqueId}'>
  <button class='scroll-btn left' onclick='scrollAttachments(""{uniqueId}"", -1)'>&lt;</button>
  <div class='attachments-strip' id='strip-{uniqueId}'>");

                        foreach (var att in attachmentsArray.EnumerateArray())
                        {
                            string fileName = att.GetProperty("filename").GetString() ?? "unknown";
                            string contentUrl = att.GetProperty("content").GetString() ?? "";
                            string thumbnailUrl = att.TryGetProperty("thumbnail", out var thumbProp) ? thumbProp.GetString() ?? "" : "";
                            string mimeType = att.TryGetProperty("mimeType", out var mimeProp) ? mimeProp.GetString() ?? "" : "";
                            string fileExtension = Path.GetExtension(fileName).ToLower();
                            string created = att.TryGetProperty("created", out var createdProp) ? createdProp.GetString() ?? "" : "";
                            string author = att.TryGetProperty("author", out var authorProp) &&
                                            authorProp.TryGetProperty("displayName", out var authorNameProp)
                                            ? authorNameProp.GetString() ?? "Unknown"
                                            : "Unknown";

                            bool isImage = mimeType.StartsWith("image/") || fileExtension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";

                            string localFilePath = Path.Combine(tempDir, fileName);

                            try
                            {
                                if (File.Exists(localFilePath))
                                {
                                    File.SetAttributes(localFilePath, FileAttributes.Normal);
                                    File.Delete(localFilePath);
                                }

                                var fileBytes = clientAttachment.GetByteArrayAsync(contentUrl).Result;
                                File.WriteAllBytes(localFilePath, fileBytes);
                            }
                            catch
                            {
                                continue;
                            }

                            string thumbHtml;
                            if (isImage)
                            {
                                try
                                {
                                    using var responseAttachment = clientAttachment.GetAsync(!string.IsNullOrEmpty(thumbnailUrl) ? thumbnailUrl : contentUrl).Result;
                                    responseAttachment.EnsureSuccessStatusCode();
                                    var bytes = responseAttachment.Content.ReadAsByteArrayAsync().Result;
                                    var base64 = Convert.ToBase64String(bytes);
                                    var thumbMime = responseAttachment.Content.Headers.ContentType?.MediaType ?? "image/png";
                                    thumbHtml = $"<img src=\"data:{thumbMime};base64,{base64}\" alt=\"{fileName}\" title=\"{fileName}\" />";
                                }
                                catch
                                {
                                    thumbHtml = "<div class='attachment-placeholder'>🖼️</div>";
                                }
                            }
                            else
                            {
                                string icon = fileExtension switch
                                {
                                    ".pdf" => "📄",
                                    ".doc" or ".docx" => "📝",
                                    ".xls" or ".xlsx" => "📊",
                                    ".zip" or ".rar" => "🗜️",
                                    ".txt" => "📃",
                                    _ => "📁"
                                };
                                thumbHtml = $"<div class='attachment-placeholder'>{icon}</div>";
                            }

                            string createdDisplay = DateTime.TryParse(created, out var createdDt)
                                ? createdDt.ToString("yyyy-MM-dd HH:mm")
                                : "";

                            string fileSizeDisplay = "";
                            if (att.TryGetProperty("size", out var sizeProp))
                            {
                                long sizeBytes = sizeProp.GetInt64();
                                fileSizeDisplay = $"{(sizeBytes / 1024.0):0.#} KB";
                            }

                            string encodedLocalPath = "file:///" + Uri.EscapeDataString(localFilePath.Replace("\\", "/"));
                            string thumbWrapper = isImage
                                ? $"<a href='#' class='preview-image' data-src='{encodedLocalPath}' title='Preview {fileName}'>{thumbHtml}</a>"
                                : $"<a class='attachment-link' href='{encodedLocalPath}' target='_blank' title='{fileName}'>{thumbHtml}</a>";

                            sb.AppendLine($@"
<div class='attachment-card'>
  {thumbWrapper}
  <div class='attachment-filename'>{HttpUtility.HtmlEncode(fileName)}</div>
  <div class='attachment-meta'>
    {fileSizeDisplay}<br/>
    {createdDisplay}<br/>
    by {HttpUtility.HtmlEncode(author)}
  </div>
  <div>
    <a href='#' data-filepath='{encodedLocalPath}' title='Download {HttpUtility.HtmlEncode(fileName)}' class='download-btn'>⬇️ Download</a>
  </div>
</div>");
                        }

                        sb.AppendLine($@"
  </div>
  <button class='scroll-btn right' onclick='scrollAttachments(""{uniqueId}"", 1)'>&gt;</button>
</div>
<script>
function scrollAttachments(id, direction) {{
  const strip = document.getElementById('strip-' + id);
  if (strip) {{
    strip.scrollBy({{ left: direction * 200, behavior: 'smooth' }});
  }}
}}
</script>");

                        attachmentsHtml = sb.ToString();
                    }
                }


                string linksHtml =
                    BuildLinksTable("Parent", "Parent/Child", "inwardIssue") +
                    BuildLinksTable("Children", "Parent/Child", "outwardIssue") +
                    BuildLinksTable("Related", "Relates", null);

                string historyHtml = "";
                if (root.TryGetProperty("changelog", out var changelog) &&
                    changelog.TryGetProperty("histories", out var histories))
                {
                    var grouped = histories.EnumerateArray()
                        .Select(h =>
                        {
                            var createdRaw = h.GetProperty("created").GetString();
                            if (!DateTime.TryParse(createdRaw, out var created))
                                created = DateTime.MinValue;

                            var author = "";
                            if (h.TryGetProperty("author", out var authorProp) &&
                                authorProp.TryGetProperty("displayName", out var displayNameProp))
                                author = displayNameProp.GetString() ?? "";

                            var items = h.GetProperty("items").EnumerateArray()
                                .Select(item =>
                                {
                                    var field = item.GetProperty("field").GetString();
                                    var from = item.TryGetProperty("fromString", out var fromVal) ? fromVal.GetString() ?? "null" : "null";
                                    var to = item.TryGetProperty("toString", out var toVal) ? toVal.GetString() ?? "null" : "null";

                                    string icon = field?.ToLower() switch
                                    {
                                        "status" => "🟢",
                                        "assignee" => "👤",
                                        "priority" => "⚡",
                                        "summary" => "📝",
                                        "description" => "📄",
                                        _ => "🔧"
                                    };

                                    string highlight = field?.ToLower() switch
                                    {
                                        "status" => "highlight-status",
                                        "assignee" => "highlight-assignee",
                                        "priority" => "highlight-priority",
                                        _ => ""
                                    };

                                    string inlineDiff = DiffText(from, to);

                                    string fromEsc = HttpUtility.JavaScriptStringEncode(from);
                                    string toEsc = HttpUtility.JavaScriptStringEncode(to);

                                    string sideBySideButton = $@"<button class='view-diff-btn' onclick=""showDiffOverlay('{fromEsc}', '{toEsc}')"">🔍 View</button>";

                                    return $@"<li class='history-item {highlight}'>{icon} <strong>{HttpUtility.HtmlEncode(field)}</strong>: 
<span class='from-val'>{inlineDiff}</span> {sideBySideButton}</li>";
                                });

                            return new
                            {
                                Day = created.Date,
                                Html = $@"
<div class='history-block'>
    <div class='change-header'>{created:HH:mm} by <strong>{HttpUtility.HtmlEncode(author)}</strong></div>
    <ul>{string.Join("", items)}</ul>
</div>"
                            };
                        })
                        .GroupBy(x => x.Day)
                        .OrderByDescending(g => g.Key);

                    var sb = new StringBuilder();


                    foreach (var group in grouped)
                    {
                        sb.AppendLine($@"<div class='history-day'>
<h5>{group.Key:yyyy-MM-dd}</h5>
{string.Join("\n", group.Select(g => g.Html))}</div>");
                    }

                    sb.AppendLine(@"
<div class='diff-overlay' id='diffOverlay'>
    <div class='diff-close' onclick=""document.getElementById('diffOverlay').style.display='none'"">✖</div>
    <div class='diff-columns'>
        <div id='diffFrom'></div>
        <div id='diffTo'></div>
    </div>
</div>

<script>
// Simple char-level diff highlighter (similar to C# DiffText)
function simpleDiffHtml(from, to) {
  let i = 0;
  let minLen = Math.min(from.length, to.length);
  let commonPrefix = '';

  while(i < minLen && from[i] === to[i]) {
    commonPrefix += from[i];
    i++;
  }

  let fromDeleted = from.slice(i);
  let toAdded = to.slice(i);

  // Escape HTML helper
  function escapeHtml(text) {
    return text.replace(/&/g, ""&amp;"")
               .replace(/</g, ""&lt;"")
               .replace(/>/g, ""&gt;"")
               .replace(/""/g, ""&quot;"")
               .replace(/'/g, ""&#039;"");
  }

  let htmlFrom = escapeHtml(commonPrefix);
  if(fromDeleted.length > 0) {
    htmlFrom += `<span class=""diff-deleted"">${escapeHtml(fromDeleted)}</span>`;
  }

  let htmlTo = escapeHtml(commonPrefix);
  if(toAdded.length > 0) {
    htmlTo += `<span class=""diff-added"">${escapeHtml(toAdded)}</span>`;
  }

  return { htmlFrom, htmlTo };
}

function showDiffOverlay(from, to) {
  const diffs = simpleDiffHtml(from, to);

  document.getElementById('diffFrom').innerHTML = diffs.htmlFrom;
  document.getElementById('diffTo').innerHTML = diffs.htmlTo;
  document.getElementById('diffOverlay').style.display = 'block';
}
</script>

");

                    historyHtml = sb.ToString();
                }

                string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css' rel='stylesheet' />
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-json.min.js'></script>
  <style>
    body {{
      font-family: 'Inter', sans-serif;
      margin: 30px;
      background: #ffffff;
      color: #1c1c1c;
      font-size: 16px;
      line-height: 1.7;
    }}
    h2 {{
      color: #0d47a1;
      font-size: 1.9em;
      margin-bottom: 20px;
    }}
    details {{
      margin-bottom: 30px;
      border: 1px solid #d6e0f5;
      border-radius: 6px;
      box-shadow: 0 2px 6px rgba(0,0,0,0.05);
    }}
    summary {{
      padding: 14px 20px;
      background-color: #e8f0fe;
      cursor: pointer;
      font-weight: 600;
      font-size: 1.2em;
      border-bottom: 1px solid #d6e0f5;
    }}
    section {{
      padding: 16px 20px;
      background-color: #f9fafe;
    }}
    .subsection h4 {{
      margin-top: 20px;
      margin-bottom: 10px;
      font-size: 1.1em;
      color: #1a237e;
    }}
    table {{
      width: 100%;
      border-collapse: separate;
      border-spacing: 0;
      border-radius: 8px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.05);
      overflow: hidden;
      background: #fff;
      margin-bottom: 20px;
    }}
    th {{
      background-color: #e3f2fd;
      color: #0d47a1;
      text-align: left;
      padding: 12px 16px;
      font-weight: bold;
      border-bottom: 2px solid #bbdefb;
    }}
    td {{
      padding: 12px 16px;
      border-bottom: 1px solid #e0e0e0;
    }}
    tr:hover td {{
      background-color: #f0f4ff;
    }}
    a {{
      color: #1565c0;
      text-decoration: none;
    }}
    a:hover {{
      text-decoration: underline;
    }}
    pre[class*='language-'] {{
      background: #f5f5f5;
      padding: 16px;
      border-radius: 6px;
      overflow-x: auto;
      font-size: 0.9em;
    }}
    .history-day {{
      margin: 24px 0;
      border-left: 4px solid #1e88e5;
      padding-left: 16px;
    }}
    .history-day h5 {{
      font-size: 1.2em;
      color: #0d47a1;
      margin-bottom: 8px;
    }}
    .history-block {{
      background: #f0f8ff;
      padding: 12px 16px;
      margin-bottom: 10px;
      border: 1px solid #bbdefb;
      border-radius: 6px;
      box-shadow: inset 0 0 3px rgba(0,0,0,0.04);
    }}
    .change-header {{
      font-weight: 600;
      color: #1565c0;
      margin-bottom: 6px;
    }}
    .history-item {{
      font-family: sans-serif;
      margin-bottom: 5px;
    }}
    .highlight-status {{ background: #fffde7; padding: 2px 6px; border-radius: 4px; }}
    .highlight-assignee {{ background: #e8f5e9; padding: 2px 6px; border-radius: 4px; }}
    .highlight-priority {{ background: #fce4ec; padding: 2px 6px; border-radius: 4px; }}
    .from-val {{ color: #d32f2f; }}
    .to-val {{ color: #2e7d32; }}
    .diff-added {{ color: #007f00; font-weight: bold; }}
    .diff-deleted {{ color: #888; text-decoration: line-through; }}
    .diff-arrow {{ color: #999; padding: 0 4px; }}
    .view-diff-btn {{
      margin-left: 10px;
      font-size: 0.9em;
      cursor: pointer;
    }}
    .diff-overlay {{
      position: fixed;
      top: 5%;
      left: 10%;
      width: 80%;
      height: 70%;
      background: white;
      border: 2px solid #ccc;
      z-index: 9999;
      overflow: auto;
      display: none;
      box-shadow: 0 0 20px rgba(0,0,0,0.3);
    }}
    .diff-overlay .diff-close {{
      float: right;
      margin: 10px;
      cursor: pointer;
      font-size: 20px;
    }}
    .diff-columns {{
      display: flex;
      justify-content: space-between;
      padding: 20px;
      font-family: monospace;
      white-space: pre-wrap;
    }}
    .diff-columns > div {{
      width: 48%;
      border: 1px solid #eee;
      padding: 10px;
      background: #f9f9f9;
    }}

.no-links {{padding: 12px;
  color: #666;
  font-style: italic;
  background: #f9f9f9;
  border: 1px solid #ddd;
  border-radius: 4px;
}}

    /* Attachment strip */
    .attachment-strip-wrapper {{
      position: relative;
      overflow: hidden;
    }}
    .attachment-strip {{
      display: flex;
      gap: 12px;
      overflow-x: auto;
      scroll-behavior: smooth;
      padding: 8px 36px;
    }}
    .attachment-strip::-webkit-scrollbar {{
      height: 8px;
    }}
    .attachment-strip::-webkit-scrollbar-thumb {{
      background: #bbb;
      border-radius: 4px;
    }}

    .attachment-nav {{
      position: absolute;
      top: 50%;
      transform: translateY(-50%);
      width: 32px;
      height: 32px;
      background: #eee;
      border-radius: 50%;
      text-align: center;
      line-height: 32px;
      font-weight: bold;
      cursor: pointer;
      box-shadow: 0 0 5px rgba(0,0,0,0.2);
      z-index: 2;
    }}
    .attachment-nav.left {{ left: 0; }}
    .attachment-nav.right {{ right: 0; }}

    .attachment-card {{
      border: 1px solid #ccc;
      background: #fff;
      border-radius: 6px;
      padding: 6px;
      text-align: center;
      font-size: 0.85em;
      box-shadow: 0 1px 2px rgba(0,0,0,0.05);
      display: flex;
      flex-direction: column;
      align-items: center;
      min-width: 130px;
      max-width: 140px;
    }}

    .attachment-filename,
    .attachment-meta,
    .download-btn {{
      width: 100%;
      box-sizing: border-box;
      margin: 4px 0;
    }}
    .attachment-meta {{
      font-size: 0.75em;
      color: #444;
      line-height: 1.3;
    }}

.no-attachments {{padding: 12px;
  color: #666;
  font-style: italic;
  background: #f9f9f9;
  border: 1px solid #ddd;
  border-radius: 4px;
}}


.attachments-wrapper {{position: relative;
  display: flex;
  align-items: center;
  margin: 10px 0;
}}

.attachments-strip {{display: flex;
  gap: 10px;
  overflow-x: auto;
  padding: 10px 0;
  scroll-behavior: smooth;
  flex-grow: 1;
}}

.scroll-btn {{background - color: #e3f2fd;
  border: none;
  cursor: pointer;
  padding: 8px 12px;
  font-size: 18px;
  border-radius: 4px;
  color: #0d47a1;
  transition: background 0.3s;
}}

.scroll-btn:hover {{background - color: #bbdefb;
}}
  </style>
</head>
<body>
  <h2>{headerLine}</h2>

<div style='margin-bottom: 20px; font-size: 0.95em; color: #444; display: flex; gap: 40px; align-items: center;'>
  <div>📅 <strong>Updated:</strong> 
                {lastUpdated}</div>
  <div>
    {statusIcon} {HttpUtility.HtmlEncode(status)}
  </div>
  <div>
    🔗 <a href='{issueUrl}' target='_blank'>Open in Browser</a>
  </div>
</div>

  <details open>
    <summary>Description</summary>
    <section>{resolvedDesc}</section>
  </details>

  <details>
    <summary>Attachments</summary>
    <section>
      <div class='attachment-strip-wrapper'>
        <div class='attachment-nav left' onclick='scrollStrip(-1)'>&lt;</div>
        <div class='attachment-strip' id='attachmentStrip'>
          {attachmentsHtml}
        </div>
        <div class='attachment-nav right' onclick='scrollStrip(1)'>&gt;</div>
      </div>
    </section>
  </details>

  <details open>
    <summary>Links</summary>
    <section>{linksHtml}</section>
  </details>

  <details>
    <summary>History</summary>
    <section>{historyHtml}</section>
  </details>

  <details>
    <summary>Response</summary>
    <section>
      <pre class='language-json'><code>{encodedJson}</code></pre>
    </section>
  </details>

  <script>
    Prism.highlightAll();

    document.querySelectorAll('a').forEach(link => {{
      link.addEventListener('click', e => {{
        e.preventDefault();
        if (link.classList.contains('download-btn') || link.classList.contains('preview-image'))
          return;
        let key = link.dataset.key || link.innerText.match(/\\b[A-Z]+-\\d+\\b/)?.[0];
        if (key && window.chrome && window.chrome.webview) {{
          window.chrome.webview.postMessage(key);
        }}
      }});
    }});

    document.querySelectorAll('.download-btn').forEach(btn => {{
      btn.addEventListener('click', e => {{
        e.preventDefault();
        const path = btn.dataset.filepath;
        if (window.chrome?.webview && path) {{
          window.chrome.webview.postMessage(JSON.stringify({{ type: 'download', path }}));
        }}
      }});
    }});

    document.querySelectorAll('.preview-image').forEach(link => {{
      link.addEventListener('click', e => {{
        e.preventDefault();
        const src = link.dataset.src;
        if (window.chrome?.webview && src) {{
          window.chrome.webview.postMessage(JSON.stringify({{ type: 'preview', path: src }}));
        }} else {{
          const overlay = document.createElement('div');
          overlay.className = 'lightbox-overlay';
          overlay.style.display = 'flex';
          overlay.innerHTML = `<img src='${{src}}' alt='Preview' />`;
          overlay.onclick = () => overlay.remove();
          document.body.appendChild(overlay);
        }}
      }});
    }});

    function scrollStrip(direction) {{
      const strip = document.getElementById('attachmentStrip');
      const scrollAmount = 160; // width + margin
      strip.scrollLeft += direction * scrollAmount;
    }}
  </script>
</body>
</html>";

                var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                await webView.EnsureCoreWebView2Async();

                webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
                {
                    var deferral = args.GetDeferral();
                    try { args.Accept(); } finally { deferral.Complete(); }
                };

                webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                if (tabDetails.ImageList == null)
                {
                    tabDetails.ImageList = new ImageList();
                    tabDetails.ImageList.ImageSize = new Size(16, 16);
                }

                System.Drawing.Image iconImage = null;
                string iconKey = issueKey;
                if (!string.IsNullOrWhiteSpace(iconUrl) && iconUrl.StartsWith("data:image"))
                {
                    try
                    {
                        string base64 = iconUrl.Substring(iconUrl.IndexOf(",") + 1);
                        byte[] bytes = Convert.FromBase64String(base64);
                        using var ms = new MemoryStream(bytes);
                        iconImage = System.Drawing.Image.FromStream(ms);
                        if (!tabDetails.ImageList.Images.ContainsKey(iconKey))
                            tabDetails.ImageList.Images.Add(iconKey, iconImage);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                var page = new TabPage(issueKey)
                {
                    ImageKey = iconKey,
                    ToolTipText = $"{summary} [{issueKey}]"
                };

                page.Controls.Add(webView);
                tabDetails.TabPages.Add(page);
                tabDetails.SelectedTab = page;

                webView.NavigateToString(html);

                tree.SelectedNode = e.Node;
                e.Node.EnsureVisible();
                tree.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not connect to fetch the information you requested.\nPlease check your connection and other settings are ok.\n{ex.Message}", "Could not connect!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string DiffText(string? from, string? to)
        {
            if (from == to) return $"<span>{WebUtility.HtmlEncode(from ?? "")}</span>";

            from ??= "";
            to ??= "";

            var diff = new StringBuilder();
            int minLen = Math.Min(from.Length, to.Length);
            int i = 0;

            // Find common prefix
            while (i < minLen && from[i] == to[i])
            {
                diff.Append(WebUtility.HtmlEncode(from[i].ToString()));
                i++;
            }

            // Deleted part (from)
            if (i < from.Length)
            {
                var deleted = WebUtility.HtmlEncode(from.Substring(i));
                diff.Append($@"<span class='diff-deleted'>{deleted}</span>");
            }

            // Added part (to)
            if (i < to.Length)
            {
                var added = WebUtility.HtmlEncode(to.Substring(i));
                diff.Append($@"<span class='diff-added'>{added}</span>");
            }

            return diff.ToString();
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(message)) return;

                // Try to detect if it's JSON or a plain string
                message = message.Trim();

                if (message.StartsWith("{"))
                {
                    using var jsonDoc = JsonDocument.Parse(message);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "download")
                    {
                        var filePath = root.GetProperty("path").GetString();

                        if (!string.IsNullOrEmpty(filePath))
                        {
                            // Strip file:/// and decode
                            if (filePath.StartsWith("file:///"))
                                filePath = Uri.UnescapeDataString(filePath.Substring(8)); // removes 'file:///' and keeps path

                            SaveFile(filePath); // your download/open logic
                        }
                    }
                }
                else
                {
                    // Handle plain string messages (e.g. issue keys like REQ-123)
                    SelectAndLoadTreeNode(message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebMessageReceived error: " + ex.Message);
            }
        }


        private void SaveFile(string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath)) return;

            try
            {
                // Create temp folder inside working directory if it doesn't exist
                string tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "temp");
                Directory.CreateDirectory(tempFolder);

                // Destination path
                string destFilePath = Path.Combine(tempFolder, Path.GetFileName(sourceFilePath));

                // Forcefully remove existing file
                if (File.Exists(destFilePath))
                {
                    File.SetAttributes(destFilePath, FileAttributes.Normal); // Remove read-only
                    File.Delete(destFilePath);
                }

                // Copy file to temp folder
                File.Copy(sourceFilePath, destFilePath, overwrite: false); // overwrite not needed now

                // Open the saved file with default app
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = destFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving or opening file: " + ex.Message);
            }
        }



        private string ReplaceJiraLinksAndSVNFeatures(string htmlDesc)
        {
            if (string.IsNullOrEmpty(htmlDesc)) return htmlDesc;

            htmlDesc = htmlDesc.Replace("ffffff", "000000");

            // Remove {color:#xxxxxx} and {color}
            // Replace opening {color:#xxxxxx} with a span tag
            htmlDesc = Regex.Replace(
                htmlDesc,
                @"\{color:(#[0-9a-fA-F]{6})\}",
                match =>
                {
                    var hex = match.Groups[1].Value.ToLower();
                    if (hex == "#ffffff") hex = "#000000"; // swap black to white for dark mode
                    return $"<span style=\"color:{hex}\">";
                },
                RegexOptions.IgnoreCase
            );

            // Replace closing {color} with </span>
            htmlDesc = Regex.Replace(htmlDesc, @"\{color\}", "</span>", RegexOptions.IgnoreCase);


            // Replace Jira <a href=".../browse/REQ-####"...>...</a> links
            htmlDesc = Regex.Replace(htmlDesc, @"<a\s+[^>]*href\s*=\s*[""'](https?://[^""']+/browse/(\w+-\d+))[""'][^>]*>.*?</a>", match =>
            {
                string url = match.Groups[1].Value;
                string key = match.Groups[2].Value;

                if (issueDict.TryGetValue(key, out var issue))
                {
                    return $"<a href=\"#\" data-key=\"{key}\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";

                }

                return $"<a href=\"#\">[{key}]</a>";
            }, RegexOptions.IgnoreCase);

            // Replace wiki-style links like [Label|https://.../browse/REQ-xxxx]
            htmlDesc = Regex.Replace(htmlDesc, @"\[(.*?)\|((https?://[^\|\]]+/browse/(\w+-\d+))(\|.*)?)\]", match =>
            {
                string label = match.Groups[1].Value;
                string fullUrlPart = match.Groups[2].Value;
                string firstUrl = fullUrlPart.Split('|')[0];

                var keyMatch = Regex.Match(firstUrl, @"browse/(\w+-\d+)");
                string key = keyMatch.Success ? keyMatch.Groups[1].Value : null;

                if (!string.IsNullOrEmpty(key) && issueDict.TryGetValue(key, out var issue))
                {
                    return $"<a href=\"#\" data-key=\"{key}\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";
                }

                return HttpUtility.HtmlEncode(label);
            });

            // Handle complex nested Jira macro links like:
            // <a ...><font>Summary <span><a ...>REQ-####</a><span>Status</span></span></font></a>
            htmlDesc = Regex.Replace(htmlDesc, @"
    <a[^>]*href\s*=\s*[""']https?://[^""']+/browse/(\w+-\d+)[""'][^>]*>      # outer <a> with href to issue
    (?:.*?<title=[""']([^""']+)[""'])?                                       # optional title attribute with summary
    .*?</a>                                                                 # closing outer </a>
", match =>
            {
                string key = match.Groups[1].Value;
                string title = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

                if (string.IsNullOrWhiteSpace(title) && issueDict.TryGetValue(key, out var issue))
                    title = issue.Summary;

                if (string.IsNullOrWhiteSpace(title))
                    title = "Issue";

                return $"<a href=\"#\">{HttpUtility.HtmlEncode(title)} [{key}]</a>";
            }, RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);


            // Replace raw URLs like https://.../browse/REQ-xxxx
            htmlDesc = Regex.Replace(htmlDesc, @"https?://[^\s""<>]+/browse/(\w+-\d+)", match =>
            {
                string key = match.Groups[1].Value;

                if (issueDict.TryGetValue(key, out var issue))
                {
                    return $"<a href=\"#\" data-key=\"{key}\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";
                }

                return $"<a href=\"#\">[{key}]</a>";
            });


            // Replace [svn://...feature|...] style SVN links// Replace encoded [svn://...feature|svn://...feature] or just [svn://...feature]
            htmlDesc = Regex.Replace(htmlDesc, @"&#91;(svn://[^\|\]]+\.feature)(?:\|svn://[^\]]+\.feature)?&#93;", match =>
            {
                string svnUrl = match.Groups[1].Value;

                try
                {
                    using var client = new SharpSvn.SvnClient();
                    using var ms = new MemoryStream();
                    client.LoadConfigurationDefault();
                    client.Authentication.DefaultCredentials = CredentialCache.DefaultNetworkCredentials;
                    client.Write(new SvnUriTarget(svnUrl), ms);
                    ms.Position = 0;

                    using var reader = new StreamReader(ms);
                    string content = reader.ReadToEnd();
                    string encoded = HttpUtility.HtmlEncode(content);

                    return $@"<pre><code class=""language-gherkin"">{encoded}</code></pre>";
                }
                catch (Exception ex)
                {
                    return $"<div style='color:red;'>⚠ Failed to load: {HttpUtility.HtmlEncode(svnUrl)}<br><strong>{ex.GetType().Name}:</strong> {HttpUtility.HtmlEncode(ex.Message)}</div>";
                }
            });

            // Replace inline Jira attachment images with embedded base64 images
            htmlDesc = Regex.Replace(htmlDesc, @"<img\s+[^>]*src\s*=\s*[""'](/rest/api/3/attachment/content/(\d+))[""'][^>]*>", match =>
            {
                string relativeUrl = match.Groups[1].Value;
                string attachmentId = match.Groups[2].Value;

                try
                {
                    var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(jiraBaseUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                    // Jira requires redirect following for attachment download
                    using var response = client.GetAsync(relativeUrl).Result;
                    response.EnsureSuccessStatusCode();
                    var imageBytes = response.Content.ReadAsByteArrayAsync().Result;

                    string base64 = Convert.ToBase64String(imageBytes);
                    string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";

                    return $"<img src=\"data:{contentType};base64,{base64}\" style=\"max-width:100%;border-radius:4px;border:1px solid #ccc;\" />";
                }
                catch (Exception ex)
                {
                    return $"<div style='color:red;'>⚠ Failed to load attachment ID {attachmentId}: {ex.Message}</div>";
                }
            });



            return htmlDesc;
        }

        private void SelectAndLoadTreeNode(string key)
        {
            var node = FindNodeByKey(tree.Nodes, key);
            if (node != null)
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
                tree.Focus();                      // ✅ Return focus to tree
                tree.SelectedNode = node;        // ✅ Restore visual highlight
            }
        }

        private TreeNode FindNodeByKey(TreeNodeCollection nodes, string key)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag?.ToString() == key)
                    return node;

                var child = FindNodeByKey(node.Nodes, key);
                if (child != null)
                    return child;
            }
            return null;
        }

        private string FormatJson(string rawJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                {
                    doc.WriteTo(writer);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return rawJson;
            }
        }

        private bool SelectNodeRecursive(TreeNode node, string issueKey)
        {
            if (node.Tag is JiraIssue issue && issue.Key.Equals(issueKey, StringComparison.OrdinalIgnoreCase))
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
                node.Expand();
                tree.Focus();
                return true;
            }

            foreach (TreeNode child in node.Nodes)
            {
                if (SelectNodeRecursive(child, issueKey))
                    return true;
            }

            return false;
        }

        private void TabDetails_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedTab = tabDetails.SelectedTab;
            if (selectedTab == null) return;

            string issueKey = selectedTab.Text;
            if (string.IsNullOrEmpty(issueKey)) return;
            SelectAndLoadTreeNode(issueKey);
            //SelectTreeNodeByKey(issueKey);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Shift | Keys.S))
            {
                ShowSearchDialog();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowSearchDialog()
        {
            var dialog = new SearchDialog(tree);
            dialog.Show(this); // non-modal
        }

        private void toolStripSplitButton1_ButtonClick(object sender, EventArgs e)
        {

        }

        private void updateHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
          "Are you sure you want to sync the hierarchy?\nThis will take some time depending on your network bandwidth.\n\nAre you sure you want to continue?",
          "Sync Hierarchy",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Warning
      );

            if (result == DialogResult.Yes)
            {
                SyncHierarchy();
            }
        }

        private async void SyncHierarchy()
        {
            await LoadAllProjectsToTreeAsync(true);
        }
    }
}
