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
using Application = System.Windows.Forms.Application;

namespace Monovera
{
    /// <summary>
    /// Search dialog for Monovera.
    /// Allows users to search Jira issues by text, project, type, and status.
    /// Displays results in a WebView2 browser and supports navigation to issues in the tree.
    /// </summary>
    public partial class frmSearch : Form
    {
        private ListBox lstAutoComplete;

        /// <summary>
        /// Main constructor. Initializes UI, combo boxes, event handlers, and WebView2.
        /// </summary>
        /// <param name="tree">The TreeView control to use for navigation.</param>
        public frmSearch()
        {
            InitializeComponent();

            lstAutoComplete = new ListBox
            {
                Visible = false,
                Height = 160,
                Width = this.ClientSize.Width - txtSearch.Left - 16, // initial max width
                Font = txtSearch.Font
            };
            this.Controls.Add(lstAutoComplete);
            lstAutoComplete.BringToFront();
            lstAutoComplete.Left = txtSearch.Left;
            lstAutoComplete.Top = txtSearch.Bottom + 2;

            // Handle selection
            lstAutoComplete.Click += (s, e) => AcceptAutoComplete();
            lstAutoComplete.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    AcceptAutoComplete();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    lstAutoComplete.Visible = false;
                    txtSearch.Focus();
                }
            };

            txtSearch.TextChanged += TxtSearch_TextChanged;

            // Only use Hide, never Close
            btnClose.Click += (s, e) => this.Hide();

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

            txtSearch.LostFocus += (s, e) =>
            {
                // Hide only if focus is not moving to the listbox
                if (!lstAutoComplete.Focused)
                    lstAutoComplete.Visible = false;
            };
            lstAutoComplete.LostFocus += (s, e) => lstAutoComplete.Visible = false;

            chkJQL.CheckedChanged += ChkJQL_CheckedChanged;

            // Initialize WebView2 and attach message handler
            webViewResults.EnsureCoreWebView2Async().ContinueWith(_ =>
            {
                webViewResults.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                // Show welcome message on first open
                webViewResults.Invoke(() =>
                {
                    webViewResults.NavigateToString($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
  <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body style=""margin:0; display:flex; align-items:center; justify-content:center; height:100vh; background-color:white;"">
  <h2 style=""color:grey; font-size:2.5em; font-family:Segoe UI, sans-serif; text-align:center;"">
    🙂 Ready… set… search!
  </h2>
</body>
</html>");
                });
            }, TaskScheduler.FromCurrentSynchronizationContext());

            // Focus search box when dialog is shown
            this.VisibleChanged += (s, e) =>
            {
                if (this.Visible)
                    txtSearch.Focus();
            };

        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtSearch.Focus();
        }

