using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using SharpSvn;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace Monovera
{
    public partial class frmMain : Form
    {
        private Dictionary<string, List<JiraIssue>> childrenByParent = new();
        private Dictionary<string, JiraIssue> issueDict = new();
        private string root_key = "";
        private List<string> projectList = new();
        private string jiraBaseUrl = "";
        private string jiraEmail = "";
        private string jiraToken = "";

        private TabControl tabDetails;

        public frmMain()
        {
            InitializeComponent();
            InitializeIcons();
   
            //Tabs for right side
            tabDetails = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "tabDetails"
            };

            tabDetails.SelectedIndexChanged += TabDetails_SelectedIndexChanged;
            panelTabs.Controls.Add(tabDetails);
        }

        private void InitializeIcons()
        {
            ImageList icons = new ImageList();
            icons.ImageSize = new Size(18, 18);
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images");

            var iconDefinitions = new Dictionary<string, string>
            {
                { "user", "type_userreq.png" },
                { "gui", "type_gui.png" },
                { "rule", "type_rule.png" },
                { "technical", "type_technical.png" },
                { "entity", "type_entity.png" },
                { "project", "type_project.png" },
                { "folder", "type_folder.png" },
                { "definition", "type_definition.png" },
                { "element", "type_element.png" },
                { "menu", "type_menu.png" },
                { "draft", "status_draft.png" },
                { "published", "status_published.png" },
                { "rejected", "status_rejected.png" },
                { "tood", "status_todo.png" }
            };

            foreach (var kvp in iconDefinitions)
            {
                string key = kvp.Key;
                string path = Path.Combine(basePath, kvp.Value);
                if (File.Exists(path))
                {
                    icons.Images.Add(key, Image.FromFile(path));
                }
            }

            tree.ImageList = icons;
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

        private async void frmMain_Load(object sender, EventArgs e)
        {
            var config = ReadConfiguration();

            if (!config.TryGetValue("JIRA_PROJECTS", out string projectCsv) ||
                !config.TryGetValue("JIRA_PROJECT_ROOTS", out root_key) ||
                !config.TryGetValue("JIRA_HOME", out jiraBaseUrl) ||
                !config.TryGetValue("JIRA_EMAIL", out jiraEmail) ||
                !config.TryGetValue("JIRA_TOKEN", out jiraToken))
            {
                MessageBox.Show("Missing required configuration values.");
                return;
            }

            projectList = projectCsv.Split(',').Select(p => p.Trim()).Where(p => p != "").ToList();

            await LoadAllProjectsToTreeAsync();
        }

        private Dictionary<string, string> ReadConfiguration()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.properties");
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadAllLines(configPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                    config[parts[0].Trim()] = parts[1].Trim();
            }

            return config;
        }

        private async Task LoadAllProjectsToTreeAsync()
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

                if (File.Exists(cacheFile))
                {
                    string cachedJson = await File.ReadAllTextAsync(cacheFile);
                    issues = ParseIssuesFromJson(cachedJson);
                }
                else
                {
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
            string iconKey = GetIconForType(issue.Type);
            return new TreeNode($"{issue.Summary} [{issue.Key}]")
            {
                Tag = issue.Key,
                ImageKey = iconKey,
                SelectedImageKey = iconKey
            };
        }

        private string GetIconForType(string issueType)
        {
            string lower = issueType.ToLower();

            if (lower.StartsWith("user")) return "user";
            if (lower.StartsWith("gui")) return "gui";
            if (lower.StartsWith("rule")) return "rule";
            if (lower.StartsWith("definition")) return "definition";
            if (lower.StartsWith("data entity")) return "entity";
            if (lower.StartsWith("element")) return "element";
            if (lower.StartsWith("folder")) return "folder";
            if (lower.StartsWith("technical")) return "technical";
            if (lower.StartsWith("project")) return "project";
            if (lower.StartsWith("menu")) return "menu";

            return "";
        }

        private async void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node?.Tag is not string issueKey || string.IsNullOrWhiteSpace(issueKey))
                return;

            // Prevent duplicate tabs
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
                var fields = doc.RootElement.GetProperty("fields");
                var renderedFields = doc.RootElement.GetProperty("renderedFields");

                string summary = fields.GetProperty("summary").GetString();
                string htmlDesc = renderedFields.GetProperty("description").GetString() ?? "";

                var innerTabs = new TabControl { Dock = DockStyle.Fill };

                // Description tab with WebView2
                var descriptionTab = new TabPage("Description");
                var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };

                descriptionTab.Controls.Add(webView);

                await webView.EnsureCoreWebView2Async();

                // Suppress JS dialogs
                webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
                {
                    var deferral = args.GetDeferral();
                    try
                    {
                        args.Accept();
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                };

                string header = $"<h2>{WebUtility.HtmlEncode(summary)} [{issueKey}]</h2>";
                string resolvedDesc = ReplaceJiraLinksAndSVNFeatures(htmlDesc);

                string lightModeHtml = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href=""https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css"" rel=""stylesheet"" />
  <script src=""https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js""></script>
  <script src=""https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js""></script>
  <style>
    @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&display=swap');

    body {{
      background-color: #ffffff;
      color: #1c1c1c;
      font-family: 'Inter', 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
      margin: 30px;
      line-height: 1.75;
      font-size: 16px;
    }}

    h1, h2, h3 {{
      color: #1a237e;
      font-weight: 700;
      margin-bottom: 16px;
    }}

    h2 {{
      font-size: 1.8em;
      border-bottom: 2px solid #c5cae9;
      padding-bottom: 6px;
      margin-bottom: 24px;
    }}

    a {{
      color: #1565c0;
      text-decoration: none;
      font-weight: 500;
      transition: color 0.2s ease-in-out;
    }}

    a:hover {{
      text-decoration: underline;
      color: #0d47a1;
    }}

    p, li {{
      font-size: 1em;
      color: #333333;
    }}

    ul, ol {{
      margin-left: 25px;
      margin-bottom: 15px;
      padding-left: 1.5em;
      list-style: none;
    }}

    ul li::before {{
      content: '●';
      color: #42a5f5;
      font-weight: bold;
      display: inline-block;
      width: 1em;
      margin-left: -1.5em;
    }}

    ol {{
      counter-reset: section;
    }}

    ol li {{
      counter-increment: section;
      position: relative;
    }}

    ol li::before {{
      content: counter(section) '.';
      color: #7e57c2;
      font-weight: bold;
      display: inline-block;
      width: 1.5em;
      margin-left: -1.5em;
    }}

    table {{
      border-collapse: separate;
      border-spacing: 0;
      width: 100%;
      margin: 30px 0;
      background: linear-gradient(to bottom right, #ffffff, #f6f7fb);
      color: #222;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
      border-radius: 12px;
      overflow: hidden;
      font-size: 0.95em;
    }}

    th, td {{
      padding: 16px 20px;
      text-align: left;
      border-bottom: 1px solid #e0e0e0;
    }}

    th {{
      background: linear-gradient(to right, #e8eaf6, #c5cae9);
      color: #1c1c1c;
      font-weight: 700;
      text-shadow: 0 1px 0 #fff;
      border-bottom: 2px solid #b0b9e6;
    }}

    td {{
      background-color: #ffffff;
      transition: background-color 0.3s ease, transform 0.1s ease;
    }}

    tr:nth-child(even) td {{
      background-color: #f9faff;
    }}

    tr:hover td {{
      background-color: #edf1ff;
      transform: scale(1.005);
      box-shadow: inset 0 0 5px rgba(0, 0, 0, 0.05);
    }}

    blockquote {{
      border-left: 4px solid #64b5f6;
      padding-left: 16px;
      color: #555;
      background-color: #f6f9fc;
      font-style: italic;
      margin: 20px 0;
    }}

    hr {{
      border: none;
      height: 1px;
      background-color: #dcdcdc;
      margin: 30px 0;
    }}

    code {{
      background-color: #f3f3f3;
      color: #000;
      font-family: monospace;
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 90%;
    }}

    pre {{
      background-color: #f5f5f5;
      padding: 12px;
      border-radius: 6px;
      overflow-x: auto;
      font-size: 90%;
      line-height: 1.6;
    }}

    .panel {{
      background-color: #f4f6ff;
      border: 1px solid #3f51b5; /* rich blue border */
      border-radius: 6px;
      margin: 20px 0;
      padding: 0;
      box-shadow: 0 2px 6px rgba(0, 0, 0, 0.08);
    }}

    .panelContent {{
      background-color: #fdfdff;
      color: #1c1c1c;
      padding: 16px 20px;
      border-radius: 6px;
      line-height: 1.7;
    }}
  </style>
</head>
<body>
  {header}
  {resolvedDesc}
</body>
</html>
";

                webView.NavigateToString(lightModeHtml);

                // Setup link click interception
                webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                webView.CoreWebView2.DOMContentLoaded -= CoreWebView2_DOMContentLoaded;
                webView.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;

                void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
                {
                    string message = args.TryGetWebMessageAsString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        SelectAndLoadTreeNode(message); // This should select and highlight the node
                    }
                }

                void CoreWebView2_DOMContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs args)
                {
                    string js = @"
        document.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', e => {
                e.preventDefault();
                let text = a.innerText;
                let match = text.match(/\b[A-Z]+-\d+\b/);
                if (match) {
                    window.chrome.webview.postMessage(match[0]);
                }
            });
        });
    ";
                    _ = webView.ExecuteScriptAsync(js);
                }


                // Links tab
                var linksTab = new TabPage("Links");
                var linksPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false
                };

                AddLinkSection("Parent", fields, linksPanel, "inwardIssue", "Parent/Child");
                AddLinkSection("Children", fields, linksPanel, "outwardIssue", "Parent/Child");
                AddLinkSection("Related", fields, linksPanel, null, "Relates");

                linksTab.Controls.Add(linksPanel);
                innerTabs.TabPages.Add(linksTab);

                // JSON tab
                var jsonTab = new TabPage("Response");
                var jsonBox = new TextBox
                {
                    Multiline = true,
                    Dock = DockStyle.Fill,
                    ScrollBars = ScrollBars.Both,
                    ReadOnly = true,
                    Font = new Font("Consolas", 12),
                    Text = FormatJson(json)
                };
                jsonTab.Controls.Add(jsonBox);
                innerTabs.TabPages.Add(jsonTab);

                // Add Description tab last so it shows first (optional)
                innerTabs.TabPages.Insert(0, descriptionTab);

                // Add outer tab to main tab control
                var outerTab = new TabPage(issueKey);
                outerTab.Controls.Add(innerTabs);
                tabDetails.TabPages.Add(outerTab);

                // Select Description and outer tabs
                innerTabs.SelectedTab = descriptionTab;
                tabDetails.SelectedTab = outerTab;

                // Keep tree node focused and visible
                tree.SelectedNode = e.Node;
                tree.SelectedNode?.EnsureVisible();
                tree.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load issue details: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }




        private void AddLinkSection(string title, JsonElement fields, Control container, string issueProp, string linkType)
        {
            var label = new Label { Text = title, Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true };
            container.Controls.Add(label);

            if (fields.TryGetProperty("issuelinks", out var links))
            {
                foreach (var link in links.EnumerateArray())
                {
                    if (link.GetProperty("type").GetProperty("name").GetString() == linkType)
                    {
                        JsonElement issueElem;
                        if (issueProp == null)
                        {
                            issueElem = link.TryGetProperty("inwardIssue", out var inw) ? inw :
                                         link.TryGetProperty("outwardIssue", out var outw) ? outw : default;
                        }
                        else if (link.TryGetProperty(issueProp, out var targetIssue))
                        {
                            issueElem = targetIssue;
                        }
                        else continue;

                        string key = issueElem.GetProperty("key").GetString();
                        string summary = issueElem.GetProperty("fields").GetProperty("summary").GetString();

                        var linkLabel = new LinkLabel
                        {
                            Text = "\u2022 " + summary + " [" + key + "]",
                            Tag = key,
                            AutoSize = true,
                            LinkBehavior = LinkBehavior.HoverUnderline
                        };

                        linkLabel.ForeColor = Color.Black;
                        linkLabel.Font = new Font(linkLabel.Font.FontFamily, 12, linkLabel.Font.Style);

                        linkLabel.Click += (s, e) => SelectAndLoadTreeNode(key);
                        container.Controls.Add(linkLabel);
                    }
                }
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
                    return $"<a href=\"#\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";
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
                    return $"<a href=\"#\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";
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
                    return $"<a href=\"#\">{HttpUtility.HtmlEncode(issue.Summary)} [{key}]</a>";
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
    }
}
