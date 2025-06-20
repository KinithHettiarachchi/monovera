using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.IO;

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

        public frmMain()
        {
            InitializeComponent();
            InitializeIcons();
            this.Load += frmMain_Load;
        }

        private void InitializeIcons()
        {
            ImageList icons = new ImageList();
            icons.ImageSize = new Size(20, 20);
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
    }
}