        private void UpdateProgressFromService(int done, int total, double percent)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateProgressFromService(done, total, percent)));
                return;
            }

            pbProgress.Visible = true;
            pbProgress.Maximum = 100;
            pbProgress.Value = Math.Clamp((int)Math.Round(percent), 0, 100);

            lblProgress.Visible = true;
            lblProgress.Text = done == 0
                ? $"Preparing to load... ({total:N0} issue{(total == 1 ? "" : "s")})"
                : $"Loading {done:N0}/{total:N0} ({percent:0.0}%)";
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            if (chkJQL.Checked || string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                lstAutoComplete.Visible = false;
                return;
            }

            string input = txtSearch.Text.Trim().ToLowerInvariant();
            var matches = frmMain.FlatJiraIssueDictionary.Values
                .Where(i => (!string.IsNullOrEmpty(i.Summary) && i.Summary.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                         || (!string.IsNullOrEmpty(i.Key) && i.Key.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(i => i.Summary)
                .Take(20)
                .Select(i => $"{i.Summary} [{i.Key}]")
                .ToList();

            if (matches.Count > 0)
            {
                lstAutoComplete.BeginUpdate();
                lstAutoComplete.Items.Clear();
                foreach (var m in matches)
                    lstAutoComplete.Items.Add(m);
                lstAutoComplete.EndUpdate();

                // Set width up to the right edge of the form (minus a margin)
                int margin = 16;
                int maxWidth = this.ClientSize.Width - txtSearch.Left - margin;
                lstAutoComplete.Width = Math.Max(txtSearch.Width, maxWidth);

                lstAutoComplete.Left = txtSearch.Left;
                lstAutoComplete.Top = txtSearch.Bottom + 2;
                lstAutoComplete.Visible = true;
            }
            else
            {
                lstAutoComplete.Visible = false;
            }
        }

        private void AcceptAutoComplete()
        {
            if (lstAutoComplete.SelectedItem == null) return;
            string selected = lstAutoComplete.SelectedItem.ToString();
            // Extract key from "Summary [KEY]"
            int lb = selected.LastIndexOf('[');
            int rb = selected.LastIndexOf(']');
            if (lb >= 0 && rb > lb)
            {
                string key = selected.Substring(lb + 1, rb - lb - 1);
                SelectNodeByKey(key);
                this.Hide();
            }
            lstAutoComplete.Visible = false;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (lstAutoComplete.Visible)
            {
                if (e.KeyCode == Keys.Down)
                {
                    if (lstAutoComplete.SelectedIndex < lstAutoComplete.Items.Count - 1)
                        lstAutoComplete.SelectedIndex++;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    if (lstAutoComplete.SelectedIndex > 0)
                        lstAutoComplete.SelectedIndex--;
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (lstAutoComplete.SelectedIndex >= 0)
                    {
                        AcceptAutoComplete();
                    }
                    else
                    {
                        // No selection, trigger search
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        BtnSearch_Click(sender, e);
                        lstAutoComplete.Visible = false;
                    }
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    lstAutoComplete.Visible = false;
                    e.Handled = true;
                }
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                BtnSearch_Click(sender, e);
            }
        }


        private void ChkJQL_CheckedChanged(object sender, EventArgs e)
        {
            bool jqlMode = chkJQL.Checked;

            // Hide or show labels and dropdowns
            lblType.Visible = !jqlMode;
            lblStatus.Visible = !jqlMode;
            cmbType.Visible = !jqlMode;
            cmbStatus.Visible = !jqlMode;
            cmbProject.Visible = !jqlMode;
            lblProject.Visible = !jqlMode;
            txtSearch.Clear();

            // Resize txtSearch to the end of the status dropdown
            if (jqlMode)
            {
                txtSearch.Width = cmbStatus.Right - txtSearch.Left;
                txtSearch.PlaceholderText = "Enter JQL...";
            }
            else
            {
                txtSearch.Width = lblProject.Left - txtSearch.Left -5;
                txtSearch.PlaceholderText = "Enter issue key or search text...";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Only hide if user is closing the search dialog directly
            if (e.CloseReason == CloseReason.UserClosing && Application.OpenForms.OfType<frmMain>().Any(f => f.Visible))
            {
                e.Cancel = true;
                this.Hide();
            }
            // Otherwise (app is exiting), allow close
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

        private void SelectNodeByKey(string key)
        {
            // Find the main form instance
            var mainForm = Application.OpenForms.OfType<frmMain>().FirstOrDefault();
            if (mainForm != null)
            {
                mainForm.Invoke(() => mainForm.SelectAndLoadTreeNode(key));
            }
        }

        /// <summary>
        /// Handles the search button click event.
        /// Builds a JQL query from UI selections, performs the search, and displays results.
        /// </summary>
        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            string query = txtSearch.Text.Trim();

            if (chkJQL.Checked)
            {
                // Use the entered JQL as-is
                string jql = query;
                await DoJiraSearch(jql, query);
                return;
            }

            string queryKey = query.ToUpperInvariant();

            // If the query matches a local issue key, select it in the tree and close dialog
            if (!string.IsNullOrWhiteSpace(queryKey) && issueDict.ContainsKey(queryKey))
            {
                SelectNodeByKey(queryKey);
                this.Hide();
                return;
            }

            string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
            File.WriteAllText(htmlFilePath, frmMain.HTML_LOADINGPAGE);
            webViewResults.CoreWebView2.Navigate(htmlFilePath);

            // --- Progress UI setup ---
            pbProgress.Visible = true;
            lblProgress.Visible = true;
            pbProgress.Style = ProgressBarStyle.Marquee;
            lblProgress.Text = "Searching...";
            System.Windows.Forms.Application.DoEvents(); // Let UI render the progress state

            // Build JQL filters based on UI selections as before
            List<string> jqlFilters = new();
            string selectedProject = cmbProject.SelectedItem?.ToString() ?? "All";
            string selectedType = cmbType.SelectedItem?.ToString() ?? "All";
            string selectedStatus = cmbStatus.SelectedItem?.ToString() ?? "All";

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

            string normalJql = $"{string.Join(" AND ", jqlFilters)} ORDER BY key ASC";
            await DoJiraSearch(normalJql, query);
        }

        private async Task DoJiraSearch(string jql, string query)
        {
            string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
            File.WriteAllText(htmlFilePath, frmMain.HTML_LOADINGPAGE);
            webViewResults.CoreWebView2.Navigate(htmlFilePath);

            pbProgress.Visible = true;
            lblProgress.Visible = true;
            pbProgress.Style = ProgressBarStyle.Marquee;
            lblProgress.Text = "Searching...";
            System.Windows.Forms.Application.DoEvents();

            try
            {
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

                ShowResults(matches, query, chkJQL.Checked);
            }
            catch (Exception ex)
            {
                pbProgress.Visible = false;
                lblProgress.Visible = false;

                webViewResults.NavigateToString($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
  <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body style=""margin:0; display:flex; align-items:center; justify-content:center; height:100vh; background-color:white;"">
  <div style=""text-align:center;"">
    <h2 style=""color:#b71c1c; font-size:2.2em; font-family:Segoe UI, sans-serif;"">
      😢 Whoops! A little glitch in the matrix. Try again?
    </h2>
    <p style=""color:#444; font-size:1.2em; max-width:600px; margin:0 auto 1em auto;"">
      The Jira search could not be completed.<br>
      Please check your connection/query and try again.
    </p>
    <details style=""margin-top:1em;"">
      <summary style=""cursor:pointer; color:#b71c1c;"">Show error details</summary>
      <pre style=""color:#b71c1c; background:#fbe9e7; padding:1em; border-radius:6px; font-size:1em;"">{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>
    </details>
  </div>
</body>
</html>");
            }
        }

        public static async Task<List<JiraIssueDictionary>> SearchJiraIssues(string jql, IProgress<(int done, int total)> progress = null)
        {
            if (string.IsNullOrWhiteSpace(jql))
                throw new ArgumentException("JQL must not be empty for the new /search/jql endpoint.", nameof(jql));

            static string Truncate(string s, int max) =>
                string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max) + " …");

            var list = new List<JiraIssueDictionary>();

            try
            {
                var baseUrl = jiraBaseUrl?.TrimEnd('/') ?? throw new InvalidOperationException("jiraBaseUrl is not configured.");
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));

                using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = TimeSpan.FromSeconds(100);

                const int pageSize = 100; // default is usually 50; 100 is acceptable
                string nextPageToken = null;
                int collected = 0;

                do
                {
                    var fieldsCsv = "summary,issuetype,status,updated";
                    var qs = new List<string>
            {
                "jql=" + Uri.EscapeDataString(jql),
                "maxResults=" + pageSize,
                "fields=" + Uri.EscapeDataString(fieldsCsv)
            };
                    if (!string.IsNullOrWhiteSpace(nextPageToken))
                        qs.Add("nextPageToken=" + Uri.EscapeDataString(nextPageToken));

                    var url = "/rest/api/3/search/jql?" + string.Join("&", qs);
                    var res = await client.GetAsync(url);
                    if (!res.IsSuccessStatusCode)
                    {
                        var err = await res.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"Jira search failed: {(int)res.StatusCode} {res.ReasonPhrase}. {Truncate(err, 2000)}");
                    }

                    var json = await res.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Two possible shapes: top-level { issues, nextPageToken } or { results: [ { issues, nextPageToken } ] }
                    JsonElement page;
                    if (root.TryGetProperty("results", out var resultsArr) && resultsArr.ValueKind == JsonValueKind.Array && resultsArr.GetArrayLength() > 0)
                        page = resultsArr[0];
                    else
                        page = root;

                    if (!page.TryGetProperty("issues", out var issuesEl) || issuesEl.ValueKind != JsonValueKind.Array)
                        break;

                    int count = 0;
                    foreach (var issue in issuesEl.EnumerateArray())
                    {
                        count++;

                        var key = issue.TryGetProperty("key", out var kProp) ? kProp.GetString() : null;
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        var fields = issue.TryGetProperty("fields", out var fProp) ? fProp : default;

                        string summary = null;
                        string type = null;
                        DateTime? updated = null;

                        if (fields.ValueKind == JsonValueKind.Object)
                        {
                            if (fields.TryGetProperty("summary", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                                summary = sProp.GetString();

                            if (fields.TryGetProperty("issuetype", out var it) &&
                                it.TryGetProperty("name", out var itn) && itn.ValueKind == JsonValueKind.String)
                                type = itn.GetString();

                            if (fields.TryGetProperty("updated", out var up) &&
                                up.ValueKind == JsonValueKind.String &&
                                DateTime.TryParse(up.GetString(), out var dt))
                                updated = dt;
                        }

                        list.Add(new JiraIssueDictionary
                        {
                            Key = key,
                            Summary = summary,
                            Type = type,
                            Updated = updated
                        });
                    }

                    collected += count;
                    // New API often doesn’t return a total; report incremental progress
                    progress?.Report((collected, collected));

                    nextPageToken = page.TryGetProperty("nextPageToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.String
                        ? tokenEl.GetString()
                        : null;

                    if (count == 0) break;
                } while (!string.IsNullOrWhiteSpace(nextPageToken));
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "The Jira search could not be completed. Please check your connection/query and try again.\n\n" +
                    "Error details: " + ex.Message, ex);
            }

            return list;
        }

        /// <summary>
        /// Displays the search results in the WebView2 browser.
        /// Results are grouped by title and description matches, and rendered as HTML.
        /// </summary>
        /// <param name="issues">List of JiraIssueDictionary results.</param>
        /// <param name="query">Search query string.</param>
        private void ShowResults(List<JiraIssueDictionary> issues, string query, bool isJqlMode = false)
        {
            var matchedTitle = new StringBuilder();
            var matchedDesc = new StringBuilder();
            bool hasQuery = !string.IsNullOrWhiteSpace(query);

            foreach (var issue in issues)
            {
                string key = issue.Key;
                string summary = HttpUtility.HtmlEncode(issue.Summary ?? "");
                string issueType = issue.Type;
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
                            iconPath = $"<img src='data:image/png;base64,{base64}' width='28' height='28' style='vertical-align:middle;margin-right:8px;' title='{issueType}' />";
                        }
                        catch { }
                    }
                }

                string pathHtml = "";
                string path = frmMain.GetRequirementPath(key);
                if (!string.IsNullOrEmpty(path))
                {
                    pathHtml = $"<div style='font-size:0.7em;color:#888;margin-left:48px;margin-top:1px;'>{path}</div>";
                }
                string htmlLink = $"<tr><td class='confluenceTd'><a href=\"#\" data-key=\"{key}\">{iconPath}{summary} [{key}]</a>{pathHtml}</td></tr>";

                if (isJqlMode)
                {
                    matchedTitle.AppendLine(htmlLink); // Use matchedTitle as the single section
                }
                else if (!hasQuery)
                {
                    matchedTitle.AppendLine(htmlLink);
                }
                else if (summary.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedTitle.AppendLine(htmlLink);
                }
                else
                {
                    matchedDesc.AppendLine(htmlLink);
                }
            }

            // If nothing matched, show a message
            if (matchedTitle.Length == 0 && matchedDesc.Length == 0)
            {
                webViewResults.NavigateToString($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
  <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body style=""margin:0; display:flex; align-items:center; justify-content:center; height:100vh; background-color:white;"">
  <h2 style=""color:grey; font-size:2.5em; font-family:Segoe UI, sans-serif; text-align:center;"">
    🤔 No luck. Search again?
  </h2>
</body>
</html>");
                return;
            }

            // Build HTML for results
            string html;
            if (isJqlMode)
            {
                html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
 <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
 <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body>
  <details open>
    <summary>Search Results</summary>
    <section><table class='confluenceTable'>{matchedTitle}</table></section>
  </details>
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
            }
            else
            {
                var sections = new StringBuilder();
                if (matchedTitle.Length > 0)
                {
                    sections.Append($@"
  <details open>
    <summary>Matched by Title</summary>
    <section><table class='confluenceTable'>{matchedTitle}</table></section>
  </details>");
                }
                if (matchedDesc.Length > 0)
                {
                    sections.Append($@"
  <details open>
    <summary>Matched by Description</summary>
    <section><table class='confluenceTable'>{matchedDesc}</table></section>
  </details>");
                }

                html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
 <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
 <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body>
{sections}
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
            }

            try
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(), "monovera_results.html");
                File.WriteAllText(tempFilePath, html);
                webViewResults.CoreWebView2.Navigate(tempFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("NavigateToString error: " + ex.Message);
                webViewResults.NavigateToString($@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
  <link rel='stylesheet' href='{frmMain.cssHref}' />
</head>
<body style=""margin:0; display:flex; align-items:center; justify-content:center; height:100vh; background-color:white;"">
  <div style=""text-align:center;"">
    <h2 style=""color:#b71c1c; font-size:2.2em; font-family:Segoe UI, sans-serif;"">
      ⚠️ Unable to display results
    </h2>
    <p style=""color:#444; font-size:1.2em; max-width:600px; margin:0 auto 1em auto;"">
      The result list is too long or there was an error rendering the results.<br>
      Please filter your search to get more specific results.
    </p>
    <details style=""margin-top:1em;"">
      <summary style=""cursor:pointer; color:#b71c1c;"">Show error details</summary>
      <pre style=""color:#b71c1c; background:#fbe9e7; padding:1em; border-radius:6px; font-size:1em;"">{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>
    </details>
  </div>
</body>
</html>");
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
            SelectNodeByKey(key);
        }
    }
}
