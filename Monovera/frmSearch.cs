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
    /// <summary>
    /// Search dialog for Monovera.
    /// Allows users to search Jira issues by text, project, type, and status.
    /// Displays results in a WebView2 browser and supports navigation to issues in the tree.
    /// </summary>
    public partial class frmSearch : Form
    {
        /// <summary>
        /// Default constructor for designer support.
        /// </summary>
        public frmSearch()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Reference to the tree view used for selecting and focusing issues.
        /// </summary>
        private readonly TreeView tree;

        /// <summary>
        /// Main constructor. Initializes UI, combo boxes, event handlers, and WebView2.
        /// </summary>
        /// <param name="tree">The TreeView control to use for navigation.</param>
        public frmSearch(TreeView tree)
        {
            InitializeComponent();
            this.tree = tree;

            // Set up type and status combo boxes for custom drawing and item height
            cmbType.DrawMode = DrawMode.OwnerDrawFixed;
            cmbType.ItemHeight = 28;
            cmbStatus.DrawMode = DrawMode.OwnerDrawFixed;
            cmbStatus.ItemHeight = 28;

            // Attach custom draw handlers for icons
            cmbType.DrawItem += (s, e) => DrawComboItem(s, e, typeIcons);
            cmbStatus.DrawItem += (s, e) => DrawComboItem(s, e, statusIcons);

            // Populate project combo box
            cmbProject.Items.Clear();
            cmbProject.Items.Add("All");
            cmbProject.Items.AddRange(config.Projects.Select(p => p.Project).ToArray());

            // Subscribe to project selection change before setting index
            cmbProject.SelectedIndexChanged += CmbProject_SelectedIndexChanged;
            cmbProject.SelectedIndex = 0; // Triggers handler

            // Ensure handler runs even if event doesn't fire
            CmbProject_SelectedIndexChanged(cmbProject, EventArgs.Empty);

            // Attach search box and button handlers
            txtSearch.KeyDown += TxtSearch_KeyDown;
            btnSearch.Click += BtnSearch_Click;
            btnClose.Click += (s, e) => Close();

            // Initialize WebView2 and attach message handler
            webViewResults.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewResults.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            }, TaskScheduler.FromCurrentSynchronizationContext());

            // Focus search box when dialog is shown
            this.Shown += (s, e) => txtSearch.Focus();
        }

        /// <summary>
        /// Handles the search box key down event.
        /// Triggers search when Enter is pressed.
        /// </summary>
        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true; // Prevents the ding sound
                BtnSearch_Click(sender, e); // Trigger search
            }
        }

        /// <summary>
        /// Handles project selection change.
        /// Updates type and status combo boxes to show only relevant options for the selected project.
        /// </summary>
        private void CmbProject_SelectedIndexChanged(object sender, EventArgs e)
        {
            string selectedProject = cmbProject.SelectedItem.ToString();

            // Determine which projects to merge types/statuses from
            var selectedProjects = selectedProject == "All"
                ? config.Projects
                : config.Projects.Where(p => p.Project == selectedProject);

            // Merge all types for selected projects
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

            // Merge all statuses for selected projects
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

        /// <summary>
        /// Custom draw handler for combo box items.
        /// Draws an icon next to the text if available.
        /// </summary>
        /// <param name="sender">ComboBox being drawn.</param>
        /// <param name="e">DrawItemEventArgs for drawing.</param>
        /// <param name="iconMap">Dictionary mapping item text to icon filenames.</param>
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

            // Draw icon if available
            if (img != null)
                e.Graphics.DrawImage(img, e.Bounds.Left + 2, e.Bounds.Top + 2, 24, 24);

            // Draw text
            using var brush = new SolidBrush(e.ForeColor);
            float textX = e.Bounds.Left + (img != null ? 28 : 2);
            e.Graphics.DrawString(text, e.Font, brush, textX, e.Bounds.Top + 2);
            e.DrawFocusRectangle();
        }

        /// <summary>
        /// Handles the search button click event.
        /// Builds a JQL query from UI selections, performs the search, and displays results.
        /// </summary>
        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            string query = txtSearch.Text.Trim();
            string queryKey = query.ToUpperInvariant();

            string selectedProject = cmbProject.SelectedItem?.ToString() ?? "All";
            string selectedType = cmbType.SelectedItem?.ToString() ?? "All";
            string selectedStatus = cmbStatus.SelectedItem?.ToString() ?? "All";

            // If the query matches a local issue key, select it in the tree and close dialog
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

            // Build JQL filters based on UI selections
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

            // Perform Jira search and report progress
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

            // Display results in WebView2
            ShowResults(matches, query);
        }

        /// <summary>
        /// Recursively searches a TreeNodeCollection for a node with the specified key.
        /// </summary>
        /// <param name="nodes">TreeNodeCollection to search.</param>
        /// <param name="key">Issue key to find.</param>
        /// <returns>The matching TreeNode, or null if not found.</returns>
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

        /// <summary>
        /// Performs a Jira search using the provided JQL and returns a list of matching issues.
        /// Handles paging and progress reporting.
        /// </summary>
        /// <param name="jql">Jira Query Language string.</param>
        /// <param name="progress">Optional progress reporter for UI updates.</param>
        /// <returns>List of JiraIssueDto objects matching the query.</returns>
        public static async Task<List<JiraIssueDto>> SearchJiraIssues(string jql, IProgress<(int done, int total)> progress = null)
        {
            var list = new List<JiraIssueDto>();

            try
            {
                // Set up Jira REST API client
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                using var client = new HttpClient();
                client.BaseAddress = new Uri(jiraBaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                const int pageSize = 100;
                int startAt = 0;
                int total = int.MaxValue;
                int collected = 0;

                // Fetch results in pages
                while (startAt < total)
                {
                    var url = $"/rest/api/3/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}&maxResults={pageSize}&fields=summary,issuetype,status,updated";

                    var res = await client.GetAsync(url);
                    res.EnsureSuccessStatusCode();

                    string json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    var root = doc.RootElement;
                    total = root.GetProperty("total").GetInt32();

                    // Parse issues from response
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

        /// <summary>
        /// Displays the search results in the WebView2 browser.
        /// Results are grouped by title and description matches, and rendered as HTML.
        /// </summary>
        /// <param name="issues">List of JiraIssueDto results.</param>
        /// <param name="query">Search query string.</param>
        private void ShowResults(List<JiraIssueDto> issues, string query)
        {
            var matchedTitle = new StringBuilder();
            var matchedDesc = new StringBuilder();
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            // Group results by title and description match
            foreach (var issue in issues)
            {
                string key = issue.Key;
                string summary = HttpUtility.HtmlEncode(issue.Summary ?? "");
                string iconPath = "";

                // Get icon for issue type
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

            // If nothing matched, show a message
            if (matchedTitle.Length == 0 && matchedDesc.Length == 0)
            {
                webViewResults.NavigateToString(@"
        <html><body style='font-family:Segoe UI;padding:30px;font-size:18px;color:#444;'>
        <h3>Nothing was found!</h3>
        </body></html>");
                return;
            }

            // Build HTML for results
            string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
 <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
 <link rel='stylesheet' href='{frmMain.cssHref}' />
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
                // Write HTML to temp file and navigate WebView2 to it
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

        /// <summary>
        /// Handles messages from the WebView2 browser.
        /// Selects and focuses the corresponding issue node in the tree when a result is clicked.
        /// </summary>
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
