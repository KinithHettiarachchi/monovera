using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using static Monovera.frmMain;

namespace Monovera
{
    public partial class SearchDialog : Form
    {
        public SearchDialog()
        {
            InitializeComponent();
        }

        private void SearchDialog_Load(object sender, EventArgs e)
        {

        }

        private readonly Dictionary<string, JiraIssue> issueDict;
        private readonly TreeView tree;
        private readonly string jiraEmail;
        private readonly string jiraToken;
        private readonly string jiraBaseUrl;
        private List<string> projectList = new();
        private Dictionary<string, string> iconDefinitions;

        public SearchDialog(
                Dictionary<string, JiraIssue> issueDict,
                TreeView tree,
                string email,
                string token,
                string baseUrl,
                List<string> projectList,
                Dictionary<string, string> iconDefinitions)
        {
            InitializeComponent();

            this.issueDict = issueDict;
            this.tree = tree;
            this.jiraEmail = email;
            this.jiraToken = token;
            this.jiraBaseUrl = baseUrl;
            this.projectList = projectList;
            this.iconDefinitions = iconDefinitions;

            cmbProject.Items.Add("All");
            cmbProject.Items.AddRange(projectList.ToArray());
            cmbProject.SelectedIndex = 0;

            cmbType.Items.AddRange(new[] { "All", "Data Entity", "Definition", "Element", "Folder", "GUI Component", "Menu", "Parameter", "Project", "Report Component", "Rule", "Technical Req", "Testable Functionality", "Use Case", "User req", "User Story" });
            cmbType.SelectedIndex = 0;

            cmbStatus.Items.AddRange(new[] { "All", "Draft", "To Approve", "Published", "Rejected" });
            cmbStatus.SelectedIndex = 0;

            btnSearch.Click += BtnSearch_Click;
            btnClose.Click += (s, e) => Close();

            webViewResults.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewResults.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }



        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            string query = txtSearch.Text.Trim();
            string queryKey = query.ToUpperInvariant();

            string selectedProject = cmbProject.SelectedItem?.ToString() ?? "All";
            string selectedType = cmbType.SelectedItem?.ToString() ?? "All";
            string selectedStatus = cmbStatus.SelectedItem?.ToString() ?? "All";

            // Local key match
            if (!string.IsNullOrWhiteSpace(queryKey) && issueDict.ContainsKey(queryKey))
            {
                tree.Invoke(() =>
                {
                    var node = FindNodeByKey(tree.Nodes, queryKey);
                    if (node != null)
                    {
                        tree.SelectedNode = node;
                        node.EnsureVisible();
                    }
                });
                this.Close();
                return;
            }

            // --- Progress UI setup ---
            pbProgress.Visible = true;
            lblProgress.Visible = true;
            pbProgress.Style = ProgressBarStyle.Marquee;
            lblProgress.Text = "Searching...";
            Application.DoEvents(); // Let UI render the progress state

