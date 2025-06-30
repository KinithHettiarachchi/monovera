using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
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
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
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

        private readonly TreeView tree;

        public SearchDialog(TreeView tree)
        {
            InitializeComponent();

            this.tree = tree;

            cmbType.DrawMode = DrawMode.OwnerDrawFixed;
            cmbType.ItemHeight = 28;

            cmbStatus.DrawMode = DrawMode.OwnerDrawFixed;
            cmbStatus.ItemHeight = 28;

            cmbType.DrawItem += (s, e) => DrawComboItem(s, e, typeIcons);
            cmbStatus.DrawItem += (s, e) => DrawComboItem(s, e, statusIcons);

            cmbProject.Items.Clear();
            cmbProject.Items.Add("All");
            cmbProject.Items.AddRange(config.Projects.Select(p => p.Project).ToArray());

            // Subscribe event before setting SelectedIndex
            cmbProject.SelectedIndexChanged += CmbProject_SelectedIndexChanged;

            // Set selected index AFTER event subscription to trigger the handler
            cmbProject.SelectedIndex = 0;

            // Just in case event doesn’t fire on setting SelectedIndex, call handler explicitly
            CmbProject_SelectedIndexChanged(cmbProject, EventArgs.Empty);

            txtSearch.KeyDown += TxtSearch_KeyDown;

            btnSearch.Click += BtnSearch_Click;
            btnClose.Click += (s, e) => Close();

            webViewResults.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewResults.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }, TaskScheduler.FromCurrentSynchronizationContext());

            this.Shown += (s, e) => txtSearch.Focus();
        }


        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true; // Prevents the ding sound

                BtnSearch_Click(sender, e); // Call your method here
            }
        }

        private void CmbProject_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedProject = cmbProject.SelectedItem.ToString();

            var selectedProjects = selectedProject == "All"
                ? config.Projects
                : config.Projects.Where(p => p.Project == selectedProject);

            // Merge all types
            var mergedTypes = selectedProjects
                .Where(p => p.Types != null)
                .SelectMany(p => p.Types.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            cmbType.Items.Clear();
            cmbType.Items.Add("All");
            cmbType.Items.AddRange(mergedTypes.ToArray());
            cmbType.SelectedIndex = 0;

            // Merge all statuses
            var mergedStatuses = selectedProjects
                .Where(p => p.Status != null)
                .SelectMany(p => p.Status.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            cmbStatus.Items.Clear();
            cmbStatus.Items.Add("All");
            cmbStatus.Items.AddRange(mergedStatuses.ToArray());
            cmbStatus.SelectedIndex = 0;
        }


        private void DrawComboItem(object sender, DrawItemEventArgs e, Dictionary<string, string> iconMap)
        {
            if (e.Index < 0) return;
            ComboBox cmb = sender as ComboBox;
            string text = cmb.Items[e.Index].ToString();
            e.DrawBackground();

            System.Drawing.Image img = null;
            if (iconMap.TryGetValue(text, out var fileName))
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                if (File.Exists(path))
                {
                    try { img = System.Drawing.Image.FromFile(path); } catch { }
                }
            }

            if (img != null)
                e.Graphics.DrawImage(img, e.Bounds.Left + 2, e.Bounds.Top + 2, 24, 24);

            using var brush = new SolidBrush(e.ForeColor);
            float textX = e.Bounds.Left + (img != null ? 28 : 2);
            e.Graphics.DrawString(text, e.Font, brush, textX, e.Bounds.Top + 2);
            e.DrawFocusRectangle();
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
            System.Windows.Forms.Application.DoEvents(); // Let UI render the progress state

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

        public static async Task<List<JiraIssueDto>> SearchJiraIssues(string jql, IProgress<(int done, int total)> progress = null)
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
                    var url = $"/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={pageSize}&fields=summary,issuetype,status,updated";

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

                        DateTime? updated = null;
                        if (fields.TryGetProperty("updated", out var updatedProp) &&
                            DateTime.TryParse(updatedProp.GetString(), out var dt))
                        {
                            updated = dt;
                        }

                        list.Add(new JiraIssueDto
                        {
                            Key = key,
                            Summary = summary,
                            Type = type,
                            Updated = updated
                        });
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

                string typeIconKey = frmMain.GetIconForType(issue.Type);
                if (!string.IsNullOrEmpty(typeIconKey) && typeIcons.TryGetValue(typeIconKey, out var fileName))
                {
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            string base64 = Convert.ToBase64String(bytes);
                            iconPath = $"<img src='data:image/png;base64,{base64}' width='28' height='28' style='vertical-align:middle;margin-right:8px;' />";
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
        <html><body style='font-family:Segoe UI;padding:30px;font-size:18px;color:#444;'>
        <h3>Nothing was found!</h3>
        </body></html>");
                return;
            }

            string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
 <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>

<style>
  body {{
    font-family: 'IBM Plex Sans', sans-serif;
    margin: 30px;
    font-size: 18px;
    padding: 20px;
    background-color: #f8fcf8;
    color: #1c1c1c;
  }}

  details {{border: 1px solid #c8e6c9;
    border-radius: 6px;
    margin-bottom: 20px;
    box-shadow: 0 2px 5px rgba(0, 64, 0, 0.04);
    background-color: #f5fbf5;
  }}

  summary {{padding: 10px 14px;
    font-weight: bold;
    background-color: #e9f7e9;
    cursor: pointer;
    font-size: 1.1em;
    color: #2e7d32;
    border-bottom: 1px solid #d0e8d0;
  }}

  section {{padding: 10px 20px;
    background-color: #f8fcf8;
  }}

  table {{width: 100%;
    border-collapse: collapse;
    margin-top: 10px;
    background-color: #f8fcf8;
    border: 1px solid #e0f2e0;
    border-radius: 4px;
    box-shadow: 0 1px 3px rgba(0, 64, 0, 0.03);
  
            }}

  td, th {{padding: 8px 10px;
    border-bottom: 1px solid #eef5ee;
    text-align: left;
  
            }}

  th {{background-color: #e3f4e3;
    color: #1a3d1a;
    font-weight: 600;
    font-size: 0.95em;
  }}

  tr:hover td {{background-color: #f1faf1;
  }}

  a {{color: #2e7d32;
    text-decoration: none;
  }}

  a:hover {{text-decoration: underline;
    color: #1b5e20;
  }}

  img {{vertical-align: middle;
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

            try
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(), "monovera_results.html");
                File.WriteAllText(tempFilePath, html);
                webViewResults.CoreWebView2.Navigate(tempFilePath);
            }
            catch (Exception ex)
            {
                // Optionally log the error for debugging
                Debug.WriteLine("NavigateToString error: " + ex.Message);

                // Show user-friendly message
                MessageBox.Show(
                    $"Result list is too long to show.\nPlease filter your results to get more specific results.\n\n{ex.Message}",
                    "Too Many Results!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }

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