            // Build JQL filters
            List<string> jqlFilters = new();
            if (selectedProject == "All")
                jqlFilters.Add($"({string.Join(" OR ", projectList.Select(p => $"project = \"{p}\""))})");
            else
                jqlFilters.Add($"project = \"{selectedProject}\"");

            if (!string.IsNullOrWhiteSpace(query))
                jqlFilters.Add($"text ~ \"{query}\"");

            if (selectedType != "All")
                jqlFilters.Add($"issuetype = \"{selectedType}\"");

            if (selectedStatus != "All")
                jqlFilters.Add($"status = \"{selectedStatus}\"");

            string jql = $"{string.Join(" AND ", jqlFilters)} ORDER BY key ASC";

            var matches = await SearchJiraIssues(jql, new Progress<(int, int)>(p =>
            {
                lblProgress.Invoke(() => lblProgress.Text = $"Loading {p.Item1} / {p.Item2}");
                pbProgress.Invoke(() =>
                {
                    pbProgress.Style = ProgressBarStyle.Blocks;
                    pbProgress.Maximum = p.Item2;
                    pbProgress.Value = Math.Min(p.Item1, p.Item2);
                });
            }));

            pbProgress.Visible = false;
            lblProgress.Visible = false;

            ShowResults(matches, query);
        }


        private TreeNode FindNodeByKey(TreeNodeCollection nodes, string key)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag?.ToString() == key) return node;
                var child = FindNodeByKey(node.Nodes, key);
                if (child != null) return child;
            }
            return null;
        }

        private async Task<List<JiraIssueDto>> SearchJiraIssues(string jql, IProgress<(int done, int total)> progress = null)
        {
            var list = new List<JiraIssueDto>();

            try
            {
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                using var client = new HttpClient();
                client.BaseAddress = new Uri(jiraBaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                const int pageSize = 100;
                int startAt = 0;
                int total = int.MaxValue;
                int collected = 0;

                while (startAt < total)
                {
                    var url = $"/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={pageSize}&fields=summary,issuetype,status";

                    var res = await client.GetAsync(url);
                    res.EnsureSuccessStatusCode();

                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    var root = doc.RootElement;
                    total = root.GetProperty("total").GetInt32();

                    foreach (var issue in root.GetProperty("issues").EnumerateArray())
                    {
                        var key = issue.GetProperty("key").GetString();
                        var fields = issue.GetProperty("fields");
                        var summary = fields.GetProperty("summary").GetString();
                        var type = fields.GetProperty("issuetype").GetProperty("name").GetString();
                        list.Add(new JiraIssueDto { Key = key, Summary = summary, Type = type });
                    }

                    collected += root.GetProperty("issues").GetArrayLength();
                    progress?.Report((collected, total));

                    startAt += pageSize;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Jira search failed: " + ex.Message);
            }

            return list;
        }


        private void ShowResults(List<JiraIssueDto> issues, string query)
        {
            var matchedTitle = new StringBuilder();
            var matchedDesc = new StringBuilder();
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            foreach (var issue in issues)
            {
                string key = issue.Key;
                string summary = HttpUtility.HtmlEncode(issue.Summary ?? "");
                string iconPath = "";

                string typeIconKey = GetIconForType(issue.Type);
                if (!string.IsNullOrEmpty(typeIconKey) && iconDefinitions.TryGetValue(typeIconKey, out var fileName))
                {
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            string base64 = Convert.ToBase64String(bytes);
                            iconPath = $"<img src='data:image/png;base64,{base64}' width='16' height='16' style='vertical-align:middle;margin-right:8px;' />";
                        }
                        catch
                        {
                            // fallback silently
                        }
                    }
                }

                string htmlLink = $"<tr><td><a href=\"#\" data-key=\"{key}\">{iconPath}{summary} [{key}]</a></td></tr>";

                if (!hasQuery)
                    matchedTitle.AppendLine(htmlLink);
                else if (summary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    matchedTitle.AppendLine(htmlLink);
                else
                    matchedDesc.AppendLine(htmlLink);
            }

            // If nothing matched
            if (matchedTitle.Length == 0 && matchedDesc.Length == 0)
            {
                webViewResults.NavigateToString(@"
        <html><body style='font-family:Segoe UI;padding:30px;font-size:16px;color:#444;'>
        <h3>Nothing was found!</h3>
        </body></html>");
                return;
            }

            string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <style>
    body {{
      font-family: 'Segoe UI'; font-size: 14px; padding: 20px;
      background-color: #fefefe;
      color: #1c1c1c;
    }}
    details {{
      border: 1px solid #ccc;
      border-radius: 6px;
      margin-bottom: 20px;
      box-shadow: 0 2px 5px rgba(0,0,0,0.04);
    }}
    summary {{
      padding: 10px 14px;
      font-weight: bold;
      background-color: #e8f0fe;
      cursor: pointer;
      font-size: 1.1em;
    }}
    section {{
      padding: 10px 20px;
      background-color: #ffffff;
    }}
    table {{
      width: 100%;
      border-collapse: collapse;
    }}
    td {{
      padding: 8px;
      border-bottom: 1px solid #eee;
    }}
    a {{
      color: #1565c0;
      text-decoration: none;
    }}
    a:hover {{
      text-decoration: underline;
    }}
    img {{
      vertical-align: middle;
      margin-right: 8px;
    }}
  </style>
</head>
<body>
  <details open>
    <summary>{(hasQuery ? "Matched by Title" : "Results")}</summary>
    <section><table>{matchedTitle}</table></section>
  </details>
  {(hasQuery ? $@"
  <details>
    <summary>Matched by Description</summary>
    <section><table>{matchedDesc}</table></section>
  </details>" : "")}

  <script>
    document.querySelectorAll('a').forEach(link => {{
      link.addEventListener('click', e => {{
        e.preventDefault();
        const key = link.dataset.key;
        if (key && window.chrome?.webview)
          window.chrome.webview.postMessage(key);
      }});
    }});
  </script>
</body>
</html>";

            webViewResults.NavigateToString(html);
        }


        private string GetIconHtml(string issueType)
        {
            string key = GetIconForType(issueType);
            if (!string.IsNullOrEmpty(key) && iconDefinitions.TryGetValue(key, out var fileName))
            {
                string path = $"file:///{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName).Replace("\\", "/")}";
                return $"<img src='{path}' width='16' height='16' style='vertical-align:middle;margin-right:8px;' />";
            }
            return "";
        }

        private string GetIconForType(string issueType)
        {
            string lower = issueType.ToLower();

            if (lower.StartsWith("user")) return "user"; // handles "User req" and "User Story"
            if (lower.StartsWith("gui")) return "gui"; // GUI Component
            if (lower.StartsWith("rule")) return "rule";
            if (lower.StartsWith("definition")) return "definition";
            if (lower.StartsWith("data entity")) return "entity";
            if (lower.StartsWith("element")) return "element";
            if (lower.StartsWith("folder")) return "folder";
            if (lower.StartsWith("technical")) return "technical";
            if (lower.StartsWith("project")) return "project";
            if (lower.StartsWith("menu")) return "menu";
            if (lower.StartsWith("test")) return "test";
            if (lower.StartsWith("parameter")) return "parameter";
            if (lower.StartsWith("report")) return "report";
            if (lower.StartsWith("use case")) return "usecase";

            return "";
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string key = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(key)) return;

            var node = FindNodeByKey(tree.Nodes, key);
            if (node != null)
            {
                tree.Invoke(() =>
                {
                    tree.SelectedNode = node;
                    node.EnsureVisible();
                });
            }
        }
    }
}
