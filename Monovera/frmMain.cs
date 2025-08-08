using HtmlAgilityPack;
using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SharpSvn;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO.Packaging;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms; 
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TrayNotify;
using Font = System.Drawing.Font;
using Image = System.Drawing.Image;

namespace Monovera
{
    /// <summary>
    /// Handles Jira integration, UI setup, and user interactions.
    /// </summary>
    public partial class frmMain : Form
    {
        bool editorMode = true;
        private MouseButtons lastTreeMouseButton = MouseButtons.Left;
        private bool suppressAfterSelect = false;

        /// <summary>
        /// Represents a service for interacting with Jira.
        /// </summary>
        /// <remarks>This field provides access to Jira-related operations and functionalities. It is
        /// intended to be used for managing and retrieving Jira issues, projects, and other related data.</remarks>
        public JiraService jiraService;

        /// <summary>
        /// Base URL for Jira REST API.
        /// </summary>
        public static string jiraBaseUrl = "";

        /// <summary>
        /// Jira user email for authentication.
        /// </summary>
        public static string jiraEmail = "";

        /// <summary>
        /// Jira API token for authentication.
        /// </summary>
        public static string jiraToken = "";

        public static string jiraUserName = "System";

        /// <summary>Maps parent issue keys to their child issues.</summary>
        public static Dictionary<string, List<JiraIssue>> childrenByParent = new();
        /// <summary>Maps issue keys to JiraIssue objects for quick lookup.</summary>
        public static Dictionary<string, JiraIssue> issueDict = new();
        /// <summary>Comma-separated root issue keys for all configured projects.</summary>
        public static string root_key = "";
        /// <summary>List of Jira project keys loaded from configuration.</summary>
        public static List<string> projectList = new();
        
        private List<TreeNode> rootNodeList = new();

        private bool isNavigatingToNode = false;
        private bool suppressTabSelection = false;

        /// <summary>Maps issue type names to icon filenames.</summary>
        public static Dictionary<string, string> typeIcons;
        /// <summary>Maps issue status names to icon filenames.</summary>
        public static Dictionary<string, string> statusIcons;
        /// <summary>Loaded Jira configuration object.</summary>
        public static JiraConfigRoot config;
        /// <summary>Comma-separated link type names used for hierarchy (e.g. "Blocks").</summary>
        public static string hierarchyLinkTypeName = "";

        /// <summary>Tab control for displaying issue details and other pages.</summary>
        private TabControl tabDetails;

        /// <summary>Application directory path.</summary>
        string appDir = "";
        /// <summary>Temporary directory path for storing files.</summary>
        public static string tempFolder = "";

        public static string cssPath = "";
        public static string cssHref = "";
        public static string HTML_LOADINGPAGE = "";

        private frmSearch frmSearchInstance;
        private Image tabDetailsBackgroundImage;

        string focustToTreeJS = @"
<script>
(function() {
    let lastSelectionState = false;

    function hasTextSelection() {
        const sel = window.getSelection();
        return sel && sel.toString().length > 0;
    }

    function checkSelection() {
        const hasSelection = hasTextSelection();
        if (hasSelection && !lastSelectionState) {
            // Selection started, keep focus in WebView2
            if (window.chrome && window.chrome.webview)
                window.chrome.webview.postMessage('__keep_webview_focus__');
        } else if (!hasSelection && lastSelectionState) {
            // Selection cleared, restore focus to tree
            if (window.chrome && window.chrome.webview)
                window.chrome.webview.postMessage('__tree_focus__');
        }
        lastSelectionState = hasSelection;
    }

    document.addEventListener('selectionchange', checkSelection);

    // List of interactive tags
    const interactiveTags = ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON', 'OPTION', 'LABEL'];
    // List of interactive classes (customize as needed)
    const interactiveClasses = ['datepicker', 'dropdown', 'combo', 'calendar'];

    document.addEventListener('mouseup', function(e) {
        if (hasTextSelection()) return; // Do not restore focus if text is selected
        if (interactiveTags.includes(e.target.tagName)) return;
        if (e.target.isContentEditable) return;
        if (e.target.closest('.' + interactiveClasses.join(', .'))) return;
        if (window.chrome && window.chrome.webview)
            window.chrome.webview.postMessage('__tree_focus__');
    });

    // Restore focus after selection or closing of interactive elements
    function restoreTreeFocus() {
        if (hasTextSelection()) return; // Do not restore focus if text is selected
        if (window.chrome && window.chrome.webview)
            window.chrome.webview.postMessage('__tree_focus__');
    }

    // Listen for change and blur events on interactive elements
    interactiveTags.forEach(function(tag) {
        document.querySelectorAll(tag).forEach(function(el) {
            el.addEventListener('change', restoreTreeFocus);
            el.addEventListener('blur', restoreTreeFocus);
        });
    });

    // Listen for custom calendar/date picker close events if available
    interactiveClasses.forEach(function(cls) {
        document.querySelectorAll('.' + cls).forEach(function(el) {
            el.addEventListener('change', restoreTreeFocus);
            el.addEventListener('blur', restoreTreeFocus);
        });
    });

    // For dynamically added elements, use event delegation
    document.body.addEventListener('change', function(e) {
        if (interactiveTags.includes(e.target.tagName) || interactiveClasses.some(cls => e.target.classList.contains(cls))) {
            restoreTreeFocus();
        }
    });
    document.body.addEventListener('blur', function(e) {
        if (interactiveTags.includes(e.target.tagName) || interactiveClasses.some(cls => e.target.classList.contains(cls))) {
            restoreTreeFocus();
        }
    }, true);
})();
</script>";

        /// <summary>
        /// Root configuration for Jira integration.
        /// </summary>
        public class JiraConfigRoot
        {
            /// <summary>Jira authentication information.</summary>
            public JiraAuth Jira { get; set; }
            /// <summary>List of project-specific configuration objects.</summary>
            public List<JiraProjectConfig> Projects { get; set; }
        }

        /// <summary>
        /// Jira authentication details.
        /// </summary>
        public class JiraAuth
        {
            /// <summary>Jira instance base URL.</summary>
            public string Url { get; set; }
            /// <summary>Jira user email.</summary>
            public string Email { get; set; }
            /// <summary>Jira API token.</summary>
            public string Token { get; set; }
        }

        /// <summary>
        /// Configuration for a single Jira project.
        /// </summary>
        public class JiraProjectConfig
        {
            /// <summary>Project key (e.g. "PROJECT1").</summary>
            public string Project { get; set; }
            /// <summary>Root issue key for the project.</summary>
            public string Root { get; set; }
            /// <summary>Link type name used for hierarchy (e.g. "Blocks").</summary>
            public string LinkTypeName { get; set; }
            /// <summary>Field name to sort child nodes by (e.g. "created", "summary", "customfield_12345").</summary>
            public string SortingField { get; set; } // <-- Add this line
            /// <summary>Maps issue type names to icon filenames.</summary>
            public Dictionary<string, string> Types { get; set; }
            /// <summary>Maps status names to icon filenames.</summary>
            public Dictionary<string, string> Status { get; set; }

            public bool HasCreatePermission { get; set; }
            public bool HasEditPermission { get; set; }
        }

        /// <summary>
        /// Represents a Jira issue for hierarchy and tree display.
        /// </summary>
        public class JiraIssue
        {
            /// <summary>Issue key (e.g. "PRJ1-100").</summary>
            public string Key { get; set; }
            /// <summary>Issue summary/title.</summary>
            public string Summary { get; set; }
            /// <summary>Issue type name.</summary>
            public string Type { get; set; }
            /// <summary>Parent issue key, if any.</summary>
            public string ParentKey { get; set; }
            /// <summary>List of related issue keys (issue links).</summary>
            public List<string> RelatedIssueKeys { get; set; } = new List<string>();

            public string SortingField { get; set; }
        }

        /// <summary>
        /// Represents a link between Jira issues.
        /// </summary>
        public class JiraIssueLink
        {
            /// <summary>Link type name (e.g. "Blocks").</summary>
            public string LinkTypeName { get; set; } = "";
            /// <summary>Outward issue key (the linked issue).</summary>
            public string OutwardIssueKey { get; set; } = "";
            /// <summary>Outward issue summary/title.</summary>
            public string OutwardIssueSummary { get; set; } = "";
            /// <summary>Outward issue type name.</summary>
            public string OutwardIssueType { get; set; } = "";
        }

        /// <summary>
        /// Data transfer object for Jira issues, including links and timestamps.
        /// Used for parsing and caching.
        /// </summary>
        public class JiraIssueDto
        {
            /// <summary>Issue key.</summary>
            public string Key { get; set; }
            /// <summary>Issue summary/title.</summary>
            public string Summary { get; set; }
            /// <summary>Issue type name.</summary>
            public string Type { get; set; }
            /// <summary>List of issue links.</summary>
            public List<JiraIssueLink> IssueLinks { get; set; } = new();

            public string SortingField { get; set; }

            /// <summary>Last updated timestamp.</summary>
            public DateTime? Updated { get; set; }
            /// <summary>Created timestamp.</summary>
            public DateTime? Created { get; set; }

            public Dictionary<string, object> CustomFields { get; set; } = new();
        }

        private NotifyIcon notifyIcon;

        private void InitializeNotifyIcon()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Visible = true;
            notifyIcon.Icon = SystemIcons.Information; // You can use your own .ico file if needed
            notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            notifyIcon.BalloonTipTitle = "Node Not Found!";
        }

        private void ShowTrayNotification(string key)
        {
            string message = $"{key} was not found in the tree. If this belongs to one of loaded projects, please update the hierarchy to view it.";
            notifyIcon.BalloonTipText = message;
            notifyIcon.ShowBalloonTip(5000); // Show for 5 seconds
        }

        /// <summary>
        /// Alphanumeric comparer for natural sorting (e.g. 1,2,10,11).
        /// </summary>
        public class AlphanumericComparer : IComparer<object>
        {
            public int Compare(object x, object y)
            {
                string sx = x?.ToString() ?? "";
                string sy = y?.ToString() ?? "";
                return AlphanumericCompare(sx, sy);
            }

            // Natural sort implementation
            private static int AlphanumericCompare(string s1, string s2)
            {
                var regex = new Regex(@"\d+|\D+");
                var e1 = regex.Matches(s1);
                var e2 = regex.Matches(s2);
                int i = 0;
                while (i < e1.Count && i < e2.Count)
                {
                    string a = e1[i].Value;
                    string b = e2[i].Value;
                    int result;
                    if (int.TryParse(a, out int na) && int.TryParse(b, out int nb))
                        result = na.CompareTo(nb);
                    else
                        result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                    if (result != 0)
                        return result;
                    i++;
                }
                return e1.Count.CompareTo(e2.Count);
            }
        }

        /// <summary>
        /// Gets an Image from the application's images folder by file name.
        /// Returns null if the file does not exist or cannot be loaded.
        /// </summary>
        /// <param name="fileName">The image file name (e.g., "settings.png").</param>
        /// <returns>The loaded Image, or null if not found.</returns>
        public static System.Drawing.Image? GetImageFromImagesFolder(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
            if (!File.Exists(imagePath))
                return null;

            try
            {
                return System.Drawing.Image.FromFile(imagePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Initializes the main form, sets up UI controls, event handlers, and directories.
        /// </summary>
        public frmMain()
        {
            // Set up application and temp directories
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            tempFolder = Path.Combine(appDir, "temp");

            Directory.CreateDirectory(tempFolder);
            cssPath = Path.Combine(appDir, "monovera.css");
            cssHref = new Uri(cssPath).AbsoluteUri;
            if (!File.Exists(cssPath))
            {
                cssHref = "https://raw.githubusercontent.com/monovera/monovera/main/monovera.css";
            }
            else
            {

            }
            InitializeComponent();
            InitializeNotifyIcon();

            StartJiraUpdateQueueWorker();

            // Initialize context menu for tree
            InitializeContextMenu();
            SetupSpinMessages();

            //Set menu icons
            mnuSettings.Image = GetImageFromImagesFolder("Settings.png");
            mnuUpdateHierarchy.Image = GetImageFromImagesFolder("Update.png");
            mnuConfiguration.Image = GetImageFromImagesFolder("Configuration.png");

            mnuActions.Image = GetImageFromImagesFolder("Actions.png");
            mnuSearch.Image = GetImageFromImagesFolder("Search.png");
            mnuReport.Image = GetImageFromImagesFolder("GenerateReport.png");
            mnuRead.Image = GetImageFromImagesFolder("Read.png");
            mnuRecentUpdates.Image= GetImageFromImagesFolder("Monovera.png");  

            // Set up tab control for details panel
            tabDetails = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "tabDetails",
                BackColor = Color.Red
            };
            tabDetails.SelectedIndexChanged += TabDetails_SelectedIndexChanged;
            tabDetails.ShowToolTips = true;
            tabDetails.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabDetails.DrawItem += TabDetails_DrawItem;
            tabDetails.MouseDown += tabDetails_MouseDown;
            tabDetails.ItemSize = new Size(200, 30);
            tabDetails.Padding = new Point(40, 5);
            panelTabs.Controls.Add(tabDetails);

            InitializeTabContextMenu();
            EnableTabDragDrop();


            tree.BackColor = GetCSSColor_Tree_Background(cssPath);
            tree.ForeColor = GetCSSColor_Tree_Foreground(cssPath);
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += Tree_DrawNode;
            tree.BeforeExpand += tree_BeforeExpand;

            // Tree mouse event for context menu
            if (editorMode)
            {
                tree.AllowDrop = true;
                tree.ItemDrag += tree_ItemDrag;
                tree.DragEnter += tree_DragEnter;
                tree.DragOver += tree_DragOver;
                tree.DragDrop += tree_DragDrop;
                tree.MouseUp += tree_MouseUp;
            }

            // Enable keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += frmMain_KeyDown;
        }

        private TreeNode draggedNode;

        private void Tree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            var tree = sender as System.Windows.Forms.TreeView;
            bool isSelected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;

            // Set background color
            Color nodeBackColor = isSelected ? GetCSSColor_Tree_SelectionBackground(cssPath) : GetCSSColor_Tree_Background(cssPath);
            Color nodeForeColor = GetCSSColor_Tree_Foreground(cssPath);

            using (var bgBrush = new SolidBrush(nodeBackColor))
            {
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            }

            // Set font style
            Font nodeFont = isSelected ? new Font(e.Node.NodeFont ?? tree.Font, FontStyle.Underline) : (e.Node.NodeFont ?? tree.Font);

            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                nodeFont,
                e.Bounds,
                nodeForeColor,
                TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter
            );

            // Prevent default highlight
            e.DrawDefault = false;
        }

        private void tree_ItemDrag(object sender, ItemDragEventArgs e)
        {
            suppressAfterSelect = true;
            draggedNode = e.Item as TreeNode;
            if (draggedNode != null)
                tree.DoDragDrop(draggedNode, DragDropEffects.Move);
        }

        private void tree_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TreeNode)))
                e.Effect = DragDropEffects.Move;
        }

        private void tree_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
            Point pt = tree.PointToClient(new Point(e.X, e.Y));
            tree.SelectedNode = tree.GetNodeAt(pt);
        }

        private async void tree_DragDrop(object sender, DragEventArgs e)
        {
            suppressAfterSelect = false;
            Point pt = tree.PointToClient(new Point(e.X, e.Y));
            TreeNode targetNode = tree.GetNodeAt(pt);
            TreeNode nodeToMove = (TreeNode)e.Data.GetData(typeof(TreeNode));

            var rootKeys = root_key.Split(',').Select(k => k.Trim()).ToHashSet();

            // Prevent dragging root node, dropping onto itself, or into its own descendant
            if (nodeToMove == null || targetNode == null || nodeToMove == targetNode || IsDescendant(nodeToMove, targetNode))
                return;
            if (rootKeys.Contains(nodeToMove.Tag?.ToString()))
                return;

            // Custom confirmation dialog (unchanged)
            var DialogMoveConfirm = new Form
            {
                Text = "Confirm Parent Change",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Width = 500,
                Height = 220,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                Padding = new Padding(20),
            };

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
            if (File.Exists(iconPath))
            {
                DialogMoveConfirm.Icon = new Icon(iconPath);
            }

            var lbl = new Label
            {
                Text = $"Are you sure you want to move '{nodeToMove.Tag}' under '{targetNode.Tag}'?\n\n" +
                       $"🌳 {targetNode.Tag}\n" +
                       $"   └── 🌱 {nodeToMove.Tag}",
                Dock = DockStyle.Top,
                Height = 80,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Padding = new Padding(0, 10, 0, 10),
                AutoSize = true
            };

            var btnMove = CreateDialogButton("Move", DialogResult.Yes, true);
            var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Padding = new Padding(0, 10, 0, 0),
                Height = 60,
                AutoSize = true
            };

            btnMove.Margin = new Padding(10, 0, 0, 0);
            btnCancel.Margin = new Padding(10, 0, 0, 0);

            buttonPanel.Controls.Add(btnMove);
            buttonPanel.Controls.Add(btnCancel);

            DialogMoveConfirm.Controls.Add(lbl);
            DialogMoveConfirm.Controls.Add(buttonPanel);

            DialogMoveConfirm.AcceptButton = btnMove;
            DialogMoveConfirm.CancelButton = btnCancel;

            var result = DialogMoveConfirm.ShowDialog(this);

            if (result != DialogResult.Yes)
                return;

            // --- Ensure children of targetNode are loaded before dropping ---
            if (targetNode.Nodes.Count == 1 && targetNode.Nodes[0].Tag?.ToString() == "DUMMY")
            {
                // Expand and trigger loading
                targetNode.Expand();
                // Wait for UI to process expand and children to load
                await Task.Delay(100); // Adjust delay as needed for your loading speed

                // Optionally, force loading if still dummy
                if (targetNode.Nodes.Count == 1 && targetNode.Nodes[0].Tag?.ToString() == "DUMMY")
                {
                    tree_BeforeExpand(tree, new TreeViewCancelEventArgs(targetNode, false, TreeViewAction.Expand));
                    await Task.Delay(50);
                }
            }

            // --- Move node in tree immediately (UI thread) ---
            string oldParentKey = nodeToMove.Parent?.Tag as string;
            if (nodeToMove.Parent != null)
                nodeToMove.Parent.Nodes.Remove(nodeToMove);
            else
                tree.Nodes.Remove(nodeToMove);

            // Add as last child
            targetNode.Nodes.Add(nodeToMove);

            targetNode.Expand();
            tree.SelectedNode = nodeToMove;

            // --- Enqueue Jira parent link update and sequence updates ---
            string newParentKey = targetNode.Tag as string;
            string movedKey = nodeToMove.Tag as string;
            string linkTypeName = hierarchyLinkTypeName.Split(',')[0];

            // Enqueue parent link update as a special tuple (key, sequence) where sequence = -1 means parent update
            sequenceUpdateQueue.Enqueue(($"{movedKey}|{oldParentKey}|{newParentKey}|{linkTypeName}", -1));

            // Enqueue sequence updates for all siblings under the new parent
            for (int i = 0; i < targetNode.Nodes.Count; i++)
            {
                string siblingKey = targetNode.Nodes[i].Tag as string;
                int sequence = i + 1;
                sequenceUpdateQueue.Enqueue((siblingKey, sequence));
            }
        }

        private void StartJiraUpdateQueueWorker()
        {
            if (sequenceUpdateWorker != null)
                return; // Only start once

            sequenceUpdateWorker = Task.Run(async () =>
            {
                while (!sequenceUpdateCts.Token.IsCancellationRequested)
                {
                    if (sequenceUpdateQueue.TryDequeue(out var item))
                    {
                        UpdateJiraUpdateStatus(true); // Show red dot and "Updating Jira..."
                        try
                        {
                            if (item.sequence == -1 && item.key.Contains("|"))
                            {
                                // Parent link update
                                var parts = item.key.Split('|');
                                if (parts.Length == 4)
                                {
                                    string movedKey = parts[0];
                                    string oldParentKey = parts[1];
                                    string newParentKey = parts[2];
                                    string linkTypeName = parts[3];
                                    await jiraService.UpdateParentLinkAsync(movedKey, oldParentKey, newParentKey, linkTypeName);
                                }
                            }
                            else
                            {
                                // Sequence update
                                await jiraService.UpdateSequenceFieldAsync(item.key, item.sequence);
                            }
                        }
                        catch (Exception ex)
                        {
                            this.Invoke(() =>
                                MessageBox.Show(ex.Message, "Jira Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
                        }
                    }
                    else
                    {
                        UpdateJiraUpdateStatus(false); // Show green dot and "Jira updates completed!"
                        await Task.Delay(50, sequenceUpdateCts.Token);
                    }
                }
            }, sequenceUpdateCts.Token);
        }

        private void UpdateJiraUpdateStatus(bool isProcessing)
        {
            // Use the form for thread marshaling
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateJiraUpdateStatus(isProcessing)));
                return;
            }
            if (isProcessing)
            {
                lblJiraUpdateProcessing.Text = "🔴 Processing Jira updates!...";
                lblJiraUpdateProcessing.ForeColor = Color.Red;
            }
            else
            {
                lblJiraUpdateProcessing.Text = "🟢 No pending Jira updates!";
                lblJiraUpdateProcessing.ForeColor = Color.Green;
            }
        }

        // Helper to prevent dropping a node into its own descendant
        private bool IsDescendant(TreeNode node, TreeNode potentialParent)
        {
            TreeNode current = potentialParent;
            while (current != null)
            {
                if (current == node)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// Handles keyboard shortcuts for search and report generation.
        /// </summary>
        private void frmMain_KeyDown(object sender, KeyEventArgs e)
        {
            handleShortcuts(e);

            // Only process if tree has focus and a node is selected
            if (tree.Focused && tree.SelectedNode != null)
            {
                if (e.Control && e.KeyCode == Keys.Up)
                {
                    e.SuppressKeyPress = true;
                    MoveNodeInTree(tree.SelectedNode, -1);
                }
                else if (e.Control && e.KeyCode == Keys.Down)
                {
                    e.SuppressKeyPress = true;
                    MoveNodeInTree(tree.SelectedNode, 1);
                }
            }
        }

        // Add these fields to frmMain
        private readonly ConcurrentQueue<(string key, int sequence)> sequenceUpdateQueue = new();
        private readonly CancellationTokenSource sequenceUpdateCts = new();
        private Task sequenceUpdateWorker;

        private async void MoveNodeInTree(TreeNode node, int direction)
        {
            if (node == null) return;
            TreeNodeCollection siblings = node.Parent?.Nodes ?? tree.Nodes;
            int index = siblings.IndexOf(node);
            int newIndex = index + direction;

            if (newIndex < 0 || newIndex >= siblings.Count)
                return;

            // Show confirmation dialog before moving
            using (var DialogMoveConfirmation = new Form
            {
                Text = direction < 0 ? "Confirm Move Up" : "Confirm Move Down",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Width = 500,
                Height = 180,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                Padding = new Padding(20),
            })
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                if (File.Exists(iconPath))
                {
                    DialogMoveConfirmation.Icon = new Icon(iconPath);
                }

                var lbl = new Label
                {
                    Text = $"Are you sure you want to move '{node.Tag}' {(direction < 0 ? "up" : "down")}?",
                    Dock = DockStyle.Top,
                    Height = 60,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    Padding = new Padding(0, 10, 0, 10)
                };

                var btnMove = CreateDialogButton(direction < 0 ? "Move Up" : "Move Down", DialogResult.Yes,true);
                var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);

                var buttonPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Dock = DockStyle.Bottom,
                    Padding = new Padding(0, 10, 0, 0),
                    Height = 60,
                    AutoSize = true
                };

                btnMove.Margin = new Padding(10, 0, 0, 0);
                btnCancel.Margin = new Padding(10, 0, 0, 0);

                buttonPanel.Controls.Add(btnMove);
                buttonPanel.Controls.Add(btnCancel);

                DialogMoveConfirmation.Controls.Add(lbl);
                DialogMoveConfirmation.Controls.Add(buttonPanel);

                DialogMoveConfirmation.AcceptButton = btnMove;
                DialogMoveConfirmation.CancelButton = btnCancel;

                var result = DialogMoveConfirmation.ShowDialog(this);

                if (result != DialogResult.Yes)
                    return;
            }

            siblings.RemoveAt(index);
            siblings.Insert(newIndex, node);

            // Reselect the moved node in its new position
            tree.SelectedNode = null; // Clear selection first
            tree.SelectedNode = node;
            node.EnsureVisible();
            tree.Focus();
            System.Windows.Forms.Application.DoEvents(); // Optional: helps UI update in some cases

            // Enqueue sequence updates for all siblings
            for (int i = 0; i < siblings.Count; i++)
            {
                string siblingKey = siblings[i].Tag as string;
                int sequence = i + 1;
                sequenceUpdateQueue.Enqueue((siblingKey, sequence));
            }
        }

        private System.Windows.Forms.Timer marqueeTimer;
        private string[] messages = new[]
                                        {
                                            "💡 M O N O V E R A Tips!",
                                            "💡 Ctrl + Click a tree node to reload detail",
                                            "💡 Right Click a tree node to get context menu",
                                            "💡 Ctrl + Q = Open search dialog",
                                            "💡 Ctrl + P = Generate report",
                                            "💡 Ctrl + R = Read selected text aloud",
                                            "💡 For all shortcuts, press after clicking tree area"
                                        };

        private int currentIndex = 0;
        private System.Timers.Timer spinTimer;

        private void SetupSpinMessages()
        {
            marqueeTimer = new System.Windows.Forms.Timer();
            marqueeTimer.Interval = 5000; // 3 seconds per message, adjust as you want
            marqueeTimer.Tick += SpinTimer_Tick;
            marqueeTimer.Start();

            lblShortcuts.Text = messages[0]; // show first immediately
        }

        private void SpinTimer_Tick(object? sender, EventArgs e)
        {
            currentIndex = (currentIndex + 1) % messages.Length;
            lblShortcuts.Text = messages[currentIndex];
        }


        /// <summary>
        /// Processes keyboard shortcuts asynchronously.
        /// </summary>
        private async Task handleShortcuts(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Q)
            {
                e.SuppressKeyPress = true;
                ShowSearchDialog();
            }
            else if (e.Control && e.KeyCode == Keys.P)
            {
                e.SuppressKeyPress = true;
                GenerateReport();
            }
            else if (e.Control && e.KeyCode == Keys.R)
            {
                e.SuppressKeyPress = true;
                await SpeakSelectedTextOnActiveTabAsync();
            }
        }

        private async Task SpeakSelectedTextOnActiveTabAsync()
        {
            ReadSelectedTextOutLoud();
        }

        /// <summary>
        /// Handles right-click selection in the tree view.
        /// </summary>
        private void tree_MouseUp(object sender, MouseEventArgs e)
        {
            lastTreeMouseButton = e.Button;
            suppressAfterSelect = false;

            var clickedNode = tree.GetNodeAt(e.X, e.Y);
            bool isCtrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;

            if (e.Button == MouseButtons.Right)
            {
                if (clickedNode != null)
                {
                    tree.SelectedNode = clickedNode;
                }
            }
            else if (e.Button == MouseButtons.Left && isCtrlPressed && clickedNode != null)
            {
                // If Ctrl+Left click on a node, always reload, even if already selected
                Tree_AfterSelect(tree, new TreeViewEventArgs(clickedNode));
            }
        }

        /// <summary>
        /// Context menu for the tree view, providing search and report actions.
        /// </summary>
        private ContextMenuStrip treeContextMenu;

        /// <summary>
        /// Menu item for searching issues in the tree.
        /// </summary>
        private ToolStripMenuItem searchMenuItem;

        /// <summary>
        /// Menu item for generating a hierarchical report.
        /// </summary>
        private ToolStripMenuItem reportMenuItem;
        // Add this field to frmMain
        public static Dictionary<string, JiraIssueDto> issueDtoDict = new();
        // Add these fields to frmMain
        private ContextMenuStrip tabContextMenu;
        private TabPage rightClickedTab;
        // Add these fields to frmMain
        private int dragTabIndex = -1;

        /// <summary>
        /// Initializes the context menu for the tree view, including search and report options.
        /// </summary>
        private void InitializeContextMenu()
        {
            // Create the context menu strip
            treeContextMenu = new ContextMenuStrip();
            // Assign the context menu to the tree view
            tree.ContextMenuStrip = treeContextMenu;

            AddSearchMenu();
            AddGenerateReportMenu();

            if (editorMode)
            {
                AddSeparatorMenu();
                AddLinkRelatedMenu();
                AddChangeParentMenu();
                AddSeparatorMenu();
                AddEditMenu();
                AddSeparatorMenu();
                AddCreateIssueMenus();
                AddSeparatorMenu();
                AddUpDownMenus();
            }

            treeContextMenu.Opening += (s, e) =>
            {
                var node = tree.SelectedNode;
                if (node == null) return;

                // Find project config for this node
                string key = node.Tag?.ToString();
                var projectConfig = config.Projects.FirstOrDefault(p => key != null && key.StartsWith(p.Root.Split("-")[0], StringComparison.OrdinalIgnoreCase));
                bool canCreate = projectConfig?.HasCreatePermission ?? false;
                bool canModify = projectConfig?.HasEditPermission ?? false;

                // Enable/disable create menu items based on permission
                foreach (ToolStripItem item in treeContextMenu.Items)
                {
                    if (item.Text.StartsWith("Add Child")
                    || item.Text.StartsWith("Add Sibling"))
                    {
                        item.Enabled = canCreate;
                    }
                    else if (item.Text.StartsWith("Link related")
                    || item.Text.StartsWith("Change Parent")
                    || item.Text.StartsWith("Move Up")
                    || item.Text.StartsWith("Move Down")
                    || item.Text.StartsWith("Edit"))
                    {
                        item.Enabled = canModify;
                    }
                }
            };

            treeContextMenu.Closed += treeContextMenu_Closed;
        }


        private void AddSeparatorMenu()
        {
            var separatorMenu = new ToolStripSeparator();
            treeContextMenu.Items.Add(separatorMenu);
        }

        private void AddSearchMenu()
        {
            // Create the search menu item with shortcut and icon
            var iconSearch = CreateUnicodeIcon("🔍");
            searchMenuItem = new ToolStripMenuItem("Search")
            {
                Image = iconSearch,
                ShortcutKeys = Keys.Control | Keys.Q,
                ShowShortcutKeys = true
            };

            // Attach event handlers for menu item clicks
            searchMenuItem.Click += (s, e) => ShowSearchDialog();

            // Add menu items to the context menu
            treeContextMenu.Items.Add(searchMenuItem);
        }

        private void AddGenerateReportMenu()
        {
            // Create the report menu item with shortcut and icon
            var iconReport = CreateUnicodeIcon("📄");
            reportMenuItem = new ToolStripMenuItem("Generate Report")
            {
                Image = iconReport,
                ShortcutKeys = Keys.Control | Keys.P,
                ShowShortcutKeys = true
            };

            reportMenuItem.Click += async (s, e) =>
            {
                GenerateReport();
            };

            treeContextMenu.Items.Add(reportMenuItem);
        }

        private void AddEditMenu()
        {
            // Add Edit... menu item to tree context menu
            var iconEdit = CreateUnicodeIcon("✏️");
            var editMenuItem = new ToolStripMenuItem("Edit...", iconEdit);
            editMenuItem.Click += async (s, e) =>
            {
                EditCurrentIssue(true);
            };

            treeContextMenu.Items.Add(editMenuItem);
        }

        private void AddLinkRelatedMenu()
        {
            var iconLink = CreateUnicodeIcon("🔗");
            var linkRelatedMenuItem = new ToolStripMenuItem("Link related...", iconLink);
            linkRelatedMenuItem.Click += async (s, e) =>
            {
                await ShowLinkRelatedDialogAsync();
            };
            treeContextMenu.Items.Add(linkRelatedMenuItem);
        }

        private async Task ShowLinkRelatedDialogAsync()
        {
            if (tree.SelectedNode == null) return;
            string baseKey = tree.SelectedNode.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(baseKey)) return;

            // Gather all keys and summaries from issueDtoDict instead of the tree
            var allKeys = issueDtoDict.Keys.ToList();
            var keySummaryDict = issueDtoDict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Summary ?? ""
            );

            var DialogLinkRelatedIssues = new Form
            {
                Text = $"Link related issues to {baseKey}",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Width = 700,
                Height = 440,
                BackColor = GetCSSColor_Tree_Background(cssPath),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10)
            };

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
            if (File.Exists(iconPath))
            {
                DialogLinkRelatedIssues.Icon = new Icon(iconPath);
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(18, 18, 18, 18),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                AutoSize = false
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var lblTitle = new Label
            {
                Text = $"Link related issues to: {baseKey}",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = GetCSSColor_Title(cssPath)
            };
            layout.Controls.Add(lblTitle, 0, 0);

            // Input + Add button
            var txtInput = new System.Windows.Forms.TextBox
            {
                Font = new Font("Segoe UI", 10),
                Width = 480,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            var autoSource = new AutoCompleteStringCollection();
            autoSource.AddRange(allKeys.ToArray());
            txtInput.AutoCompleteCustomSource = autoSource;
            txtInput.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            txtInput.AutoCompleteSource = AutoCompleteSource.CustomSource;

            var btnAdd = new System.Windows.Forms.Button
            {
                Text = "+",
                Width = 36,
                Height = 32,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Color.White,
                Margin = new Padding(6, 0, 0, 0)
            };

            var inputPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0)
            };
            inputPanel.Controls.Add(txtInput);
            inputPanel.Controls.Add(btnAdd);
            layout.Controls.Add(inputPanel, 0, 1);

            // DataGridView setup
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = GetCSSColor_DataGrid_Background(cssPath),
                ForeColor = GetCSSColor_DataGrid_Foreground(cssPath),
                RowHeadersVisible = false,
                MultiSelect = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    SelectionBackColor = GetCSSColor_DataGrid_SelectionBackground(cssPath),
                    SelectionForeColor = GetCSSColor_DataGrid_SelectionForeground(cssPath)
                },
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            var colKey = new DataGridViewTextBoxColumn
            {
                Name = "KEY",
                HeaderText = "KEY",
                Width = 120,
                FillWeight = 20,
                MinimumWidth = 100
            };
            var colSummary = new DataGridViewTextBoxColumn
            {
                Name = "SUMMARY",
                HeaderText = "SUMMARY",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            grid.Columns.Add(colKey);
            grid.Columns.Add(colSummary);

            layout.Controls.Add(grid, 0, 2);

            // Add keys to grid
            void AddKeysToGrid(string input)
            {
                var keys = input.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(k => k.Trim().ToUpper())
                                .Distinct();

                foreach (var key in keys)
                {
                    if (key == baseKey.ToUpper())
                    {
                        MessageBox.Show($"You cannot link {baseKey.ToUpper()} to itself so it will be skipped.", "Validation Failed!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        continue;
                    }
                    if (!allKeys.Contains(key))
                    {
                        MessageBox.Show($"The key you specified '{key}' was not found as a valid key to link. This will be skipped.", "Validation Failed!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        continue;
                    }

                    var existingRow = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Cells[0].Value?.ToString() == key);
                    if (existingRow != null)
                    {
                        grid.ClearSelection();
                        existingRow.Selected = true;
                        grid.FirstDisplayedScrollingRowIndex = existingRow.Index;
                        continue;
                    }

                    int rowIndex = grid.Rows.Add(key, keySummaryDict.TryGetValue(key, out var summary) ? summary : "");
                    grid.ClearSelection();
                    grid.Rows[rowIndex].Selected = true;
                    grid.FirstDisplayedScrollingRowIndex = rowIndex;
                }
            }

            txtInput.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    AddKeysToGrid(txtInput.Text);
                    txtInput.Clear();
                    e.SuppressKeyPress = true;
                    e.Handled = true; // Prevent dialog from closing
                }
            };

            btnAdd.Click += (s, e) =>
            {
                AddKeysToGrid(txtInput.Text);
                txtInput.Clear();
            };

            grid.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && grid.SelectedRows.Count > 0)
                {
                    foreach (DataGridViewRow row in grid.SelectedRows)
                    {
                        if (!row.IsNewRow)
                            grid.Rows.Remove(row);
                    }
                    e.Handled = true;
                }
            };

            layout.RowStyles.Clear();
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // Title
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // Input
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // Button panel (increased height)

            // Button Panel
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Top, // Ensures full visibility in TableLayoutPanel
                Padding = new Padding(0, 8, 0, 0),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                AutoSize = false,
                Height = 56 // Should match or exceed button height
            };

            var btnLink = CreateDialogButton("Link", DialogResult.OK, true);
            var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);
          
            buttonPanel.Controls.Add(btnLink);
            buttonPanel.Controls.Add(btnCancel);
            layout.Controls.Add(buttonPanel, 0, 3);

            DialogLinkRelatedIssues.Controls.Add(layout);
            //frmSearchInstance.AcceptButton = btnLink;
            DialogLinkRelatedIssues.CancelButton = btnCancel;

            btnLink.Enter += (s, e) => DialogLinkRelatedIssues.AcceptButton = btnLink;
            txtInput.Enter += (s, e) => DialogLinkRelatedIssues.AcceptButton = null;

            if (DialogLinkRelatedIssues.ShowDialog(tree) == DialogResult.OK)
            {
                var keysToLink = grid.Rows.Cast<DataGridViewRow>()
                    .Select(r => r.Cells[0].Value?.ToString())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList();

                if (keysToLink.Count == 0)
                {
                    MessageBox.Show("No issues selected to link.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                tree.Enabled = false;
                try
                {
                    await jiraService.LinkRelatedIssuesAsync(baseKey, keysToLink);
                    MessageBox.Show("Related issues linked successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Jira Link Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    tree.Enabled = true;
                }
            }
        }

        private void AddChangeParentMenu()
        {
            var iconChangeParent = CreateUnicodeIcon("🌳");
            var changeParentMenuItem = new ToolStripMenuItem("Change Parent...", iconChangeParent);
            changeParentMenuItem.Click += async (s, e) =>
            {
                if (tree.SelectedNode == null) return;
                string childKey = tree.SelectedNode.Tag?.ToString();
                string oldParentKey = tree.SelectedNode.Parent?.Tag?.ToString();

                // Gather all keys from the tree
                List<string> allKeys = new List<string>();
                void CollectKeys(TreeNodeCollection nodes)
                {
                    foreach (TreeNode node in nodes)
                    {
                        if (node.Tag is string key && !string.IsNullOrWhiteSpace(key))
                            allKeys.Add(key);
                        CollectKeys(node.Nodes);
                    }
                }
                CollectKeys(tree.Nodes);

                // Get mouse position for dialog
                Point menuLocation = tree.PointToClient(treeContextMenu.Bounds.Location);

                using (var DialogChangeParentMenu = new Form())
                {
                    DialogChangeParentMenu.Text = $"Change Parent of {childKey}";
                    DialogChangeParentMenu.FormBorderStyle = FormBorderStyle.FixedDialog;
                    DialogChangeParentMenu.StartPosition = FormStartPosition.CenterScreen;
                    DialogChangeParentMenu.Width = 420;
                    DialogChangeParentMenu.Height = 210;
                    DialogChangeParentMenu.BackColor = GetCSSColor_Tree_Background(cssPath);
                    DialogChangeParentMenu.MaximizeBox = false;
                    DialogChangeParentMenu.MinimizeBox = false;
                    DialogChangeParentMenu.ShowInTaskbar = false;
                    DialogChangeParentMenu.Font = new Font("Segoe UI", 10);

                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                    if (File.Exists(iconPath))
                    {
                        DialogChangeParentMenu.Icon = new Icon(iconPath);
                    }

                    var layout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 3,
                        Padding = new Padding(24, 18, 24, 18),
                        BackColor = GetCSSColor_Tree_Background(cssPath),
                        AutoSize = true
                    };
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
                    layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

                    var lblTitle = new Label
                    {
                        Text = $"Change Parent of: {tree.SelectedNode.Tag}",
                        Font = new Font("Segoe UI", 11, FontStyle.Bold),
                        Dock = DockStyle.Top,
                        Height = 32,
                        TextAlign = ContentAlignment.MiddleLeft,
                        ForeColor = GetCSSColor_Title(cssPath)
                    };
                    layout.SetColumnSpan(lblTitle, 2);
                    layout.Controls.Add(lblTitle, 0, 0);

                    var lblPrompt = new Label
                    {
                        Text = "New Parent Key:",
                        TextAlign = ContentAlignment.MiddleRight,
                        Dock = DockStyle.Fill
                    };
                    layout.Controls.Add(lblPrompt, 0, 1);

                    var cmbInput = new System.Windows.Forms.ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDown,
                        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                        AutoCompleteSource = AutoCompleteSource.CustomSource,
                        Dock = DockStyle.Fill,
                        Font = new Font("Segoe UI", 10)
                    };
                    var autoSource = new AutoCompleteStringCollection();
                    autoSource.AddRange(allKeys.ToArray());
                    cmbInput.AutoCompleteCustomSource = autoSource;
                    cmbInput.Items.AddRange(allKeys.ToArray());
                    layout.Controls.Add(cmbInput, 1, 1);

                    var buttonPanel = new FlowLayoutPanel
                    {
                        FlowDirection = FlowDirection.RightToLeft,
                        Dock = DockStyle.Fill,
                        Padding = new Padding(0, 8, 0, 0),
                        BackColor = GetCSSColor_Tree_Background(cssPath)
                    };

                    var btnOk = CreateDialogButton("Change", DialogResult.OK, true);
                    var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);

                    buttonPanel.Controls.Add(btnOk);
                    buttonPanel.Controls.Add(btnCancel);
                    layout.Controls.Add(buttonPanel, 1, 2);

                    DialogChangeParentMenu.Controls.Add(layout);
                    DialogChangeParentMenu.AcceptButton = btnOk;
                    DialogChangeParentMenu.CancelButton = btnCancel;

                    if (DialogChangeParentMenu.ShowDialog(tree) == DialogResult.OK)
                    {
                        string newParentKey = cmbInput.Text.Trim();
                        if (string.IsNullOrWhiteSpace(newParentKey))
                        {
                            MessageBox.Show("Please enter a valid parent key.", "Input Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        // Get link type name for this issue/project
                        string linkTypeName = "";
                        var dashIndex = childKey?.IndexOf('-') ?? -1;
                        if (dashIndex > 0)
                        {
                            var keyPrefix = childKey.Substring(0, dashIndex);
                            var projectConfig = config?.Projects?.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                            linkTypeName = projectConfig?.LinkTypeName ?? hierarchyLinkTypeName.Split(',')[0];
                        }
                        else
                        {
                            linkTypeName = hierarchyLinkTypeName.Split(',')[0];
                        }


                        tree.Enabled = false;
                        try
                        {
                            await jiraService.UpdateParentLinkAsync(childKey, oldParentKey, newParentKey, linkTypeName);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                ex.Message,
                                "Jira Link Type Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                            // Restore UI state
                            tree.Enabled = true;
                            return;
                        }

                        // Move node in tree
                        TreeNode nodeToMove = tree.SelectedNode;
                        TreeNode newParentNode = await ExpandPathToKeyAsync(newParentKey);
                        if (newParentNode == null)
                        {
                            MessageBox.Show($"New parent node '{newParentKey}' not found in tree.", "Parent Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            tree.Enabled = false;
                            return;
                        }

                        // Remove from old parent
                        if (nodeToMove.Parent != null)
                            nodeToMove.Parent.Nodes.Remove(nodeToMove);
                        else
                            tree.Nodes.Remove(nodeToMove);

                        newParentNode.Nodes.Add(nodeToMove);
                        newParentNode.Expand();
                        tree.Enabled = true;
                        tree.SelectedNode = nodeToMove;

                        MessageBox.Show($"Parent of {childKey} changed to {newParentKey}.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            };
            treeContextMenu.Items.Add(changeParentMenuItem);
        }

        private void AddCreateIssueMenus()
        {
            // Add these inside InitializeContextMenu()
            var iconChild = CreateUnicodeIcon("🌱");
            var iconSibling = CreateUnicodeIcon("🌳");

            var addChildMenuItem = new ToolStripMenuItem("Add Child...", iconChild);
            addChildMenuItem.Click += (s, e) => ShowAddIssueDialogAsync("Child");

            var addSiblingMenuItem = new ToolStripMenuItem("Add Sibling...", iconSibling);
            addSiblingMenuItem.Click += (s, e) => ShowAddIssueDialogAsync("Sibling");

            treeContextMenu.Items.Add(addChildMenuItem);
            treeContextMenu.Items.Add(addSiblingMenuItem);
        }

        private void AddUpDownMenus()
        {
            // Add Move Up and Move Down menu items with icons
            var iconUp = CreateUnicodeIcon("🔼");
            var iconDown = CreateUnicodeIcon("🔽");

            var moveUpMenuItem = new ToolStripMenuItem("Move Up", iconUp);
            var moveDownMenuItem = new ToolStripMenuItem("Move Down", iconDown);

            moveUpMenuItem.Click += (s, e) =>
            {
                if (tree.SelectedNode != null)
                    MoveNodeInTree(tree.SelectedNode, -1); // Move up
            };

            moveDownMenuItem.Click += (s, e) =>
            {
                if (tree.SelectedNode != null)
                    MoveNodeInTree(tree.SelectedNode, 1); // Move down
            };

            treeContextMenu.Items.Add(moveUpMenuItem);
            treeContextMenu.Items.Add(moveDownMenuItem);
        }
        private async Task ShowAddIssueDialogAsync(string mode)
        {
            if (tree.SelectedNode == null) return;

            string selectedKey = "";
            string baseKey = tree.SelectedNode.Tag?.ToString();

            if (mode == "Sibling")
            {
                selectedKey = tree.SelectedNode.Parent.Tag.ToString();
            }
            else if (mode == "Child")
            {
                selectedKey = tree.SelectedNode.Tag?.ToString();
            }
            else
            {
                MessageBox.Show("Invalid mode specified for adding issue.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedKey)) return;

            // Find project config by key prefix
            var dashIndex = selectedKey.IndexOf('-');
            var keyPrefix = dashIndex > 0 ? selectedKey.Substring(0, dashIndex) : selectedKey;
            var projectConfig = config?.Projects?.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
            if (projectConfig == null)
            {
                MessageBox.Show("Project config not found for selected node.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Prepare issue types for dropdown
            var issueTypes = projectConfig.Types.Keys.ToList();

            // Create dialog
            var DialogCreateIssue = new Form
            {
                Text = $"Add {mode.ToLower()} node to {baseKey}",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                Width = 700,
                Height = 280,
                BackColor = GetCSSColor_Tree_Background(cssPath),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10)
            };

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
            if (File.Exists(iconPath))
            {
                DialogCreateIssue.Icon = new Icon(iconPath);
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10, 18, 24, 18),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

            var lblTitle = new Label
            {
                Text = $"Add {mode.ToLower()} node to: {baseKey}",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = GetCSSColor_Title(cssPath)
            };
            layout.SetColumnSpan(lblTitle, 2);
            layout.Controls.Add(lblTitle, 0, 0);

            var lblMode = new Label
            {
                Text = "Link Mode:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblMode, 0, 1);

            var cmbMode = new System.Windows.Forms.ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                Enabled = false
            };
            cmbMode.Items.AddRange(new[] { "Child", "Sibling" });
            cmbMode.SelectedItem = mode;
            layout.Controls.Add(cmbMode, 1, 1);

            var lblType = new Label
            {
                Text = "Issue Type:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblType, 0, 2);

            var cmbType = new System.Windows.Forms.ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10)
            };
            foreach (var type in issueTypes)
                cmbType.Items.Add(type);
            cmbType.SelectedIndex = 0;
            layout.Controls.Add(cmbType, 1, 2);

            var lblSummary = new Label
            {
                Text = "Summary:",
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblSummary, 0, 3);

            var txtSummary = new System.Windows.Forms.TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10),
                MaxLength = 250
            };
            layout.Controls.Add(txtSummary, 1, 3);

            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 0, 0)
            };

            var btnCreate = CreateDialogButton("Create", DialogResult.OK, true);
            var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);
            
            buttonPanel.Controls.Add(btnCreate);
            buttonPanel.Controls.Add(btnCancel);
            layout.Controls.Add(buttonPanel, 1, 4);

            DialogCreateIssue.Controls.Add(layout);
            DialogCreateIssue.AcceptButton = btnCreate;
            DialogCreateIssue.CancelButton = btnCancel;

            if (DialogCreateIssue.ShowDialog(tree) == DialogResult.OK)
            {
                string linkMode = cmbMode.SelectedItem.ToString();
                string issueType = cmbType.SelectedItem.ToString();
                string summary = txtSummary.Text.Trim();

                if (string.IsNullOrWhiteSpace(summary))
                {
                    MessageBox.Show("Summary is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Call async create method
                string? newIssueKey = await jiraService.CreateAndLinkJiraIssueAsync(
                    selectedKey,
                    linkMode,
                    issueType,
                    summary,
                    config);

                if (!string.IsNullOrWhiteSpace(newIssueKey))
                {
                    // Immediately open the created issue in the Edit dialog
                    await Task.Delay(500); // Optional: give Jira a moment to process
                    EditCurrentIssue(newIssueKey, summary);
                }

                MessageBox.Show($"New issue {newIssueKey} has been created as a {mode} of {tree.SelectedNode.Tag?.ToString()}.\nPlease update your hierarchy to show it in the tree view.", "New issue created!", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Overload for EditCurrentIssue to accept key and summary directly
        private async void EditCurrentIssue(string issueKey, string issueSummary)
        {
            if (string.IsNullOrWhiteSpace(issueKey)) return;

            string url = $"{jiraBaseUrl}/browse/{issueKey}";

            using (var DialogEditIssue = new Form())
            {
                DialogEditIssue.Text = $"Edit : {issueSummary} [{issueKey}]";
                DialogEditIssue.Width = 1200;
                DialogEditIssue.Height = 800;
                DialogEditIssue.StartPosition = FormStartPosition.CenterParent;
                DialogEditIssue.FormBorderStyle = FormBorderStyle.FixedDialog;
                DialogEditIssue.MinimizeBox = false;
                DialogEditIssue.MaximizeBox = true;

                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                if (File.Exists(iconPath))
                {
                    DialogEditIssue.Icon = new Icon(iconPath);
                }

                var webView = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill
                };

                DialogEditIssue.Controls.Add(webView);

                DialogEditIssue.Shown += async (s, e) =>
                {
                    await webView.EnsureCoreWebView2Async();
                    webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    webView.CoreWebView2.Navigate(url);
                };

                DialogEditIssue.FormClosed += async (s, e) =>
                {
                    var node = await ExpandPathToKeyAsync(issueKey); ;
                    if (node != null)
                    {
                        lastTreeMouseButton = MouseButtons.Left;
                        await Tree_AfterSelect_Internal(tree, new TreeViewEventArgs(node), true);
                    }
                    tree.Focus(); // <-- Always focus tree after dialog closes
                };

                DialogEditIssue.ShowDialog(this);
            }
        }


        /// <summary>
        /// Creates a bitmap icon from a Unicode character for use in menu items.
        /// </summary>
        /// <param name="unicodeChar">The Unicode character to render as an icon.</param>
        /// <param name="font">Optional font to use for rendering.</param>
        /// <returns>Bitmap containing the rendered icon.</returns>
        private Bitmap CreateUnicodeIcon(string unicodeChar, Font? font = null)
        {
            font ??= new Font("Segoe UI Emoji", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            var bmp = new Bitmap(24, 24); // Icon size
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.DrawString(unicodeChar, font, Brushes.Black, new PointF(2, 2));
            return bmp;
        }

        /// <summary>
        /// Displays the search dialog for the tree view, allowing users to search for issues.
        /// </summary>
        /// <param name="tree">The tree view to search within.</param>
        private void ShowSearchDialog()
        {
            if (frmSearchInstance == null || frmSearchInstance.IsDisposed)
                frmSearchInstance = new frmSearch();

            frmSearchInstance.Show(this); // Use Show, not ShowDialog, to allow hiding
            frmSearchInstance.BringToFront();
            frmSearchInstance.Focus();
            //tree.Focus();
        }

        /// <summary>
        /// Generates a hierarchical HTML report for the selected Jira issue and its children.
        /// </summary>
        private async void GenerateReport()
        {
            if (tree.SelectedNode?.Tag is string rootKey)
            {
                // Custom confirmation dialog (similar to OnFormClosing)
                var DialogReportConfirmation = new Form
                {
                    Text = $"Generate Report for {rootKey}",
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    Width = 460,
                    Height = 220,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = false,
                    Font = new Font("Segoe UI", 10),
                    BackColor = GetCSSColor_Tree_Background(cssPath),
                    Padding = new Padding(20),
                };

                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                if (File.Exists(iconPath))
                {
                    DialogReportConfirmation.Icon = new Icon(iconPath);
                }

                // Use a TableLayoutPanel for flexible layout
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    Padding = new Padding(0),
                    BackColor = GetCSSColor_Tree_Background(cssPath),
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));

                var lbl = new Label
                {
                    Text = $"This will generate a hierarchical HTML report including all the child issues of {rootKey} recursively. Are you sure you want to continue?",
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold),
                    Padding = new Padding(10),
                    MaximumSize = new Size(400, 0), // Wrap text if needed
                };

                var btnGenerate = CreateDialogButton("Generate", DialogResult.OK, true);
                var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);
                
                var buttonPanel = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0, 10, 0, 0),
                    Height = 100,
                    AutoSize = true
                };

                btnGenerate.Margin = new Padding(10, 0, 0, 0);
                btnCancel.Margin = new Padding(10, 0, 0, 0);

                buttonPanel.Controls.Add(btnGenerate);
                buttonPanel.Controls.Add(btnCancel);

                layout.Controls.Add(lbl, 0, 0);
                layout.Controls.Add(buttonPanel, 0, 1);

                DialogReportConfirmation.Controls.Add(layout);

                DialogReportConfirmation.AcceptButton = btnGenerate;
                DialogReportConfirmation.CancelButton = btnCancel;

                var result = DialogReportConfirmation.ShowDialog(this);

                if (result != DialogResult.OK)
                    return;

                // Show progress UI
                lblProgress.Text = "Generating document...";
                lblProgress.Visible = true;
                pbProgress.Visible = true;
                pbProgress.Style = ProgressBarStyle.Marquee;

                // Create the report generator and generate the report
                var generator = new JiraHtmlReportGenerator(
                    issueDict,
                    childrenByParent,
                    jiraEmail,
                    jiraToken,
                    jiraBaseUrl,
                    tree);
                var path = await generator.GenerateAsync(rootKey, new Progress<string>(t => lblProgress.Text = t));

                // Hide progress UI and open the generated report
                lblProgress.Visible = false;
                pbProgress.Visible = false;
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }

        /// <summary>
        /// Custom drawing for tab items in the details panel, including icon and close button.
        /// </summary>
        /// <param name="sender">The tab control being drawn.</param>
        /// <param name="e">Draw item event arguments.</param>
        private void TabDetails_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tabControl = sender as TabControl;
            var tabPage = tabControl.TabPages[e.Index];
            var tabRect = tabControl.GetTabRect(e.Index);

            using var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool isSelected = tabControl.SelectedIndex == e.Index;

            // Draw tab background
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

            // Draw close "X" button with rounded corners
            int closeX = tabRect.Right - closeSize - padding;
            int closeY = tabRect.Y + (tabRect.Height - closeSize) / 2;
            var closeRect = new Rectangle(closeX, closeY, closeSize, closeSize);

            using var closeBg = new SolidBrush(Color.FromArgb(220, 50, 50));
            using var closeFg = new SolidBrush(Color.White);
            using (var path = RoundedRect(closeRect, 4))
            {
                g.FillPath(closeBg, path);
            }

            using var closeFont = new System.Drawing.Font("Segoe UI", 9, FontStyle.Bold);
            var stringFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("×", closeFont, closeFg, closeRect, stringFormat);

            // Store the close rect for MouseDown event to detect tab close clicks
            tabPage.Tag = closeRect;
        }

        /// <summary>
        /// Creates a rounded rectangle graphics path for drawing UI elements.
        /// </summary>
        /// <param name="bounds">Rectangle bounds.</param>
        /// <param name="radius">Corner radius.</param>
        /// <returns>GraphicsPath representing the rounded rectangle.</returns>
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

        /// <summary>
        /// Handles mouse down events on the tab control, closes tab if close button is clicked.
        /// </summary>
        /// <param name="sender">Tab control.</param>
        /// <param name="e">Mouse event arguments.</param>
        private void tabDetails_MouseDown(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < tabDetails.TabPages.Count; i++)
            {
                var tab = tabDetails.TabPages[i];
                if (tab.Tag is Rectangle closeRect && closeRect.Contains(e.Location))
                {
                    int idx = i;
                    // If the tab to be closed is currently selected, select the tab to the left (or right if first)
                    if (tabDetails.SelectedTab == tab)
                    {
                        if (tabDetails.TabPages.Count > 1)
                        {
                            int newIdx = idx == 0 ? 0 : idx - 1;
                            tabDetails.SelectedTab = tabDetails.TabPages[newIdx == idx ? 1 : newIdx];
                        }
                    }
                    tabDetails.TabPages.Remove(tab);
                    break;
                }
            }
        }

        /// <summary>
        /// Initializes icons for issue types and statuses and assigns them to the tree view.
        /// </summary>
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

        /// <summary>
        /// Returns the icon key for a given issue type, using full or partial match.
        /// </summary>
        /// <param name="issueType">The issue type name.</param>
        /// <returns>Icon key string or empty if not found.</returns>
        public static string GetIconForType(string issueType)
        {
            if (string.IsNullOrWhiteSpace(issueType))
                return "";

            if (typeIcons.ContainsKey(issueType))
                return issueType;

            foreach (var key in typeIcons.Keys)
            {
                if (issueType.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return "";
        }

        /// <summary>
        /// Returns the icon key for a given status, using full or partial match.
        /// </summary>
        /// <param name="status">The status name.</param>
        /// <returns>Icon key string or empty if not found.</returns>
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

        /// <summary>
        /// Loads configuration from configuration.json, validates, and initializes settings.
        /// </summary>
        private async Task LoadConfigurationFromJsonAsync()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.json");

            if (!File.Exists(path))
            {
                MessageBox.Show(
                    $"Missing configuration.json file.\n\nA default file will be created at:\n{path}\n\nPlease update it with your Jira details and restart.",
                    "Configuration Missing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                File.WriteAllText(path, GetDefaultConfigJson());
                LaunchConfigForm();
                return;
            }

            try
            {
                string configText = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<JiraConfigRoot>(configText, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"The configuration file exists but contains invalid JSON.\n\nIt will now open in your default editor so you can fix it.\n\nDetails: {ex.Message}",
                    "Invalid Configuration File",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Process.Start("notepad.exe", path);
                System.Windows.Forms.Application.Exit();
                return;
            }

            // Load jira config data
            jiraBaseUrl = config.Jira.Url;
            jiraEmail = config.Jira.Email;
            jiraToken = config.Jira.Token;

            // Load other config data
            projectList = config.Projects.Select(p => p.Project).ToList();
            root_key = string.Join(",", config.Projects.Select(p => p.Root));
            hierarchyLinkTypeName = string.Join(",", config.Projects.Select(p => p.LinkTypeName));

            typeIcons = new Dictionary<string, string>();
            statusIcons = new Dictionary<string, string>();

            foreach (var project in config.Projects)
            {
                foreach (var kvp in project.Types)
                    typeIcons[kvp.Key] = kvp.Value;

                foreach (var kvp in project.Status)
                    statusIcons[kvp.Key] = kvp.Value;
            }


            if (string.IsNullOrWhiteSpace(jiraBaseUrl) || string.IsNullOrWhiteSpace(jiraEmail) || string.IsNullOrWhiteSpace(jiraToken))
            {
                MessageBox.Show(
                    "Jira URL, Email or Token is not set in the configuration.\nPlease complete the configuration.",
                    "Configuration Error!",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                LaunchConfigForm();
                return;
            }
            // Initiate and test connection to Jira
            jiraService = new JiraService(jiraBaseUrl, jiraEmail, jiraToken);

            bool isConnected = await jiraService.TestConnectionAsync();

            if (isConnected)
            {
                jiraUserName = jiraService.GetConnectedUserNameAsync().Result;
                lblUser.Text = $"    👤 Connected as :  {jiraUserName}      ";

                foreach (var project in config.Projects)
                {
                    // Extract the project key prefix from the Root property (e.g., "MON" from "MON-123")
                    string rootKeyPrefix = project.Root?.Split('-')[0];
                    if (string.IsNullOrWhiteSpace(rootKeyPrefix))
                    {
                        project.HasCreatePermission = false;
                        project.HasEditPermission = false;
                        continue;
                    }

                    var permissions = await jiraService.GetWritePermissionsAsync(rootKeyPrefix);
                    project.HasCreatePermission = permissions?.CanCreateIssues == true;
                    project.HasEditPermission = permissions?.CanEditIssues == true;
                }

            }
            else
            {
                MessageBox.Show(
                    $"Failed to connect to Jira using the provided credentials.\nPlease check and configure your settings or check if the internet connection is available.",
                    "Jira Connection Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                LaunchConfigForm();
                return;
            }
        }

        /// <summary>
        /// Returns the default configuration JSON string.
        /// </summary>
        /// <returns>Default configuration JSON.</returns>
        private string GetDefaultConfigJson()
        {
            return
        @"{
  ""Jira"": {
    ""Url"": ""https://YOUR_JIRA_DOMAIN.atlassian.net"",
    ""Email"": ""YOUR_EMAIL@YOUR_EMAIL_DOMAIN.com"",
    ""Token"": ""YOUR_JIRA_API_TOKEN""
  },
  ""Projects"": [
    {
      ""Project"": ""PROJECT1"",
      ""Root"": ""PRJ1-100"",
      ""LinkTypeName"": ""Blocks"",
      ""SortingField"": """",
      ""Types"": {
        ""Project"": ""type_project.png"",
        ""Rule"": ""type_rule.png"",
        ""User Story"": ""type_userreq.png""
      },
      ""Status"": {
        ""Draft"": ""status_draft.png"",
        ""Published"": ""status_published.png"",
        ""Rejected"": ""status_rejected.png""
      }
    }
  ]
}";
        }

        public static Color GetCSSColor_Tree_Background(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpTree\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the background-color property
                var colorMatch = Regex.Match(block, @"background-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.Window;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.Window;
        }

        public static Color GetCSSColor_Tree_SelectionBackground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpTree\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"accent-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        public static Color GetCSSColor_Tree_SelectionForeground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpTree\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"flood-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        public static Color GetCSSColor_Tree_Foreground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpTree\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        public static Color GetCSSColor_DataGrid_SelectionForeground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpDataGrid\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"flood-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        public static Color GetCSSColor_DataGrid_SelectionBackground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpDataGrid\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"accent-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }


        public static Color GetCSSColor_DataGrid_Background(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpDataGrid\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"background-color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        public static Color GetCSSColor_DataGrid_Foreground(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpDataGrid\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }
        public static Color GetCSSColor_Title(string cssFilePath)
        {
            // Read the CSS file
            string css = File.ReadAllText(cssFilePath);

            // Find the csharpTree selector block
            var match = Regex.Match(css, @"csharpTitle\s*\{([^}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success)
            {
                string block = match.Groups[1].Value;

                // Find the color property
                var colorMatch = Regex.Match(block, @"color\s*:\s*([^;]+);", RegexOptions.IgnoreCase);
                if (colorMatch.Success)
                {
                    string colorValue = colorMatch.Groups[1].Value.Trim();
                    try
                    {
                        return ColorTranslator.FromHtml(colorValue);
                    }
                    catch
                    {
                        // Fallback to default if parsing fails
                        return SystemColors.HighlightText;
                    }
                }
            }
            // Fallback to default if not found
            return SystemColors.HighlightText;
        }

        /// <summary>
        /// Handles the form load event. Initializes configuration, icons, loads all projects into the tree,
        /// and displays recently updated issues.
        /// </summary>
        /// <param name="sender">The source of the event (frmMain).</param>
        /// <param name="e">Event arguments.</param>
        private async void frmMain_Load(object sender, EventArgs e)
        {
            // Example: Set TreeView background to match CSS section background
            tree.BackColor = GetCSSColor_Tree_Background(cssPath);
            splitContainer1.BackColor = GetCSSColor_Tree_Background(cssPath);


            HTML_LOADINGPAGE = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link rel='stylesheet' href='{cssHref}' />
  <style>
    body {{
      display: flex;
      justify-content: center;
      align-items: center;
      height: 100vh;
      margin: 0;
    }}
  </style>
</head>
<body>
  <div class='spinner'></div>
</body>
</html>
";



            //Load home page
            AddHomeTabAsync(tabDetails);

            // Load configuration from file and validate
            LoadConfigurationFromJsonAsync();

            // Initialize icons for issue types and statuses
            InitializeIcons();

            // Show a tab with recently updated issuesup
            ShowRecentlyUpdatedIssuesAsync(tabDetails);

            // Load all Jira projects and their issues into the tree view
            await LoadAllProjectsToTreeAsync(false);

   
        }

        /// <summary>
        /// Loads all Jira projects and their issues into the tree view.
        /// Optionally forces a fresh sync from the server, bypassing cache.
        /// </summary>
        /// <param name="forceSync">If true, ignores cache and fetches from Jira.</param>
        private async Task LoadAllProjectsToTreeAsync(bool forceSync, string? project = null)
        {
            // --- 1. Store the currently selected node's key (if any) ---
            string? previouslySelectedKey = null;
            if (forceSync && tree.SelectedNode != null && tree.SelectedNode.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
                previouslySelectedKey = tag;

            pbProgress.Visible = true;
            pbProgress.Value = 0;
            pbProgress.Maximum = 100;
            lblProgress.Visible = true;
            lblProgress.Text = "Loading...";

            issueDict.Clear();
            childrenByParent.Clear();
            issueDtoDict.Clear();

            // 1. Sync only the requested project (if any)
            var projectsToSync = string.IsNullOrWhiteSpace(project) ? projectList : new List<string> { project };
            foreach (var proj in projectsToSync)
            {
                var projectConfig = config.Projects.FirstOrDefault(p => p.Project == proj);
                string sortingField = projectConfig?.SortingField ?? "summary";
                string linkTypeName = projectConfig?.LinkTypeName ?? "Blocks";
                var fieldsList = new List<string> { "summary", "issuetype", "issuelinks", sortingField };

                await jiraService.GetAllIssuesForProject(
                    proj,
                    fieldsList,
                    sortingField,
                    linkTypeName,
                    forceSync,
                    (completed, total, percent) =>
                    {
                        this.Invoke(() =>
                        {
                            pbProgress.Value = Math.Min(100, (int)Math.Round(percent));
                            lblProgress.Text = $"Loading project ({proj}) : {completed}/{total} ({percent:0.0}%)...";
                        });
                    }
                );
            }

            // 2. Load all cached issues for all projects (not just the one synced)
            var allIssues = new List<JiraIssue>();
            foreach (var proj in projectList)
            {
                string cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{proj}.json");
                if (File.Exists(cacheFile))
                {
                    string json = await File.ReadAllTextAsync(cacheFile);
                    var cachedIssues = JsonSerializer.Deserialize<List<JiraIssueDto>>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new List<JiraIssueDto>();

                    foreach (var myIssue in cachedIssues)
                    {
                        var issue = new JiraIssue
                        {
                            Key = myIssue.Key,
                            Summary = myIssue.Summary,
                            Type = myIssue.Type,
                            ParentKey = null,
                            RelatedIssueKeys = new List<string>(),
                            SortingField = myIssue.SortingField
                        };

                        var projectConfig = config.Projects.FirstOrDefault(p => myIssue.Key.StartsWith(p.Root.Split("-")[0]));
                        string linkTypeName = projectConfig?.LinkTypeName ?? "Blocks";

                        if (myIssue.IssueLinks != null)
                        {
                            foreach (var link in myIssue.IssueLinks)
                            {
                                if (link.LinkTypeName == linkTypeName && !string.IsNullOrEmpty(link.OutwardIssueKey))
                                {
                                    issue.RelatedIssueKeys.Add(link.OutwardIssueKey);
                                }
                            }
                        }

                        allIssues.Add(issue);
                        issueDtoDict[myIssue.Key] = myIssue;
                    }
                }
            }

            BuildDictionaries(allIssues);

            pbProgress.Visible = false;
            lblProgress.Visible = false;

            // Always build the tree for all projects, not just the synced one
            tree.Invoke(() =>
            {
                tree.Nodes.Clear();
                rootNodeList.Clear();
                var rootKeys = root_key.Split(',').Select(k => k.Trim()).ToHashSet();

                foreach (var rootIssue in issueDict.Values.Where(i => i.ParentKey == null || !issueDict.ContainsKey(i.ParentKey)))
                {
                    if (!rootKeys.Contains(rootIssue.Key)) continue;

                    var rootNode = CreateTreeNode(rootIssue);
                    if (GetChildrenForNode(rootIssue.Key).Count > 0)
                        rootNode.Nodes.Add(new TreeNode("Loading...") { Tag = "DUMMY" });
                    tree.Nodes.Add(rootNode);
                    rootNodeList.Add(rootNode);
                }

                // Expand all root nodes after loading
                foreach (var rootNode in rootNodeList)
                {
                    rootNode.Expand();
                }
            });

            // --- 2. After reload, try to restore selection ---
            if (forceSync && !string.IsNullOrWhiteSpace(previouslySelectedKey))
            {
                // Wait a moment to ensure UI is updated and nodes are available
                await Task.Delay(100);

                tree.Invoke(async () =>
                {
                    var node = await ExpandPathToKeyAsync(previouslySelectedKey);
                    if (node != null)
                    {
                        tree.SelectedNode = node;
                        node.EnsureVisible();
                        tree.Focus();
                    }
                });
            }
        }

        private async Task<TreeNode?> ExpandPathToKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var keyPrefix = key.Split('-')[0];

            // Find the root node for this key
            var rootNode = rootNodeList.FirstOrDefault(n => n.Tag?.ToString()?.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) == true);
            if (rootNode == null) return null;

            // Build the path from root to key using ParentKey chain
            var path = new Stack<string>();
            var currentKey = key;
            while (!string.IsNullOrEmpty(currentKey) && issueDict.TryGetValue(currentKey, out var issue))
            {
                path.Push(currentKey);
                currentKey = issue.ParentKey;
            }

            // If the path doesn't start with the root node's key, abort
            if (path.Count == 0 || path.Peek() != rootNode.Tag?.ToString())
                return null;

            TreeNode node = rootNode;
            path.Pop(); // Remove root node key, already at root

            while (path.Count > 0)
            {
                var nextKey = path.Pop();
                // Expand if children not loaded
                if (node.Nodes.Count == 1 && node.Nodes[0].Tag?.ToString() == "DUMMY")
                {
                    node.Expand();
                    await Task.Delay(50); // Let UI process expand
                }
                var child = node.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag?.ToString() == nextKey);
                if (child == null)
                    return null;
                node = child;
            }
            return node;
        }

        // Helper: builds all child nodes in memory, not on UI thread
        private void BuildTreeNodesInMemory(TreeNode parentNode, string parentKey, JiraProjectConfig projectConfig)
        {
            if (!childrenByParent.TryGetValue(parentKey, out var children) || children.Count == 0)
                return;

            string sortingField = projectConfig?.SortingField ?? "summary";
            var comparer = new AlphanumericComparer();

            var sortedChildren = children.OrderBy(child =>
            {
                if (issueDtoDict.TryGetValue(child.Key, out var dto))
                    return dto.SortingField ?? "";
                return child.Summary ?? "";
            }, comparer).ToList();

            foreach (var child in sortedChildren)
            {
                var childNode = CreateTreeNode(child);
                parentNode.Nodes.Add(childNode);
                BuildTreeNodesInMemory(childNode, child.Key, projectConfig);
            }
        }

        private void tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var node = e.Node;
            if (node.Nodes.Count == 1 && node.Nodes[0].Tag?.ToString() == "DUMMY")
            {
                node.Nodes.Clear();
                var children = GetChildrenForNode(node.Tag.ToString());

                // Find the project config for this node
                string key = node.Tag?.ToString();
                string keyPrefix = key?.Split('-')[0];
                var projectConfig = config.Projects.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                string sortingField = projectConfig?.SortingField ?? "summary";
                var comparer = new AlphanumericComparer();

                // Sort children using the project's sorting field
                var sortedChildren = children.OrderBy(child =>
                {
                    if (issueDtoDict.TryGetValue(child.Key, out var dto))
                        return dto.SortingField ?? "";
                    return child.Summary ?? "";
                }, comparer).ToList();

                foreach (var child in sortedChildren)
                {
                    var childNode = CreateTreeNode(child);
                    if (GetChildrenForNode(child.Key).Count > 0)
                        childNode.Nodes.Add(new TreeNode("Loading...") { Tag = "DUMMY" });
                    node.Nodes.Add(childNode);
                }
            }
        }

        private List<JiraIssue> GetChildrenForNode(string parentKey)
        {
            if (string.IsNullOrEmpty(parentKey))
                return new List<JiraIssue>();
            if (childrenByParent.TryGetValue(parentKey, out var children))
                return children;
            return new List<JiraIssue>();
        }
        /// <summary>
        /// Builds the issue lookup and parent-child hierarchy dictionaries from a list of JiraIssue objects.
        /// </summary>
        /// <param name="issues">List of JiraIssue objects to process.</param>
        private void BuildDictionaries(List<JiraIssue> issues)
        {
            issueDict.Clear();
            childrenByParent.Clear();

            // Add all issues to the lookup dictionary
            foreach (var issue in issues)
            {
                issueDict[issue.Key] = issue;
            }

            // Build parent-child relationships based on RelatedIssueKeys (hierarchy links)
            foreach (var issue in issues)
            {
                foreach (var relatedKey in issue.RelatedIssueKeys)
                {
                    if (issueDict.TryGetValue(relatedKey, out var child))
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

        /// <summary>
        /// Sets the icon for a ToolStripMenuItem based on its description text.
        /// The icon is loaded from the application's images folder using the first word of the description.
        /// Example: "Settings..." will use "images/settings.png".
        /// </summary>
        /// <param name="menuItem">The ToolStripMenuItem to set the icon for.</param>
        public void SetMenuIconFromDescription(ToolStripMenuItem menuItem)
        {
            if (menuItem == null || string.IsNullOrWhiteSpace(menuItem.Text))
                return;

            // Get the first word (before space or punctuation)
            string firstWord = menuItem.Text.Split(new[] { ' ', '.', ',', '-', '!' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (string.IsNullOrWhiteSpace(firstWord))
                return;

            string imageFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", $"{firstWord.ToLower()}.png");
            if (File.Exists(imageFile))
            {
                menuItem.Image = System.Drawing.Image.FromFile(imageFile);
            }
        }

        /// <summary>
        /// Adds a "Home" tab to the provided TabControl, displaying a welcome banner for Monovera.
        /// This tab uses a WebView2 control to render a styled HTML page with a banner image.
        /// </summary>
        /// <param name="tabDetails">The TabControl to which the home tab will be added.</param>
        /// <remarks>
        /// - Ensures the WebView2 control is initialized before navigation.
        /// - Loads a local PNG image, encodes it as base64, and embeds it in the HTML.
        /// - Optionally sets a tab icon if the image exists.
        /// - The tab is selected after creation.
        /// </remarks>
        public async Task AddHomeTabAsync(TabControl tabDetails)
        {
            // Ensure the TabControl has an ImageList for tab icons
            if (tabDetails.ImageList == null)
            {
                tabDetails.ImageList = new ImageList();
                tabDetails.ImageList.ImageSize = new Size(16, 16);
            }

            // Load the tab icon
            string iconKey = "home";
            System.Drawing.Image iconImage = null;
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "monovera.ico");

            if (File.Exists(iconFile))
            {
                try
                {
                    using var iconStream = File.OpenRead(iconFile);
                    iconImage = System.Drawing.Image.FromStream(iconStream);
                    if (!tabDetails.ImageList.Images.ContainsKey(iconKey))
                        tabDetails.ImageList.Images.Add(iconKey, iconImage);
                }
                catch
                {
                    // Ignore icon load failure
                }
            }

            // Create the TabPage and WebView2 control
            var homePage = new TabPage("Welcome to Monovera!")
            {
                ImageKey = iconKey,
                ToolTipText = "Welcome to Monovera!",
                BackColor = GetCSSColor_Tree_Background(cssPath)
            };

            var webView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                BackColor = GetCSSColor_Tree_Background(cssPath)
            };

            homePage.Controls.Add(webView);
            tabDetails.TabPages.Add(homePage);
            tabDetails.SelectedTab = homePage;

            // Start initializing WebView2
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Show loading page after WebView is ready and attached to UI
            string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
            File.WriteAllText(htmlFilePath, HTML_LOADINGPAGE);
            webView.CoreWebView2.Navigate(htmlFilePath);

            // Handle script dialogs
            webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
            {
                var deferral = args.GetDeferral();
                try { args.Accept(); } finally { deferral.Complete(); }
            };

            // Delay a bit to simulate loading or let the user see loading animation
            //await Task.Delay(800); // Adjust delay as needed

            // Prepare image and final HTML
            string imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "MonoveraBanner.png");

            if (!File.Exists(imagePath))
            {
                MessageBox.Show("Image not found: images/MonoveraBanner.png", "Missing Image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string base64 = Convert.ToBase64String(File.ReadAllBytes(imagePath));
            string imageUri = $"data:image/webp;base64,{base64}";

            string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <link rel='stylesheet' href='{cssHref}' />
  <style>
    html, body {{
      margin: 0;
      padding: 0;
      width: 100%;
      height: 100%;
      display: flex;
      justify-content: center;
      align-items: center;
      background-color: white;
    }}
    img {{
      max-width: 100%;
      max-height: 100%;
      height: auto;
      width: auto;
    }}
  </style>
</head>
<body>
  <img src=""{imageUri}"" alt=""Monovera"" />
</body>
</html>";


            // Finally navigate to the final HTML
            webView.NavigateToString(html);
        }



        /// <summary>
        /// Displays a tab with a list of recently updated Jira issues for all configured projects.
        /// Issues are grouped by update date and filtered to show only those with summary or description changes.
        /// The tab uses a WebView2 control to render a styled HTML report, with clickable links to load issue details.
        /// </summary>
        /// <param name="tabDetails">The TabControl to which the "Recent Updates" tab will be added.</param>
        public async Task ShowRecentlyUpdatedIssuesAsync(TabControl tabDetails, int days = 14)
        {
            // --- Step 1: Prepare WebView2 control and TabPage first ---
            var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            webView.BackColor = GetCSSColor_Tree_Background(cssPath);

            var updatePage = new TabPage("Recent Updates!")
            {
                ImageKey = "updates",
                ToolTipText = $"Issues that were updated during past {days} days!",
                BackColor = GetCSSColor_Tree_Background(cssPath)
            };
            updatePage.Controls.Add(webView);

            tabDetails.TabPages.Add(updatePage);
            tabDetails.SelectedTab = updatePage;

            if (tabDetails.ImageList == null)
                tabDetails.ImageList = new ImageList { ImageSize = new Size(16, 16) };

            string iconKey = "updates";
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "monovera.png");
            if (File.Exists(iconFile))
            {
                try
                {
                    using var iconStream = File.OpenRead(iconFile);
                    var iconImage = System.Drawing.Image.FromStream(iconStream);
                    if (!tabDetails.ImageList.Images.ContainsKey(iconKey))
                        tabDetails.ImageList.Images.Add(iconKey, iconImage);
                }
                catch { }
            }

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
            File.WriteAllText(htmlFilePath, HTML_LOADINGPAGE);
            webView.CoreWebView2.Navigate(htmlFilePath);

            webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
            {
                var deferral = args.GetDeferral();
                try { args.Accept(); } finally { deferral.Complete(); }
            };

            webView.CoreWebView2.WebMessageReceived += (s, args) =>
            {
                try
                {
                    string message = args.TryGetWebMessageAsString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                        SelectAndLoadTreeNode(message);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("WebMessageReceived error: " + ex.Message);
                }
            };

            await Task.Delay(800);

            // --- Step 3: Build the JQL and get issues ---
            string jql = $"({string.Join(" OR ", projectList.Select(p => $"project = \"{p}\""))}) AND (created >= -{days}d OR updated >= -{days}d) ORDER BY updated DESC";
            var rawIssues = await frmSearch.SearchJiraIssues(jql, null);

            var tasks = rawIssues.Select(async issue =>
            {
                var url = $"{jiraBaseUrl}/rest/api/3/issue/{issue.Key}?expand=changelog";
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}")));

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                DateTime? createdDate = null;
                var changeTags = new List<string>();

                if (doc.RootElement.TryGetProperty("changelog", out var changelog) &&
                    changelog.TryGetProperty("histories", out var histories))
                {
                    if (doc.RootElement.TryGetProperty("fields", out var fields) &&
                        fields.TryGetProperty("created", out var createdProp) &&
                        createdProp.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(createdProp.GetString(), out var dt))
                            createdDate = dt;
                    }

                    issue.Created = createdDate;

                    foreach (var history in histories.EnumerateArray())
                    {
                        if (history.TryGetProperty("created", out var createdDateOfHistory) &&
                            DateTime.TryParse(createdDateOfHistory.GetString(), out var changeDate) &&
                            (changeDate.Date == issue.Updated?.ToLocalTime().Date))
                        {
                            if (issue.Created.HasValue && issue.Created.Value.Date == changeDate.Date)
                                changeTags.Add("Created");

                            if (history.TryGetProperty("items", out var items))
                            {
                                foreach (var item in items.EnumerateArray())
                                {
                                    if (item.TryGetProperty("field", out var fieldName))
                                    {
                                        string field = fieldName.GetString();
                                        if (field.ToLower().Contains("issue sequence"))
                                            changeTags.Add("order");
                                        else if (field.ToLower().Contains("issuetype"))
                                            changeTags.Add("type");
                                        else
                                            changeTags.Add(field);
                                    }
                                }
                            }
                        }
                    }
                }

                changeTags.Sort();
                if (changeTags.Count > 0)
                    issue.CustomFields["ChangeTypeTags"] = changeTags.Distinct().ToList();

                return issue;
            });
            var withChanges = await Task.WhenAll(tasks);
            var filteredIssues = withChanges.Where(i => i != null).ToList();

            IEnumerable<IGrouping<DateTime, JiraIssueDto>> grouped;
            try
            {
                grouped = filteredIssues
                    .Where(i => i.Updated.HasValue)
                    .GroupBy(i => i.Updated.Value.ToLocalTime().Date)
                    .OrderByDescending(g => g.Key);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Grouping failed: " + ex.Message);
                return;
            }

            if (!grouped.Any())
            {
                MessageBox.Show("No recently updated issues were found.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- Step 4: Generate the HTML content ---
            var sb = new StringBuilder();

            // 1. Collect all unique issue types and change types across all groups
            var allIssueTypesGlobal = grouped
                .SelectMany(g => g.Select(issue => issue.Type ?? ""))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var allChangeTypesGlobal = grouped
                .SelectMany(g => g
                    .SelectMany(issue =>
                        (issue.CustomFields.TryGetValue("ChangeTypeTags", out var tagsObj) && tagsObj is List<string> tagsList)
                            ? tagsList
                            : new List<string>()))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // 2. Render filter panels with scrollable overflow
            sb.AppendLine($@"
<!-- Show Panel Button -->
<button id='show-filter-btn'>Apply Filter</button>

<!-- Floating Panel -->
<div id='floating-filter-container'>
  <div class='filter-panel'>
    <div class='filter-panel-title'>Issue Types</div>
    <div id='issue-type-checkboxes' class='checkbox-container'>
      <label><input type='checkbox' class='issue-type-checkbox-all change-type-checkbox-all' checked /> <span style='margin-left:6px;'>All</span></label>
      {string.Join("\n", allIssueTypesGlobal.Select(t =>
         $"<label style='display:inline-flex;align-items:center;'><input type='checkbox' class='issue-type-checkbox change-type-checkbox' value='{HttpUtility.HtmlEncode(t)}' checked /> <span style='margin-left:6px;'>{HttpUtility.HtmlEncode(t)}</span></label>"))}
    </div>
  </div>

  <div class='filter-panel'>
    <div class='filter-panel-title'>Change Types</div>
    <div id='change-type-checkboxes' class='checkbox-container'>
      <label><input type='checkbox' class='change-type-checkbox-all' checked /> <span style='margin-left:6px;'>All</span></label>
      {string.Join("\n", allChangeTypesGlobal.Select(t =>
         $"<label style='display:inline-flex;align-items:center;'><input type='checkbox' class='change-type-checkbox' value='{HttpUtility.HtmlEncode(t)}' checked /> <span style='margin-left:6px;'>{HttpUtility.HtmlEncode(t)}</span></label>"))}
    </div>
  </div>
</div>

<script>
  const panel = document.getElementById('floating-filter-container');
  const showBtn = document.getElementById('show-filter-btn');

  let panelVisible = false;

  function showPanel() {{
    panel.style.top = '0px';
    panelVisible = true;
  }}

  function hidePanel() {{
    panel.style.top = '-300px';
    panelVisible = false;
  }}

  showBtn.addEventListener('click', () => {{
    if (panelVisible) {{
      hidePanel();
    }} else {{
      showPanel();
    }}
  }});

  // Hide on outside click
  document.addEventListener('click', (event) => {{
    const isClickInside = panel.contains(event.target) || showBtn.contains(event.target);
    if (!isClickInside) {{
      hidePanel();
    }}
  }});
</script>
");


            foreach (var group in grouped)
            {
                sb.AppendLine($@"
<details open>
  <summary>{group.Key:yyyy-MM-dd} ({group.Count()} issues)</summary>
  <section>
    <div class='subsection'>
      <table class='confluenceTable' style='width:100%;border-collapse:collapse;'>
        <thead>
          <tr>
            <th class='confluenceTh' style='width:36px;'>Type</th>
            <th class='confluenceTh'>Summary</th>
            <th class='confluenceTh'>Changes</th>
            <th class='confluenceTh'style='width:100px;'>Updated</th>
          </tr>
        </thead>
        <tbody>");


                foreach (var issue in group)
                {
                    string summary = HttpUtility.HtmlEncode(issue.Summary ?? "");
                    string key = issue.Key;
                    string type = HttpUtility.HtmlEncode(issue.Type ?? "");
                    string updated = issue.Updated?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
                    string status = issue.CustomFields.TryGetValue("status", out var statusObj) ? HttpUtility.HtmlEncode(statusObj?.ToString() ?? "") : "";

                    List<string> changeTags = new();
                    if (issue.CustomFields.TryGetValue("ChangeTypeTags", out var tagsObj) && tagsObj is List<string> tagsList)
                    {
                        foreach (var tag in tagsList)
                        {
                            if (!string.IsNullOrWhiteSpace(tag))
                                changeTags.Add($"<span class='recent-update-tag' data-changetype='{HttpUtility.HtmlEncode(tag)}'>{HttpUtility.HtmlEncode(tag)}</span>");
                        }
                    }
                    else if (issue.CustomFields.TryGetValue("ChangeTypeTag", out var tagObj) && tagObj is string tagStr && !string.IsNullOrWhiteSpace(tagStr))
                    {
                        changeTags.Add($"<span class='recent-update-tag' data-changetype='{HttpUtility.HtmlEncode(tagStr)}'>{HttpUtility.HtmlEncode(tagStr)}</span>");
                    }
                    string changeTagsHtml = changeTags.Count > 0
                        ? $"<div class='recent-update-tags'>{string.Join(" ", changeTags)}</div>"
                        : "";

                    string typeIconKey = frmMain.GetIconForType(issue.Type);
                    string iconImgInner = "";
                    if (!string.IsNullOrEmpty(typeIconKey) && typeIcons.TryGetValue(typeIconKey, out var fileName))
                    {
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(fullPath);
                                string base64 = Convert.ToBase64String(bytes);
                                iconImgInner = $"<img src='data:image/png;base64,{base64}' style='height:24px;width:24px;vertical-align:middle;margin-right:8px;border-radius:4px;' title='{type}' />";
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        iconImgInner = $"<span style='font-size:22px; vertical-align:middle; margin-right:8px;' title='{type}'>🟥</span>";
                    }

                    var changeTypeList = changeTags
                        .Select(tag => Regex.Match(tag, @"data-changetype='([^']+)'").Groups[1].Value)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    string changeTypeAttr = changeTypeList.Count > 0
                        ? $"data-changetypes='{string.Join(",", changeTypeList.Select(HttpUtility.HtmlEncode))}'"
                        : "";
                    string issueTypeAttr = $"data-issuetype='{type}'";

                    // --- THIS IS THE KEY CHANGE: icon is now a hyperlink, just like in link tables ---
                    sb.AppendLine($@"
<tr {changeTypeAttr} {issueTypeAttr}>
  <td class='confluenceTd'>
    <a href='#' data-key='{key}' class='recent-update-icon-link'>{iconImgInner}</a>
  </td>
  <td class='confluenceTd'>
    <a href='#' data-key='{key}' class='recent-update-summary'>{summary} [{key}]</a>
  </td>
  <td class='confluenceTd'>{changeTagsHtml}</td>
  <td class='confluenceTd'>{updated}</td>
</tr>");
                }
                sb.AppendLine(@"
        </tbody>
      </table>
    </div>
  </section>
</details>");
            }

            sb.AppendLine(@"
<script>
function applyGlobalFilter() {
    var checkedIssueTypes = Array.from(document.querySelectorAll('#issue-type-checkboxes .change-type-checkbox'))
        .filter(x => x.checked)
        .map(x => x.value);
    var checkedChangeTypes = Array.from(document.querySelectorAll('#change-type-checkboxes .change-type-checkbox'))
        .filter(x => x.checked)
        .map(x => x.value);

    document.querySelectorAll('table.confluenceTable tbody tr').forEach(function(row) {
        var rowIssueType = row.getAttribute('data-issuetype') || '';
        var rowChangeTypes = (row.getAttribute('data-changetypes') || '').split(',');
        var show = true;
        if (checkedIssueTypes.length > 0 && !checkedIssueTypes.includes(rowIssueType)) show = false;
        if (checkedChangeTypes.length > 0 && !rowChangeTypes.some(t => checkedChangeTypes.includes(t))) show = false;
        row.style.display = show ? '' : 'none';
    });
}

document.querySelectorAll('.filter-panel').forEach(panel => {
    const allCheckbox = panel.querySelector('.change-type-checkbox-all');
    const checkboxes = panel.querySelectorAll('.change-type-checkbox');

    if (!allCheckbox) return;

    allCheckbox.addEventListener('change', function () {
        const checked = this.checked;
        checkboxes.forEach(cb => cb.checked = checked);
        applyGlobalFilter();
    });

    checkboxes.forEach(cb => {
        cb.addEventListener('change', function () {
            const allChecked = Array.from(checkboxes).every(x => x.checked);
            allCheckbox.checked = allChecked;
            applyGlobalFilter();
        });
    });
});

window.addEventListener('DOMContentLoaded', applyGlobalFilter);
</script>
");

            string html = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet'>
  <link rel='stylesheet' href='{cssHref}' />
</head>
<body>
{sb}
<script>
  document.querySelectorAll('a.recent-update-summary[data-key]').forEach(link => {{
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

            string tempFilePath = Path.Combine(tempFolder, "monovera_updated.html");
            File.WriteAllText(tempFilePath, html);
            webView.CoreWebView2.Navigate(tempFilePath);
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

        private void tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (tree.SelectedNode == e.Node)
            {
                Tree_AfterSelect(sender, new TreeViewEventArgs(e.Node));
            }
        }

        private async void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (suppressAfterSelect)
                return;

            if (lastTreeMouseButton == MouseButtons.Right)
                return;

            if (e.Node?.Tag is not string issueKey || string.IsNullOrWhiteSpace(issueKey))
                return;

            suppressTabSelection = true;
            try
            {
                // Call Tree_AfterSelect_Internal directly on UI thread
                await Tree_AfterSelect_Internal(sender, e, false);

                // UI update: select the tab for this issue
                foreach (TabPage page in tabDetails.TabPages)
                {
                    if (page.Text == issueKey)
                    {
                        tabDetails.SelectedTab = page;
                        tree.Focus();
                        break;
                    }
                }
            }
            finally
            {
                suppressTabSelection = false;
            }
        }

        private async Task Tree_AfterSelect_Internal(object sender, TreeViewEventArgs e, bool forcedReload)
        {
            if (suppressAfterSelect)
                return;

            // Prevent tab loading on right-click selection
            if (lastTreeMouseButton == MouseButtons.Right)
                return;

            if (e.Node?.Tag is not string issueKey || string.IsNullOrWhiteSpace(issueKey))
                return;

            // --- IMMEDIATE SELECTION AND FOCUS ---
            tree.SelectedNode = e.Node;
            e.Node.EnsureVisible();
            tree.Focus();

            bool isCtrlPressed = (Control.ModifierKeys & Keys.Control) == Keys.Control;
            bool isLeftClick = lastTreeMouseButton == MouseButtons.Left;

            string iconUrl = null;
            if (tree.ImageList != null && e.Node.ImageKey != null && tree.ImageList.Images.ContainsKey(e.Node.ImageKey))
            {
                using var ms = new MemoryStream();
                tree.ImageList.Images[e.Node.ImageKey].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                string base64 = Convert.ToBase64String(ms.ToArray());
                iconUrl = $"data:image/png;base64,{base64}";
            }

            TabPage pageTab = tabDetails.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == issueKey);

            Microsoft.Web.WebView2.WinForms.WebView2 webView = null;

            if (pageTab == null)
            {
                // Tab does not exist, create and load it
                webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived; // <-- Add this line


                // Show loading page immediately
                string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
                File.WriteAllText(htmlFilePath, HTML_LOADINGPAGE);
                webView.CoreWebView2.Navigate(htmlFilePath);

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

                pageTab = new TabPage(issueKey)
                {
                    ImageKey = issueKey,
                    ToolTipText = $"{e.Node.Text}"
                };
                pageTab.Controls.Add(webView);
                tabDetails.TabPages.Add(pageTab);
                tabDetails.SelectedTab = pageTab;
            }
            else
            {
                tabDetails.SelectedTab = pageTab;
                // Always reload if Ctrl+LeftClick, even if tab is already focused
                if ((isLeftClick && isCtrlPressed) || forcedReload)
                {
                    webView = pageTab.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().FirstOrDefault();
                    if (webView == null)
                    {
                        webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };

                        await webView.EnsureCoreWebView2Async();
                        webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                        pageTab.Controls.Clear();
                        pageTab.Controls.Add(webView);
                    }
                    string htmlFilePath = Path.Combine(tempFolder, $"HTML_LOADINGPAGE.html");
                    File.WriteAllText(htmlFilePath, HTML_LOADINGPAGE);
                    webView.CoreWebView2.Navigate(htmlFilePath);
                }
                else
                {
                    // Just focus the tab, do not reload
                    return;
                }
            }

            // Load content asynchronously, do not block UI
            _ = Task.Run(async () =>
            {
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

                    string createdDate = fields.TryGetProperty("created", out var issueCreatedProp)
                                    ? DateTime.TryParse(issueCreatedProp.GetString(), out var IssueCreatedDt)
                                        ? IssueCreatedDt.ToString("yyyy-MM-dd HH:mm")
                                        : issueCreatedProp.GetString()
                                    : "N/A";

                    string statusIcon = "";
                    string iconKeyStatus = GetIconForStatus(status);
                    if (!string.IsNullOrEmpty(iconKeyStatus) && tree.ImageList.Images.ContainsKey(iconKeyStatus))
                    {
                        using var ms = new MemoryStream();
                        tree.ImageList.Images[iconKeyStatus].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        string base64 = Convert.ToBase64String(ms.ToArray());
                        statusIcon = $"<img src='data:image/png;base64,{base64}' style='height: 18px; vertical-align: middle; margin-right: 6px;'>";
                    }

                    string issueUrl = $"{jiraBaseUrl}/browse/{issueKey}";

                    string issueType = fields.TryGetProperty("issuetype", out var typeProp) &&
                       typeProp.TryGetProperty("name", out var typeName)
                       ? typeName.GetString() ?? ""
                       : "";

                    string encodedSummary = WebUtility.HtmlEncode(summary);
                    string iconImg = string.IsNullOrEmpty(iconUrl) ? "" : $"<img src='{iconUrl}' style='height: 24px; vertical-align: middle; margin-right: 8px;'>";
                    string headerLine = $"<h2>{iconImg}{encodedSummary} [{issueKey}]</h2>";

                    string HTML_SECTION_ATTACHMENTS = BuildHTMLSection_ATTACHMENTS(fields, issueKey);

                    string HTML_SECTION_DESCRIPTION_ORIGINAL = "";
                    if (root.TryGetProperty("renderedFields", out var renderedFields) &&
                        renderedFields.TryGetProperty("description", out var descProp) &&
                        descProp.ValueKind == JsonValueKind.String)
                    {
                        HTML_SECTION_DESCRIPTION_ORIGINAL = descProp.GetString() ?? "";
                    }
                    var HTML_SECTION_DESCRIPTION = BuildHTMLSection_DESCRIPTION(HTML_SECTION_DESCRIPTION_ORIGINAL, issueKey);

                    string HTML_SECTION_LINKS =
                                    BuildHTMLSection_LINKS(fields, "Children", hierarchyLinkTypeName.Split(",")[0].ToString(), "outwardIssue") +
                                    BuildHTMLSection_LINKS(fields, "Parent", hierarchyLinkTypeName.Split(",")[0].ToString(), "inwardIssue") +
                                    BuildHTMLSection_LINKS(fields, "Related", "Relates", null);

                    string HTML_SECTION_HISTORY = BuildHTMLSection_HISTORY(root);

                    string responseHTML = WebUtility.HtmlEncode(FormatJson(json));

                    string tempFolderPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "temp");
                    string htmlFilePath2 = Path.Combine(tempFolderPath, $"{issueKey}.html");
                    Directory.CreateDirectory(tempFolderPath);

                    string html = BuildIssueDetailFullPageHtml(
                        headerLine, issueType, statusIcon, status,
                        createdDate, lastUpdated, issueUrl,
                        HTML_SECTION_DESCRIPTION, HTML_SECTION_ATTACHMENTS,
                        HTML_SECTION_LINKS, HTML_SECTION_HISTORY, responseHTML
                    );

                    // Ensure focustToTreeJS is present in the HTML
                    if (!html.Contains(focustToTreeJS))
                        html = html.Replace("</body>", $"{focustToTreeJS}</body>");

                    File.WriteAllText(htmlFilePath2, html);

                    // Update WebView2 on UI thread
                    webView.Invoke(() =>
                    {
                        webView.Source = new Uri(htmlFilePath2);
                    });
                }
                catch (Exception ex)
                {
                    webView.Invoke(() =>
                    {
                        MessageBox.Show($"Could not connect to fetch the information you requested.\nPlease check your connection and other settings are ok.\n{ex.Message}", "Could not connect!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
        }

        private string BuildHTMLSection_ATTACHMENTS(JsonElement fields, string issueKey)
        {
            if (!fields.TryGetProperty("attachment", out var attachments) || attachments.ValueKind != JsonValueKind.Array || attachments.GetArrayLength() == 0)
                return "<details open>\r\n  <summary>Attachments</summary>\r\n  <section><div class='no-attachments'>No attachments found.</div></section>\r\n</details>";

            var sb = new StringBuilder();
            sb.AppendLine(@"
<details open>
  <summary>Attachments</summary>
  <section>
    <div class='attachments-strip-wrapper'>
      <button class='scroll-btn left' onclick='scrollAttachments(-1)' aria-label='Scroll left'>&#8592;</button>
      <div class='attachments-strip' id='attachmentsStrip'>
");

            foreach (var att in attachments.EnumerateArray())
            {
                string filename = att.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "";
                string mimeType = att.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "" : "";
                string created = att.TryGetProperty("created", out var cr) ? cr.GetString() ?? "" : "";
                string author = att.TryGetProperty("author", out var au) && au.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
                string size = att.TryGetProperty("size", out var sz) ? sz.GetInt64().ToString("N0") : "";
                string contentUrl = att.TryGetProperty("content", out var cu) ? cu.GetString() ?? "" : "";

                // Try to inline preview for images
                string previewHtml = "";
                if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                        using var client = new HttpClient();
                        client.BaseAddress = new Uri(jiraBaseUrl);
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                        using var response = client.GetAsync(contentUrl).Result;
                        response.EnsureSuccessStatusCode();
                        var imageBytes = response.Content.ReadAsByteArrayAsync().Result;
                        string base64 = Convert.ToBase64String(imageBytes);
                        previewHtml = $@"
<a href='#' class='preview-image' data-src='data:{mimeType};base64,{base64}'>
  <img src='data:{mimeType};base64,{base64}' class='attachment-img' alt='{WebUtility.HtmlEncode(filename)}' />
</a>";
                    }
                    catch
                    {
                        previewHtml = "<div style='color:red;'>Image preview failed</div>";
                    }
                }

                sb.AppendLine($@"
<div class='attachment-card'>
  {previewHtml}
  <div class='attachment-filename'>{WebUtility.HtmlEncode(filename)}</div>
  <div class='attachment-meta'>
    <span>Type: {WebUtility.HtmlEncode(mimeType)}</span><br/>
    <span>By: {WebUtility.HtmlEncode(author)}</span><br/>
    <span>Size: {size} bytes</span><br/>
    <span>Created: {WebUtility.HtmlEncode(created)}</span>
  </div>
  <a href='#' class='download-btn' data-filepath='{contentUrl}'>Download</a>
</div>");
            }

            sb.AppendLine(@"
      </div>
      <button class='scroll-btn right' onclick='scrollAttachments(1)' aria-label='Scroll right'>&#8594;</button>
    </div>
    <!-- Lightbox for image preview -->
    <div id='attachmentLightbox' class='attachment-lightbox' style='display:none;' onclick='closeAttachmentLightbox()'>
      <img id='lightboxImg' src='' alt='Preview' />
    </div>
  </section>
</details>
<script>
  function scrollAttachments(direction) {
    var strip = document.getElementById('attachmentsStrip');
    if (strip) {
      strip.scrollLeft += direction * 220; // Adjust scroll amount as needed
    }
  }
  document.querySelectorAll('.preview-image').forEach(link => {
    link.addEventListener('click', function(e) {
      e.preventDefault();
      var src = link.getAttribute('data-src');
      var lightbox = document.getElementById('attachmentLightbox');
      var img = document.getElementById('lightboxImg');
      img.src = src;
      lightbox.style.display = 'flex';
    });
  });
  function closeAttachmentLightbox() {
    document.getElementById('attachmentLightbox').style.display = 'none';
    document.getElementById('lightboxImg').src = '';
  }
</script>
");

            return sb.ToString();
        }

        private string BuildHTMLSection_LINKS(JsonElement fields, string title, string linkType, string prop)
        {
            var sb = new StringBuilder();
            int matchCount = 0;
            // Now include sortingField in the tuple
            var issues = new List<(string key, string summary, string issueType, string sortingValue, JsonElement issueElem)>();

            if (fields.TryGetProperty("issuelinks", out var links))
            {
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
                            var issueType = issueElem.GetProperty("fields").TryGetProperty("issuetype", out var typeField) && typeField.TryGetProperty("name", out var typeName)
                                ? typeName.GetString() ?? ""
                                : "";

                            // Get sorting field value for this key from issueDtoDict
                            string sortingValue = "";
                            if (issueDtoDict.TryGetValue(key, out var dto))
                            {
                                sortingValue = dto.SortingField ?? "";
                            }

                            issues.Add((key, sum, issueType, sortingValue, issueElem));
                        }
                    }
                }

                // Sort issues by sortingValue (from issueDtoDict)
                if (issues.Count > 0)
                {
                    var comparer = new AlphanumericComparer();
                    issues = issues.OrderBy(i => i.sortingValue ?? i.summary ?? "", comparer).ToList();
                }

                var tableRows = new StringBuilder();
                foreach (var i in issues)
                {
                    // Find project config for this key
                    var keyPrefix = i.key.Split('-')[0];
                    var projectConfig = config.Projects.FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
                    string iconImgInner = "";

                    // Case-insensitive lookup for issueType in projectConfig.Types
                    string fileName = null;
                    if (projectConfig != null && !string.IsNullOrEmpty(i.issueType))
                    {
                        // Try direct match first
                        if (!projectConfig.Types.TryGetValue(i.issueType, out fileName))
                        {
                            // Fallback: case-insensitive search
                            var match = projectConfig.Types
                                .FirstOrDefault(kvp => kvp.Key.Equals(i.issueType, StringComparison.OrdinalIgnoreCase));
                            fileName = match.Value;
                        }
                    }

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(fullPath);
                                string base64 = Convert.ToBase64String(bytes);
                                iconImgInner = $"<img src='data:image/png;base64,{base64}' style='height:24px; width:24px; vertical-align:middle; margin-right:8px; border-radius:4px;' title='{HttpUtility.HtmlEncode(i.issueType)}' />";
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        iconImgInner = $"<span style='font-size:22px; vertical-align:middle; margin-right:8px;' title='{HttpUtility.HtmlEncode(i.issueType)}'>🟥</span>";
                    }

                    tableRows.AppendLine($@"
<tr>
    <td class='confluenceTd'>
        <a href='#' data-key='{HttpUtility.HtmlEncode(i.key)}'>
            {iconImgInner} {HttpUtility.HtmlEncode(i.summary)} [{HttpUtility.HtmlEncode(i.key)}]
        </a>
    </td>
</tr>");
                    matchCount++;
                }

                if (matchCount > 0)
                {
                    sb.AppendLine($@"
<table class='confluenceTable' style='width:100%; border-collapse:collapse; margin-bottom:10px;'>
  <thead>
    <tr>
      <th class='confluenceTh' style='width:60px;'>{title}</th>
    </tr>
  </thead>
  <tbody>");
                    sb.Append(tableRows);
                    sb.AppendLine("</tbody></table>");
                }
                else
                {
                    sb.AppendLine($@"
<table class='confluenceTable' style='width:100%; border-collapse:collapse; margin-bottom:10px;'>
  <thead>
    <tr>
      <th class='confluenceTh' style='width:60px;'>{HttpUtility.HtmlEncode(title)}</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td class='confluenceTd' style='text-align:left; color:#888;'>{HttpUtility.HtmlEncode($"No {title} issues found.")}</td>
    </tr>
  </tbody>
</table>");
                }
            }
            else
            {
                sb.AppendLine($@"
<table class='confluenceTable' style='width:100%; border-collapse:collapse; margin-bottom:10px;'>
  <thead>
    <tr>
      <th class='confluenceTh' style='width:60px;'>{HttpUtility.HtmlEncode(title)}</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td class='confluenceTd' style='text-align:left; color:#888;'>{HttpUtility.HtmlEncode($"No {title} issues found.")}</td>
    </tr>
  </tbody>
</table>");
            }

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        public static string BuildHTMLSection_HISTORY(JsonElement root)
        {
            if (!root.TryGetProperty("changelog", out var changelog) ||
                !changelog.TryGetProperty("histories", out var histories))
                return "";

            var changes = new List<string>();
            int changeId = 0;

            foreach (var h in histories.EnumerateArray())
            {
                var createdRaw = h.GetProperty("created").GetString();
                if (!DateTime.TryParse(createdRaw, out var created))
                    continue;

                var createdStr = created.ToString("yyyy-MM-dd");
                var timeStr = created.ToString("HH:mm");

                var author = h.TryGetProperty("author", out var authorProp) &&
                             authorProp.TryGetProperty("displayName", out var displayNameProp)
                             ? displayNameProp.GetString() ?? ""
                             : "";

                foreach (var item in h.GetProperty("items").EnumerateArray())
                {
                    var field = item.GetProperty("field").GetString();
                    var from = item.TryGetProperty("fromString", out var fromVal) ? fromVal.GetString() ?? "null" : "null";
                    var to = item.TryGetProperty("toString", out var toVal) ? toVal.GetString() ?? "null" : "null";

                    string fieldIcon = field?.ToLower() switch
                    {
                        "status" => "🟢",
                        "assignee" => "👤",
                        "priority" => "⚡",
                        "summary" => "📝",
                        "description" => "📄",
                        _ => "🔧"
                    };

                    string fromEsc = HttpUtility.JavaScriptStringEncode(from);
                    string toEsc = HttpUtility.JavaScriptStringEncode(to);

                    changes.Add($@"
<tr data-date='{createdStr}' data-user='{HttpUtility.HtmlEncode(author)}' data-field='{HttpUtility.HtmlEncode(field)}'>
    <td class='confluenceTd'><input type='checkbox' class='compare-check' value='{changeId}' data-from='{fromEsc}' data-to='{toEsc}' data-field='{HttpUtility.HtmlEncode(field)}'></td>
    <td class='confluenceTd'>{createdStr}<br><small>{timeStr}</small></td>
    <td class='confluenceTd'>{HttpUtility.HtmlEncode(author)}</td>
    <td class='confluenceTd'>{fieldIcon} <strong>{HttpUtility.HtmlEncode(field)}</strong></td>
    <td class='confluenceTd'>{HttpUtility.HtmlEncode(from)}</td>
    <td class='confluenceTd'>{HttpUtility.HtmlEncode(to)}</td>
</tr>");
                    changeId++;
                }
            }

            var sb = new StringBuilder();

            sb.AppendLine(@"
<div class='filter-section' style='margin-bottom:18px;display:flex;gap:18px;align-items:center;flex-wrap:wrap;'>
    <label style='font-weight:500;color:#1565c0;'>Date:
        <select id='filterDate' class='issue-type-dropdown' style='padding:6px 12px;min-width:120px;margin-left:8px;'>
            <option value=''>-- All Dates --</option>
        </select>
    </label>
    <label style='font-weight:500;color:#1565c0;'>User:
        <select id='filterUser' class='issue-type-dropdown' style='padding:6px 12px;min-width:120px;margin-left:8px;'>
            <option value=''>-- All Users --</option>
        </select>
    </label>
    <label style='font-weight:500;color:#1565c0;'>Type:
        <select id='filterField' class='issue-type-dropdown' style='padding:6px 12px;min-width:120px;margin-left:8px;'>
            <option value=''>-- All Types --</option>
        </select>
    </label>
    <button onclick='viewSelectedDiff()' class='download-btn' style='margin-left:18px;font-size:1em;display:flex;align-items:center;gap:6px;'>
        <span style='font-size:1.2em;'>🔍</span> Show Difference
    </button>
</div>

<div class='table-wrap'>
    <table id='historyTable' class='confluenceTable'>
        <thead>
            <tr>
                <th class='confluenceTh'></th>
                <th class='confluenceTh'>Date</th>
                <th class='confluenceTh'>User</th>
                <th class='confluenceTh'>Type</th>
                <th class='confluenceTh'>Before</th>
                <th class='confluenceTh'>After</th>
            </tr>
        </thead>
        <tbody>
            " + string.Join("\n", changes) + @"
        </tbody>
    </table>
</div>
<div class='diff-overlay' id='diffOverlay' style='display:none'>
    <div class='diff-overlay-header'>
        <h3 id='diffTitle'></h3>
        <div class='diff-close' onclick=""document.getElementById('diffOverlay').style.display='none'"">✖</div>
    </div>
    <div class='diff-columns'>
        <div>
            <h4>Older Version</h4>
            <div id='diffFrom'></div>
        </div>
        <div>
            <h4>New Version</h4>
            <div id='diffTo'></div>
        </div>
    </div>
</div>

<!-- Custom alert modal -->
<div id='customAlert' class='custom-alert' style='display:none;'>
  <div class='custom-alert-content'>
    <span id='customAlertMessage'></span>
    <button onclick='closeCustomAlert()'>OK</button>
  </div>
</div>

<script>
function escapeHtml(text) {
    return text.replace(/&/g, '&amp;')
               .replace(/</g, '&lt;')
               .replace(/>/g, '&gt;')
               .replace(/""/g, '&quot;')
               .replace(/'/g, '&#039;');
}

function simpleDiffHtml(oldText, newText) {
    const oldWords = oldText.split(/(\s+|\b)/);  // split by words and spaces
    const newWords = newText.split(/(\s+|\b)/);

    const dp = Array(oldWords.length + 1).fill(null).map(() =>
        Array(newWords.length + 1).fill(0));

    for (let i = 1; i <= oldWords.length; i++) {
        for (let j = 1; j <= newWords.length; j++) {
            if (oldWords[i - 1] === newWords[j - 1]) {
                dp[i][j] = dp[i - 1][j - 1] + 1;
            } else {
                dp[i][j] = Math.max(dp[i - 1][j], dp[i][j - 1]);
            }
        }
    }

    let i = oldWords.length, j = newWords.length;
    const fromDiff = [], toDiff = [];

    while (i > 0 && j > 0) {
        if (oldWords[i - 1] === newWords[j - 1]) {
            fromDiff.unshift(escapeHtml(oldWords[i - 1]));
            toDiff.unshift(escapeHtml(newWords[j - 1]));
            i--;
            j--;
        } else if (dp[i - 1][j] >= dp[i][j - 1]) {
            fromDiff.unshift(`<span class='diff-deleted'>${escapeHtml(oldWords[i - 1])}</span>`);
            i--;
        } else {
            toDiff.unshift(`<span class='diff-added'>${escapeHtml(newWords[j - 1])}</span>`);
            j--;
        }
    }

    while (i > 0) {
        fromDiff.unshift(`<span class='diff-deleted'>${escapeHtml(oldWords[i - 1])}</span>`);
        i--;
    }
    while (j > 0) {
        toDiff.unshift(`<span class='diff-added'>${escapeHtml(newWords[j - 1])}</span>`);
        j--;
    }

    return {
        htmlFrom: fromDiff.join(''),
        htmlTo: toDiff.join('')
    };
}


function showCustomAlert(message) {
    document.getElementById('customAlertMessage').innerText = message;
    document.getElementById('customAlert').style.display = 'flex';
}
function closeCustomAlert() {
    document.getElementById('customAlert').style.display = 'none';
}

function viewSelectedDiff() {
    const checks = Array.from(document.querySelectorAll('.compare-check:checked'));

    if (checks.length === 1) {
        const c = checks[0];
        const field = c.dataset.field;
        let from = c.dataset.from;
        let to = c.dataset.to;

        from = from.replace(/\\n/g, '\n');
        to = to.replace(/\\n/g, '\n');

        const diffs = simpleDiffHtml(from, to);
        document.getElementById('diffFrom').innerHTML = diffs.htmlFrom.replace(/\n/g, '<br>');
        document.getElementById('diffTo').innerHTML = diffs.htmlTo.replace(/\n/g, '<br>');
        document.getElementById('diffTitle').innerText = `Compare ${field}`;
        document.getElementById('diffOverlay').style.display = 'block';
    }
    else if (checks.length === 2) {
        const [c1, c2] = checks;
        const field1 = c1.dataset.field;
        const field2 = c2.dataset.field;

        if (field1 !== field2) {
            showCustomAlert('Selected rows must be of the same type to compare.');
            return;
        }

        const changeId1 = parseInt(c1.value);
        const changeId2 = parseInt(c2.value);

        const older = changeId1 < changeId2 ? c1 : c2;
        const newer = changeId1 > changeId2 ? c1 : c2;

        let newerTo = older.dataset.to.replace(/\\n/g, '\n');
        let olderFrom = newer.dataset.from.replace(/\\n/g, '\n');

        const diffs = simpleDiffHtml(olderFrom, newerTo);

        document.getElementById('diffFrom').innerHTML = diffs.htmlFrom.replace(/\n/g, '<br>');
        document.getElementById('diffTo').innerHTML = diffs.htmlTo.replace(/\n/g, '<br>');
        document.getElementById('diffTitle').innerText = `Compare ${field1}`;
        document.getElementById('diffOverlay').style.display = 'block';
    }
    else {
        showCustomAlert('Select one or two rows to view changes.');
    }
}

function applyFilters() {
    const date = document.getElementById('filterDate').value;
    const user = document.getElementById('filterUser').value.toLowerCase();
    const field = document.getElementById('filterField').value.toLowerCase();

    document.querySelectorAll('#historyTable tbody tr').forEach(row => {
        const matchesDate = !date || row.dataset.date === date;
        const matchesUser = !user || row.dataset.user.toLowerCase() === user;
        const matchesField = !field || row.dataset.field.toLowerCase() === field;
        row.style.display = (matchesDate && matchesUser && matchesField) ? '' : 'none';
    });
}

function populateDropdowns() {
    const userSet = new Set();
    const fieldSet = new Set();
    const dateSet = new Set();

    document.querySelectorAll('#historyTable tbody tr').forEach(row => {
        userSet.add(row.dataset.user);
        fieldSet.add(row.dataset.field);
        dateSet.add(row.dataset.date);
    });

    // Populate user dropdown
    const userSelect = document.getElementById('filterUser');
    userSet.forEach(user => {
        const opt = document.createElement('option');
        opt.value = user;
        opt.textContent = user;
        userSelect.appendChild(opt);
    });

    // Populate field dropdown
    const fieldSelect = document.getElementById('filterField');
    fieldSet.forEach(field => {
        const opt = document.createElement('option');
        opt.value = field;
        opt.textContent = field;
        fieldSelect.appendChild(opt);
    });

    // Populate date dropdown (sorted descending)
    const dateSelect = document.getElementById('filterDate');
    Array.from(dateSet).sort((a, b) => b.localeCompare(a)).forEach(date => {
        const opt = document.createElement('option');
        opt.value = date;
        opt.textContent = date;
        dateSelect.appendChild(opt);
    });
}

document.getElementById('filterDate').addEventListener('input', applyFilters);
document.getElementById('filterUser').addEventListener('change', applyFilters);
document.getElementById('filterField').addEventListener('change', applyFilters);

document.addEventListener('DOMContentLoaded', () => {
    populateDropdowns();
    applyFilters();
});
</script>
");

            return sb.ToString();

        }



        public string BuildIssueDetailFullPageHtml(
    string headerLine,
    string issueType,
    string statusIcon,
    string status,
    string createdDate,
    string lastUpdated,
    string issueUrl,
    string resolvedDesc,
    string attachmentsHtml,
    string linksHtml,
    string historyHtml,
    string encodedJson)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link href='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/themes/prism.css' rel='stylesheet' />
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/prism.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-gherkin.min.js'></script>
  <script src='https://cdn.jsdelivr.net/npm/prismjs@1.29.0/components/prism-json.min.js'></script>
  <link href='https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&display=swap' rel='stylesheet' />
<link rel='stylesheet' href='{cssHref}' />
</head
  <h2>{headerLine}</h2>
  <div style='margin-bottom: 20px; font-size: 0.95em; color: #444; display: flex; gap: 40px; align-items: center;'>
    <div>🧰 <strong>Type:</strong> {issueType}</div>
    <div>{statusIcon} <strong>Status:</strong> {System.Web.HttpUtility.HtmlEncode(status)}</div>
    <div>📅 <strong>Created:</strong> {createdDate}</div>
    <div>📅 <strong>Updated:</strong> {lastUpdated}</div>
    <div>🔗 <a href='{issueUrl}' onclick='openInBrowser(this.href)'>Open in Browser</a></div>
  </div>

  <details open>
    <summary>Description</summary>
    <section>{resolvedDesc}</section>
  </details>

  {attachmentsHtml}

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
      const scrollAmount = 160;
      strip.scrollLeft += direction * scrollAmount;
    }}

    function openInBrowser(url) {{
      if (window.chrome?.webview) {{
        window.chrome.webview.postMessage({{ action: 'openInBrowser', url }});
      }}
    }}
  </script>
{focustToTreeJS}
</body>
</html>";
        }

        /// <summary>
        /// Handles messages received from the embedded WebView2 browser.
        /// Supports navigation, file download, and node selection based on messages from the HTML UI.
        /// - If the message is a JSON object, handles actions like 'openInBrowser' and 'download'.
        /// - If the message is a plain string (e.g., Jira issue key), selects and loads the corresponding tree node.
        /// </summary>
        /// <param name="sender">The WebView2 control sending the message.</param>
        /// <param name="e">CoreWebView2WebMessageReceivedEventArgs containing the message.</param>
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string message = null;
                try { message = e.TryGetWebMessageAsString(); }
                catch { message = e.WebMessageAsJson; }
                if (string.IsNullOrWhiteSpace(message)) return;
                message = message.Trim();

                if (message == "__tree_focus__")
                {
                    tree.Focus();
                    return;
                }

                if (message.StartsWith("{"))
                {
                    using var jsonDoc = JsonDocument.Parse(message);
                    var root = jsonDoc.RootElement;

                    // Handle browser navigation requests
                    if (root.TryGetProperty("action", out var actionProp) &&
                        actionProp.GetString() == "openInBrowser")
                    {
                        string url = null;
                        if (root.TryGetProperty("url", out var urlProp))
                            url = urlProp.GetString();
                        if (string.IsNullOrWhiteSpace(url) && Uri.TryCreate(e.Source, UriKind.Absolute, out var fallbackUri))
                            url = fallbackUri.ToString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                        return;
                    }

                    // Handle file download requests
                    if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "download")
                    {
                        var filePath = root.GetProperty("path").GetString();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            if (filePath.StartsWith("file:///"))
                                filePath = Uri.UnescapeDataString(filePath.Substring(8));

                            // If it's a Jira REST API URL, download and save
                            if (filePath.StartsWith("/rest/api/3/attachment/content") || filePath.StartsWith("http"))
                            {
                                DownloadAndOpenJiraAttachment(filePath);
                            }
                            else
                            {
                                SaveFile(filePath);
                            }
                        }
                        return;
                    }
                }
                else
                {
                    SelectAndLoadTreeNode(message);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebMessageReceived error: " + ex.Message);
            }
        }

        private async void DownloadAndOpenJiraAttachment(string contentUrl)
        {
            try
            {
                string fullUrl = contentUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? contentUrl
                    : jiraBaseUrl.TrimEnd('/') + contentUrl;

                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                // Jira may redirect, so allow auto-redirect
                var response = await client.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // Get filename from Content-Disposition header or fallback
                string filename = null;
                if (response.Content.Headers.ContentDisposition != null)
                {
                    filename = response.Content.Headers.ContentDisposition.FileName?.Trim('"');
                }
                if (string.IsNullOrWhiteSpace(filename))
                {
                    filename = Path.GetFileName(new Uri(fullUrl).AbsolutePath);
                }

                // Save to temp folder
                string tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "temp");
                Directory.CreateDirectory(tempFolder);
                string destFilePath = Path.Combine(tempFolder, filename);

                using (var fs = new FileStream(destFilePath, FileMode.Create, FileAccess.Write))
                {
                    await response.Content.CopyToAsync(fs);
                }

                // Open the saved file
                System.Diagnostics.Process.Start(new ProcessStartInfo
                {
                    FileName = destFilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error downloading or opening attachment: " + ex.Message);
            }
        }


        /// <summary>
        /// Saves a file to the application's temp directory and opens it with the default application.
        /// If a file with the same name exists, it is replaced.
        /// Used for handling file downloads from the WebView2 interface.
        /// </summary>
        /// <param name="sourceFilePath">The full path to the source file to be saved and opened.</param>
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
                File.Copy(sourceFilePath, destFilePath, overwrite: false);

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

        /// <summary>
        /// Replaces Jira links, SVN feature references, and color macros in an HTML description string.
        /// - Converts Jira issue links to clickable links with summary and key.
        /// - Embeds SVN feature file contents as code blocks.
        /// - Inlines Jira attachment images as base64-encoded images.
        /// - Handles color macros for dark mode compatibility.
        /// </summary>
        /// <param name="htmlDesc">The raw HTML description from Jira.</param>
        /// <returns>
        /// HTML string with Jira links, SVN features, and color macros replaced for display.
        /// </returns>
        public static string BuildHTMLSection_DESCRIPTION(string htmlDesc, string key)
        {
            if (string.IsNullOrEmpty(htmlDesc)) return htmlDesc;

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(htmlDesc);

            //InlineAttachmentImages(doc, key);

            ReplaceColorMacrosAndSymbols(doc);
            ReplaceJiraIssueMacros(doc);
            ReplaceJiraAnchorLinks(doc, issueDict);
            ReplaceWikiStyleLinks(doc, issueDict);
            ReplaceJiraAttachments(doc);
            ReplaceSvnFeatures(doc, issueDict);

            return doc.DocumentNode.InnerHtml;
        }

        private static void InlineAttachmentImages(HtmlAgilityPack.HtmlDocument doc, string issueKey)
        {
            if (!issueDtoDict.TryGetValue(issueKey, out var issueDto) || issueDto == null)
                return;

            // Find all text nodes that look like image file names
            var imageRegex = new Regex(@"\b([\w\-\.]+)\.(png|jpg|jpeg|gif|bmp|webp|svg)\b", RegexOptions.IgnoreCase);

            // Collect all attachment images for this issue
            var imageAttachments = new Dictionary<string, Dictionary<string, object>>();
            if (issueDto.CustomFields.TryGetValue("attachments", out var attachmentsObj) && attachmentsObj is List<Dictionary<string, object>> attachmentsList)
            {
                foreach (var att in attachmentsList)
                {
                    if (att.TryGetValue("filename", out var fnObj) && fnObj is string filename &&
                        att.TryGetValue("mimeType", out var mtObj) && mtObj is string mimeType &&
                        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                        att.TryGetValue("content", out var contentObj) && contentObj is string contentUrl)
                    {
                        imageAttachments[filename] = att;
                    }
                }
            }

            // Scan all text nodes for image file names
            var textNodes = doc.DocumentNode.SelectNodes("//text()");
            if (textNodes == null) return;

            foreach (var textNode in textNodes.ToList())
            {
                var matches = imageRegex.Matches(textNode.InnerText);
                if (matches.Count == 0) continue;

                var parent = textNode.ParentNode;
                var originalText = textNode.InnerText;
                var replacedHtml = originalText;

                foreach (Match match in matches)
                {
                    string filename = match.Value;
                    if (imageAttachments.TryGetValue(filename, out var att))
                    {
                        string mimeType = att["mimeType"].ToString();
                        string contentUrl = att["content"].ToString();

                        // Download and encode image as base64
                        try
                        {
                            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                            using var client = new HttpClient();
                            client.BaseAddress = new Uri(jiraBaseUrl);
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                            using var response = client.GetAsync(contentUrl).Result;
                            response.EnsureSuccessStatusCode();
                            var imageBytes = response.Content.ReadAsByteArrayAsync().Result;
                            string base64 = Convert.ToBase64String(imageBytes);

                            string imgTag = $"<img src=\"data:{mimeType};base64,{base64}\" alt=\"{HttpUtility.HtmlEncode(filename)}\" style=\"max-width:100%;border-radius:4px;border:1px solid #ccc;\" />";
                            replacedHtml = replacedHtml.Replace(filename, imgTag);
                        }
                        catch
                        {
                            // If image fetch fails, leave the filename as is
                        }
                    }
                }

                if (replacedHtml != originalText)
                {
                    var fragment = HtmlNode.CreateNode($"<span>{replacedHtml}</span>");
                    parent.InsertBefore(fragment, textNode);
                    parent.RemoveChild(textNode);
                }
            }
        }

        private static void ReplaceSvnFeatures(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, JiraIssue> issueDict)
        {
            var textNodes = doc.DocumentNode.SelectNodes("//text()[contains(., '&#91;svn://')]");
            if (textNodes == null) return;

            foreach (var textNode in textNodes.ToList())
            {
                var parent = textNode.ParentNode;
                var originalText = textNode.InnerHtml;

                var replacedHtml = Regex.Replace(originalText, @"&#91;(svn://[^\|\]]+\.feature)(?:\|svn://[^\]]+\.feature)?&#93;", match =>
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

                if (replacedHtml != originalText)
                {
                    var fragment = HtmlNode.CreateNode($"<div>{replacedHtml}</div>");
                    foreach (var node in fragment.ChildNodes.ToList())
                        parent.InsertBefore(node, textNode);

                    parent.RemoveChild(textNode);
                }
            }
        }

        private static string GetIssueTypeIconHtml(string issueType)
        {
            if (!string.IsNullOrEmpty(issueType) && frmMain.typeIcons.TryGetValue(issueType, out var fileName))
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(fullPath);
                        string base64 = Convert.ToBase64String(bytes);
                        return $"<img src='data:image/png;base64,{base64}' style='width:14px; height:14px; vertical-align:middle; margin-right:6px; border-radius:4px;' title='{HttpUtility.HtmlEncode(issueType)}' />";
                    }
                    catch { }
                }
            }
            return "";
        }


        private static void ReplaceJiraIssueMacros(HtmlAgilityPack.HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//span[contains(@class, 'jira-issue-macro')]");
            if (nodes == null) return;

            foreach (var span in nodes.ToList()) // clone to avoid collection modification
            {
                var key = span.GetAttributeValue("data-jira-key", null);
                var summary = span.Descendants("a").FirstOrDefault()?.GetAttributeValue("title", null);
                var issueType = "";
                var icon = null as string;

                if (string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(key) && issueDict.TryGetValue(key, out var issue))
                {
                    summary = issue.Summary;
                    issueType = issue.Type;
                    icon = GetIconForType(issueType); // returns image URL or base64 string
                }else if (!string.IsNullOrWhiteSpace(summary) && !string.IsNullOrWhiteSpace(key) && issueDict.TryGetValue(key, out var issueRertry))
                {
                    summary = issueRertry.Summary;
                    issueType = issueRertry.Type;
                    icon = GetIconForType(issueType); // returns image URL or base64 string
                }

                if (string.IsNullOrWhiteSpace(summary)) summary = "Summary not found!";

                string iconHtml = "";

                // Get icon HTML
                if (!string.IsNullOrEmpty(issueType) && frmMain.typeIcons.TryGetValue(issueType, out var fileName))
                {
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            string base64 = Convert.ToBase64String(bytes);
                            iconHtml = $"<img src='data:image/png;base64,{base64}' style='width:14px; height:14px; vertical-align:middle; margin-right:6px; border-radius:4px;' title='{HttpUtility.HtmlEncode(issueType)}' />";
                        }
                        catch { }
                    }
                }

                var newLink = HtmlNode.CreateNode($"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(summary)} [{key}]</a>");

                span.ParentNode.ReplaceChild(newLink, span);
            }
        }

        private static void ReplaceColorMacrosAndSymbols(HtmlAgilityPack.HtmlDocument doc)
        {
            var html = doc.DocumentNode.InnerHtml;

            // Replace color start macros
            html = Regex.Replace(html, @"\{color:(#[0-9a-fA-F]{6})\}", match =>
            {
                var hex = match.Groups[1].Value.ToLower();
                if (hex == "#ffffff") hex = "#000000";
                return $"<span style=\"color:{hex}\">";
            });

            // Replace color end macros
            html = Regex.Replace(html, @"\{color\}", "</span>", RegexOptions.IgnoreCase);

            // Replace plain (*) with <sup>🔸</sup>
            html = Regex.Replace(html, @"(?<!\w)\*(?!\w)", "<sup>🔸</sup>");

            // Replace <img ... star_yellow.png ... > with <sup>⭐</sup>
            html = Regex.Replace(
                html,
                @"<img\s+[^>]*?src\s*=\s*[""']?/images/icons/emoticons/star_yellow\.png[""'][^>]*?>",
                "<sup>★</sup>",
                RegexOptions.IgnoreCase
            );

            // Reload modified HTML
            doc.LoadHtml(html);
        }



        private static void ReplaceJiraAnchorLinks(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, JiraIssue> issueDict)
        {
            var nodes = doc.DocumentNode.SelectNodes("//a[contains(@href, '/browse/')]");
            if (nodes == null) return;

            foreach (var node in nodes.ToList())
            {
                var href = node.GetAttributeValue("href", "");
                var keyMatch = Regex.Match(href, @"/browse/(\w+-\d+)");
                if (!keyMatch.Success) continue;

                var key = keyMatch.Groups[1].Value;
                var issueType = "";
                var icon = null as string;

                string title = node.GetAttributeValue("title", null);
                if (string.IsNullOrWhiteSpace(title) && issueDict.TryGetValue(key, out var issue))
                {
                    title = issue.Summary;
                    issueType = issue.Type;
                    icon = GetIconForType(issueType); // returns image URL or base64 string
                }
                else if (!string.IsNullOrWhiteSpace(title) && issueDict.TryGetValue(key, out var issueRertry))
                {
                    issueType = issueRertry.Type;
                    icon = GetIconForType(issueType); // returns image URL or base64 string
                }

                if (string.IsNullOrWhiteSpace(title))
                    title = "Summary not found!";

                string iconHtml = "";

                // Get icon HTML
                if (!string.IsNullOrEmpty(issueType) && frmMain.typeIcons.TryGetValue(issueType, out var fileName))
                {
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", fileName);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(fullPath);
                            string base64 = Convert.ToBase64String(bytes);
                            iconHtml = $"<img src='data:image/png;base64,{base64}' style='width:14px; height:14px; vertical-align:middle; margin-right:6px; border-radius:4px;' title='{HttpUtility.HtmlEncode(issueType)}' />";
                        }
                        catch { }
                    }
                }

                var newLink = HtmlNode.CreateNode($"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(title)} [{key}]</a>");
                node.ParentNode.ReplaceChild(newLink, node);
            }
        }

        private static void ReplaceWikiStyleLinks(HtmlAgilityPack.HtmlDocument doc, Dictionary<string, JiraIssue> issueDict)
        {
            // Step 1: Replace real <a> external Jira links
            var linkNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, 'external-link') and (@data-key or contains(@href, '/browse/'))]");
            if (linkNodes != null)
            {
                foreach (var node in linkNodes.ToList())
                {
                    string key = null;

                    // Try to get key from data-key attribute
                    if (node.Attributes["data-key"] != null)
                    {
                        key = node.Attributes["data-key"].Value;
                    }
                    else
                    {
                        var href = node.GetAttributeValue("href", "");
                        var match = Regex.Match(href, @"/browse/([A-Z]+-\d+)");
                        if (match.Success)
                            key = match.Groups[1].Value;
                    }

                    if (string.IsNullOrEmpty(key))
                        continue;

                    string summary = issueDict.TryGetValue(key, out var issue) ? issue.Summary : node.InnerText.Trim();
                    string issueType = issueDict.TryGetValue(key, out var issue2) ? issue2.Type : "";
                    string iconHtml = GetIssueTypeIconHtml(issueType);

                    var newLink = HtmlNode.CreateNode($"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(summary)} [{key}]</a>");
                    node.ParentNode.ReplaceChild(newLink, node);
                }
            }

            // Step 2: Handle wiki-style links and smart-link anchors
            var REGEX_FONT_WRAPPED_LINK = new Regex(@"\[\s*<font[^>]*>(.*?\[([A-Z]+-\d+)\].*?)<\/font>\s*\|https:\/\/[^\]]+\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var REGEX_ANCHOR_WRAPPED_LINK = new Regex(@"\[(.*?)<a[^>]+data-key\s*=\s*""([A-Z]+-\d+)""[^>]*>.*?\[([A-Z]+-\d+)\].*?</a>\s*\|https:\/\/[^\]]+\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var REGEX_SMART_LINK = new Regex(@"<a[^>]+data-key\s*=\s*""(?<key>[A-Z]+-\d+)""[^>]*>.*?smart-link\s*\[\k<key>\]\s*</a>",RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var targetNodes = doc.DocumentNode
                                            .SelectNodes("//*")
                                            ?.Where(n => !new[] { "script", "style" }.Contains(n.Name.ToLower()))
                                            ?? Enumerable.Empty<HtmlNode>();

            foreach (var node in targetNodes)
            {
                var html = node.InnerHtml;

                // === 1. Handle font-wrapped wiki links ===
                foreach (Match match in REGEX_FONT_WRAPPED_LINK.Matches(html))
                {
                    string fullMatch = match.Value;
                    string title = match.Groups[1].Value.Trim();
                    string key = match.Groups[2].Value.Trim();
                    title = title.Replace($"[{key}]", "").Trim();

                    string summary = issueDict.TryGetValue(key, out var issue) ? issue.Summary : title;
                    string issueType = issueDict.TryGetValue(key, out var issue2) ? issue2.Type : "";
                    string iconHtml = GetIssueTypeIconHtml(issueType);

                    string replacement = $"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(summary)} [{key}]</a>";
                    html = html.Replace(fullMatch, replacement);
                }

                // === 2. Handle anchor-wrapped wiki links ===
                foreach (Match match in REGEX_ANCHOR_WRAPPED_LINK.Matches(html))
                {
                    string fullMatch = match.Value;
                    string titleBeforeAnchor = match.Groups[1].Value.Trim();
                    string key = match.Groups[2].Value.Trim();

                    string summary = issueDict.TryGetValue(key, out var issue) ? issue.Summary : titleBeforeAnchor;
                    string issueType = issueDict.TryGetValue(key, out var issue2) ? issue2.Type : "";
                    string iconHtml = GetIssueTypeIconHtml(issueType);

                    string displayTitle = $"{summary} [{key}]";
                    string replacement = $"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(displayTitle)}</a>";
                    html = html.Replace(fullMatch, replacement);
                }

                // === 3. Handle hardcoded smart-link anchors ===
                foreach (Match match in REGEX_SMART_LINK.Matches(html))
                {
                    string fullMatch = match.Value;
                    string key = match.Groups["key"].Value.Trim();

                    string summary = issueDict.TryGetValue(key, out var issue) ? issue.Summary : "Summary Not Found!";
                    string issueType = issueDict.TryGetValue(key, out var issue2) ? issue2.Type : "";
                    string iconHtml = GetIssueTypeIconHtml(issueType);

                    string displayTitle = $"{summary} [{key}]";
                    string replacement = $"<a class='issue-link' href=\"#\" data-key=\"{key}\">{iconHtml}{HttpUtility.HtmlEncode(displayTitle)}</a>";
                    html = html.Replace(fullMatch, replacement);
                }

                node.InnerHtml = html;
            }
        }


        private static void ReplaceJiraAttachments(HtmlAgilityPack.HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes("//img[contains(@src, '/rest/api/3/attachment/content/')]");
            if (nodes == null) return;

            foreach (var node in nodes.ToList())
            {
                var src = node.GetAttributeValue("src", null);
                if (string.IsNullOrEmpty(src)) continue;

                var match = Regex.Match(src, @"/attachment/content/(\d+)");
                if (!match.Success) continue;

                string attachmentId = match.Groups[1].Value;

                try
                {
                    var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}"));
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(jiraBaseUrl);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                    var response = client.GetAsync(src).Result;
                    response.EnsureSuccessStatusCode();
                    var imageBytes = response.Content.ReadAsByteArrayAsync().Result;

                    string base64 = Convert.ToBase64String(imageBytes);
                    string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";

                    var newNode = HtmlNode.CreateNode(
                        $"<img src=\"data:{contentType};base64,{base64}\" style=\"max-width:100%;border-radius:4px;border:1px solid #ccc;\" />");

                    node.ParentNode.ReplaceChild(newNode, node);
                }
                catch (Exception ex)
                {
                    var errorNode = HtmlNode.CreateNode(
                        $"<div style='color:red;'>⚠ Failed to load attachment ID {attachmentId}: {HttpUtility.HtmlEncode(ex.Message)}</div>");
                    node.ParentNode.ReplaceChild(errorNode, node);
                }
            }
        }



        /// <summary>
        /// Selects and loads a tree node by its Jira issue key.
        /// If the node is found, it is selected, made visible, and focused in the tree view.
        /// Used for navigation from WebView2 messages and other UI actions.
        /// </summary>
        /// <param name="key">The Jira issue key to select (e.g. "REQ-123").</param>
        public async void SelectAndLoadTreeNode(string key)
        {
            if (isNavigatingToNode) return; // Prevent recursion
            isNavigatingToNode = true;
            try
            {
                var node = await ExpandPathToKeyAsync(key);
                if (node != null)
                {
                    tree.SelectedNode = node;
                    node.EnsureVisible();
                    tree.Focus(); // <-- Always focus tree
                }
                else
                {
                    if (!key.ToLower().StartsWith("recent updates") 
                        && !key.ToLower().StartsWith("welcome to") 
                        && !key.ToLower().ToLower().StartsWith("__tree_focus__")
                        && !key.ToLower().ToLower().StartsWith("__keep_webview_focus__"))
                    {
                        ShowTrayNotification(key);
                    }
                }
            }
            finally
            {
                isNavigatingToNode = false;
                tree.Focus(); // <-- Always focus tree after any navigation
            }
        }

        /// <summary>
        /// Formats a raw JSON string with indentation for improved readability.
        /// If the input is not valid JSON, returns the original string.
        /// </summary>
        /// <param name="rawJson">The raw JSON string to format.</param>
        /// <returns>
        /// A pretty-printed JSON string, or the original string if parsing fails.
        /// </returns>
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

        /// <summary>
        /// Recursively selects a TreeNode by Jira issue key, expanding and focusing the node if found.
        /// Returns true if the node was found and selected; otherwise, false.
        /// </summary>
        /// <param name="node">The root TreeNode to start searching from.</param>
        /// <param name="issueKey">The Jira issue key to select.</param>
        /// <returns>
        /// True if the node was found and selected; otherwise, false.
        /// </returns>
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

        /// <summary>
        /// Handles the tab selection change event for the details TabControl.
        /// When a tab is selected, attempts to select and focus the corresponding issue node in the tree.
        /// </summary>
        /// <param name="sender">The TabControl whose selected tab changed.</param>
        /// <param name="e">Event arguments.</param>
        private void TabDetails_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressTabSelection) return; // Only handle user-initiated tab changes

            var selectedTab = tabDetails.SelectedTab;
            if (selectedTab == null) return;

            string issueKey = selectedTab.Text;
            // Only select and focus if tab text is a valid Jira issue key (e.g., "PROJ-123")
            if (!string.IsNullOrEmpty(issueKey) && System.Text.RegularExpressions.Regex.IsMatch(issueKey, @"^[A-Z]+-\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                SelectAndLoadTreeNode(issueKey);
                tree.Focus();
            }
            // Otherwise, do nothing (do not select or focus tree)
        }

        //After context menu actions, always focus tree
        private void treeContextMenu_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            tree.Focus(); // <-- Always focus tree after context menu closes
        }

        /// <summary>
        /// Handles the click event for the "Update Hierarchy" menu item.
        /// Prompts the user for confirmation, then triggers a full hierarchy sync if confirmed.
        /// </summary>
        /// <param name="sender">The menu item clicked.</param>
        /// <param name="e">Event arguments.</param>
        private void updateHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var DialogUpdateHierarchy = new Form
            {
                Text = "Update Hierarchy",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                Width = 400,
                Height = 254,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10),
                BackColor = GetCSSColor_Tree_Background(cssPath),
                Padding = new Padding(20),
            };

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
            if (File.Exists(iconPath))
            {
                DialogUpdateHierarchy.Icon = new Icon(iconPath);
            }

            // Main layout
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0),
                BackColor = GetCSSColor_Tree_Background(cssPath),
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            // Label
            var lbl = new Label
            {
                Text = "Select a project to update.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Padding = new Padding(10),
                MaximumSize = new Size(400, 0),
            };

            // ComboBox
            var cmbProjects = new System.Windows.Forms.ComboBox
            {
                Font = new Font("Segoe UI", 10),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 300,
                DropDownWidth = 300,
                Anchor = AnchorStyles.None,
            };

            cmbProjects.Items.Add("All Projects");
            foreach (var proj in projectList)
                cmbProjects.Items.Add(proj);
            cmbProjects.SelectedIndex = 0;

            // Centering ComboBox in a Panel
            var comboPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Padding = new Padding(0),
                BackColor = Color.Transparent,
            };

            cmbProjects.Location = new Point((comboPanel.Width - cmbProjects.Width) / 2, (comboPanel.Height - cmbProjects.Height) / 2);
            cmbProjects.Anchor = AnchorStyles.None;

            comboPanel.Controls.Add(cmbProjects);
            comboPanel.Resize += (s, e) =>
            {
                cmbProjects.Location = new Point(
                    (comboPanel.Width - cmbProjects.Width) / 2,
                    (comboPanel.Height - cmbProjects.Height) / 2
                );
            };

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 10, 0, 0),
            };

            var btnUpdate = CreateDialogButton("Update", DialogResult.Yes, true);
            var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);
            btnUpdate.Margin = new Padding(10, 0, 0, 0);
            btnCancel.Margin = new Padding(10, 0, 0, 0);
            buttonPanel.Controls.Add(btnUpdate);
            buttonPanel.Controls.Add(btnCancel);

            // Add controls to layout
            layout.Controls.Add(lbl, 0, 0);
            layout.Controls.Add(comboPanel, 0, 1);
            layout.Controls.Add(buttonPanel, 0, 2);

            DialogUpdateHierarchy.Controls.Add(layout);
            DialogUpdateHierarchy.AcceptButton = btnUpdate;
            DialogUpdateHierarchy.CancelButton = btnCancel;


            var result = DialogUpdateHierarchy.ShowDialog(this);

            if (result == DialogResult.Yes)
            {
                string selectedProject = cmbProjects.SelectedItem?.ToString();
                if (selectedProject == "All Projects")
                {
                    _ = LoadAllProjectsToTreeAsync(true);
                }
                else
                {
                    _ = LoadAllProjectsToTreeAsync(true, selectedProject);
                }
            }
        }

        /// <summary>
        /// Handles the click event for the "Configuration" menu item.
        /// Opens the configuration form for editing Jira and project settings.
        /// </summary>
        /// <param name="sender">The menu item clicked.</param>
        /// <param name="e">Event arguments.</param>
        private void configurationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LaunchConfigForm();
        }

        /// <summary>
        /// Launches the configuration form as a modal dialog, centered on the parent form.
        /// Allows the user to view and edit Jira and project configuration settings.
        /// </summary>
        private void LaunchConfigForm()
        {
            using (var configForm = new frmConfiguration())
            {
                configForm.StartPosition = FormStartPosition.CenterParent;
                configForm.ShowDialog(this);
            }
        }

        // In your frmMain constructor or initialization method:
        private void InitializeTabContextMenu()
        {
            tabContextMenu = new ContextMenuStrip
            {
                ShowImageMargin = true, // Make sure image space is shown
                ImageScalingSize = new Size(24, 24) // Increase image space if needed
            };

            tabContextMenu.Items.Add(new ToolStripMenuItem("Edit...", CreateIconFromUnicode("✏️"), (s, e) => EditCurrentIssue())
            {
                ImageScaling = ToolStripItemImageScaling.None
            });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close This Tab", CreateIconFromUnicode("❌"), (s, e) => CloseTab(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close All Other Tabs", CreateIconFromUnicode("🔀"), (s, e) => CloseAllOtherTabs(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close Tabs on Left", CreateIconFromUnicode("⬅️"), (s, e) => CloseTabsOnLeft(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close Tabs on Right", CreateIconFromUnicode("➡️"), (s, e) => CloseTabsOnRight(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close All Tabs", CreateIconFromUnicode("🗑"), (s, e) => CloseAllTabs()) { ImageScaling = ToolStripItemImageScaling.None });

            tabDetails.MouseUp += TabDetails_MouseUp;

        }

        private void EditCurrentIssue(bool loadedFromTreeContextMenu = false)
        {
            string issueKey = null;
            string issueSummaryAndKey = null;

            if (loadedFromTreeContextMenu)
            {
                // Handle tree context menu
                if (tree.SelectedNode == null) return;
                issueKey = tree.SelectedNode.Tag?.ToString();
                issueSummaryAndKey = tree.SelectedNode.Text;
            }
            else
            {
                // Handle tab context menu
                if (rightClickedTab == null) return;
                issueKey = rightClickedTab.Text;
                issueSummaryAndKey = rightClickedTab.ToolTipText;
            }

            if (string.IsNullOrWhiteSpace(issueKey)) return;

            string url = $"{jiraBaseUrl}/browse/{issueKey}";

            using (var DialogEditIssue = new Form())
            {
                DialogEditIssue.Text = $"Edit : {issueSummaryAndKey}";
                DialogEditIssue.Width = 1200;
                DialogEditIssue.Height = 800;
                DialogEditIssue.StartPosition = FormStartPosition.CenterParent;
                DialogEditIssue.FormBorderStyle = FormBorderStyle.FixedDialog;
                DialogEditIssue.MinimizeBox = false;
                DialogEditIssue.MaximizeBox = true;

                // Set dialog icon from Monovera.ico in images folder
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                if (File.Exists(iconPath))
                {
                    DialogEditIssue.Icon = new Icon(iconPath);
                }

                var webView = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill
                };

                DialogEditIssue.Controls.Add(webView);

                DialogEditIssue.Shown += async (s, e) =>
                {
                    await webView.EnsureCoreWebView2Async();
                    webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                    webView.CoreWebView2.Navigate(url);
                };

                DialogEditIssue.FormClosed += async (s, e) =>
                {
                    var node = await ExpandPathToKeyAsync(issueKey);
                    if (node != null)
                    {
                        lastTreeMouseButton = MouseButtons.Left;
                        await Tree_AfterSelect_Internal(tree, new TreeViewEventArgs(node), true);
                    }
                };

                DialogEditIssue.ShowDialog(this);
            }
        }

        private void TabDetails_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                for (int i = 0; i < tabDetails.TabCount; i++)
                {
                    Rectangle r = tabDetails.GetTabRect(i);
                    if (r.Contains(e.Location))
                    {
                        rightClickedTab = tabDetails.TabPages[i];
                        tabContextMenu.Show(tabDetails, e.Location);
                        break;
                    }
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                for (int i = 0; i < tabDetails.TabCount; i++)
                {
                    Rectangle r = tabDetails.GetTabRect(i);
                    if (r.Contains(e.Location))
                    {
                        var tabToClose = tabDetails.TabPages[i];

                        // Select the tab to the left, if any; otherwise, select the first tab if any remain
                        if (tabDetails.TabPages.Count > 1)
                        {
                            int newIdx = Math.Max(0, i - 1);
                            tabDetails.SelectedTab = tabDetails.TabPages[newIdx];
                        }
                        tabDetails.TabPages.Remove(tabToClose);
                        break;
                    }
                }
            }
        }

        private System.Drawing.Image CreateIconFromUnicode(string unicodeChar, int size = 24)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (Font font = new Font("Segoe UI Emoji", size - 4, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(Color.Black))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    // Draw centered
                    SizeF textSize = g.MeasureString(unicodeChar, font);
                    float x = (size - textSize.Width) / 2;
                    float y = (size - textSize.Height) / 2;
                    g.DrawString(unicodeChar, font, brush, x, y);
                }
            }
            return bmp;
        }

        private async void CloseTab(TabPage tab)
        {
            if (tab == null) return;
            int idx = tabDetails.TabPages.IndexOf(tab);

            // Select the tab to the left, if any; otherwise, select the first tab if any remain
            if (tabDetails.TabPages.Count > 1)
            {
                int newIdx = Math.Max(0, idx - 1);
                tabDetails.SelectedTab = tabDetails.TabPages[newIdx];
            }
            tabDetails.TabPages.Remove(tab);
        }

        private void CloseAllOtherTabs(TabPage tab)
        {
            foreach (TabPage t in tabDetails.TabPages.Cast<TabPage>().ToList())
                if (t != tab) tabDetails.TabPages.Remove(t);
        }

        private void CloseTabsOnLeft(TabPage tab)
        {
            int idx = tabDetails.TabPages.IndexOf(tab);
            for (int i = idx - 1; i >= 0; i--)
                tabDetails.TabPages.RemoveAt(i);
        }

        private void CloseTabsOnRight(TabPage tab)
        {
            int idx = tabDetails.TabPages.IndexOf(tab);
            for (int i = tabDetails.TabPages.Count - 1; i > idx; i--)
                tabDetails.TabPages.RemoveAt(i);
        }

        private void CloseAllTabs()
        {
            tabDetails.TabPages.Clear();
        }

        // In your frmMain constructor or initialization method:
        private void EnableTabDragDrop()
        {
            tabDetails.AllowDrop = true;
            tabDetails.MouseDown += TabDetails_MouseDown;
            tabDetails.MouseMove += TabDetails_MouseMove;
            tabDetails.DragOver += TabDetails_DragOver;
            tabDetails.DragDrop += TabDetails_DragDrop;
        }

        private void TabDetails_MouseDown(object sender, MouseEventArgs e)
        {
            dragTabIndex = -1;
            for (int i = 0; i < tabDetails.TabCount; i++)
            {
                if (tabDetails.GetTabRect(i).Contains(e.Location))
                {
                    dragTabIndex = i;
                    break;
                }
            }
        }

        private void TabDetails_MouseMove(object sender, MouseEventArgs e)
        {
            // Only start drag if index is valid, tab exists, and more than 2 tabs
            if (e.Button == MouseButtons.Left && dragTabIndex != -1 && dragTabIndex < tabDetails.TabPages.Count && tabDetails.TabCount > 2)
            {
                tabDetails.DoDragDrop(tabDetails.TabPages[dragTabIndex], DragDropEffects.Move);
            }
        }

        private void TabDetails_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void TabDetails_DragDrop(object sender, DragEventArgs e)
        {
            // Only allow drop if more than 2 tabs
            if (tabDetails.TabCount <= 2)
                return;

            TabPage draggedTab = e.Data.GetData(typeof(TabPage)) as TabPage;
            if (draggedTab == null || !tabDetails.TabPages.Contains(draggedTab))
                return;

            Point pt = tabDetails.PointToClient(new Point(e.X, e.Y));
            int targetIndex = -1;
            for (int i = 0; i < tabDetails.TabCount; i++)
            {
                if (tabDetails.GetTabRect(i).Contains(pt))
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex == -1 || draggedTab == null)
                return;

            int oldIndex = tabDetails.TabPages.IndexOf(draggedTab);

            // If dropping onto itself, do nothing
            if (oldIndex == targetIndex)
                return;

            tabDetails.TabPages.Remove(draggedTab);

            // Clamp targetIndex to valid range after removal
            if (targetIndex > tabDetails.TabPages.Count)
                targetIndex = tabDetails.TabPages.Count;
            if (targetIndex < 0)
                targetIndex = 0;

            // If moving forward, decrement targetIndex to account for removal
            if (targetIndex > oldIndex)
                targetIndex--;

            tabDetails.TabPages.Insert(targetIndex, draggedTab);
            tabDetails.SelectedTab = draggedTab;
            dragTabIndex = -1;
        }

        private void mnuReport_Click(object sender, EventArgs e)
        {
            GenerateReport();
        }

        private void mnuSearch_Click(object sender, EventArgs e)
        {
            ShowSearchDialog();
        }

        private void menuRead_Click(object sender, EventArgs e)
        {
            ReadSelectedTextOutLoud();
        }

        private async void ReadSelectedTextOutLoud()
        {
            if (tabDetails.SelectedTab is TabPage tab)
            {
                var webView = tab.Controls.OfType<WebView2>().FirstOrDefault();
                if (webView != null && webView.CoreWebView2 != null)
                {
                    string script = "window.getSelection().toString();";
                    string selectedText = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    selectedText = System.Text.RegularExpressions.Regex.Unescape(selectedText.Trim('"'));

                    if (string.IsNullOrWhiteSpace(selectedText))
                        selectedText = "Sorry, I could not find any selected text to read!";

                    // Run TTS in background to keep UI responsive
                    _ = Task.Run(() => TextToSpeechHelper.SpeakWithGoogleDefaultVoice(selectedText));
                }
            }
        }

        private async void mnuRecentUpdates_Click(object sender, EventArgs e)
        {
            // Look for an existing "Recent Updates!" tab
            TabPage recentTab = null;
            foreach (TabPage tab in tabDetails.TabPages)
            {
                if (tab.Text == "Recent Updates!")
                {
                    recentTab = tab;
                    break;
                }
            }

            int days = 14; // Default value

            // If Ctrl is pressed, prompt for number of days
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                string input = Interaction.InputBox("Enter the number of days to view changes:", "Recent Updates", days.ToString());
                if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int parsedDays) && parsedDays > 0)
                {
                    days = parsedDays;
                }
            }

            if (recentTab != null)
            {
                // Tab exists, just focus it
                tabDetails.SelectedTab = recentTab;
            }
            else
            {
                // Tab does not exist, create it
                await ShowRecentlyUpdatedIssuesAsync(tabDetails, days);
            }
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Only prompt if user is closing (not shutting down app programmatically)
            if (e.CloseReason == CloseReason.UserClosing)
            {
                // Check if Jira sequence updates are still in progress
                bool isSequenceWorkerActive = sequenceUpdateWorker != null && !sequenceUpdateWorker.IsCompleted && !sequenceUpdateQueue.IsEmpty;

                if (isSequenceWorkerActive)
                {
                    // Custom dialog for sequence update confirmation
                    var DialogSequenceCancel = new Form
                    {
                        Text = "Jira Sequence Updates In Progress",
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        StartPosition = FormStartPosition.CenterParent,
                        Width = 420,
                        Height = 200,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        ShowInTaskbar = false,
                        Font = new Font("Segoe UI", 10),
                        BackColor = GetCSSColor_Tree_Background(cssPath),
                        Padding = new Padding(20),
                    };

                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                    if (File.Exists(iconPath))
                    {
                        DialogSequenceCancel.Icon = new Icon(iconPath);
                    }

                    var lbl = new Label
                    {
                        Text = "Jira sequence updates are in progress.\nAre you sure you want to cancel them and exit?",
                        Dock = DockStyle.Top,
                        Height = 60,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 11, FontStyle.Bold),
                        Padding = new Padding(0, 10, 0, 10)
                    };

                    var btnYes = CreateDialogButton("Yes", DialogResult.Yes, true);
                    var btnWait = CreateDialogButton("Wait", DialogResult.No);

                   
                    var buttonPanel = new FlowLayoutPanel
                    {
                        FlowDirection = FlowDirection.RightToLeft,
                        Dock = DockStyle.Bottom,
                        Padding = new Padding(0, 10, 0, 0),
                        Height = 60,
                        AutoSize = true
                    };

                    btnYes.Margin = new Padding(10, 0, 0, 0);
                    btnWait.Margin = new Padding(10, 0, 0, 0);

                    buttonPanel.Controls.Add(btnYes);
                    buttonPanel.Controls.Add(btnWait);

                    DialogSequenceCancel.Controls.Add(lbl);
                    DialogSequenceCancel.Controls.Add(buttonPanel);

                    DialogSequenceCancel.AcceptButton = btnYes;
                    DialogSequenceCancel.CancelButton = btnWait;

                    var result = DialogSequenceCancel.ShowDialog(this);

                    if (result == DialogResult.Yes)
                    {
                        // Cancel the sequence update worker and proceed with exit
                        sequenceUpdateCts.Cancel();
                        notifyIcon?.Dispose();
                        base.OnFormClosing(e);
                    }
                    else
                    {
                        // Wait: cancel closing
                        e.Cancel = true;
                        return;
                    }
                }
                else
                {
                    // Normal exit confirmation dialog (existing code)
                    var DialogClosingConfirmation = new Form
                    {
                        Text = "Confirm Exit",
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        StartPosition = FormStartPosition.CenterParent,
                        Width = 400,
                        Height = 200,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        ShowInTaskbar = false,
                        Font = new Font("Segoe UI", 10),
                        BackColor = GetCSSColor_Tree_Background(cssPath),
                        Padding = new Padding(20),
                    };

                    string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "Monovera.ico");
                    if (File.Exists(iconPath))
                    {
                        DialogClosingConfirmation.Icon = new Icon(iconPath);
                    }

                    var lbl = new Label
                    {
                        Text = "Are you sure you want to exit?",
                        Dock = DockStyle.Top,
                        Height = 60,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 11, FontStyle.Bold),
                        Padding = new Padding(0, 10, 0, 10)
                    };

                    var btnExit = CreateDialogButton("Exit", DialogResult.Yes, true);
                    var btnMinimize = CreateDialogButton("Minimize", DialogResult.Ignore);
                    var btnCancel = CreateDialogButton("Cancel", DialogResult.Cancel);

                    var buttonPanel = new FlowLayoutPanel
                    {
                        FlowDirection = FlowDirection.RightToLeft,
                        Dock = DockStyle.Bottom,
                        Padding = new Padding(0, 10, 0, 0),
                        Height = 60,
                        AutoSize = true
                    };

                    btnExit.Margin = new Padding(10, 0, 0, 0);
                    btnMinimize.Margin = new Padding(10, 0, 0, 0);
                    btnCancel.Margin = new Padding(10, 0, 0, 0);

                    buttonPanel.Controls.Add(btnExit);
                    buttonPanel.Controls.Add(btnMinimize);
                    buttonPanel.Controls.Add(btnCancel);

                    DialogClosingConfirmation.Controls.Add(lbl);
                    DialogClosingConfirmation.Controls.Add(buttonPanel);

                    DialogClosingConfirmation.AcceptButton = btnExit;
                    DialogClosingConfirmation.CancelButton = btnCancel;

                    var result = DialogClosingConfirmation.ShowDialog(this);

                    if (result == DialogResult.Yes)
                    {
                        notifyIcon?.Dispose();
                        base.OnFormClosing(e);
                    }
                    else if (result == DialogResult.Ignore)
                    {
                        e.Cancel = true;
                        this.WindowState = FormWindowState.Minimized;
                    }
                    else
                    {
                        e.Cancel = true;
                    }
                }
            }
            else
            {
                // Not user closing, allow normal close
                sequenceUpdateCts.Cancel();
                notifyIcon?.Dispose();
                base.OnFormClosing(e);
            }
        }

        #region Configuration.js Processing
        private string GetSortingFieldForKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || config?.Projects == null)
                return "summary"; // Default fallback

            var keyPrefix = key.Split('-')[0];
            var projectConfig = config.Projects
                .FirstOrDefault(p => p.Root.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase));
            return projectConfig?.SortingField ?? "summary";
        }

        #endregion

        #region GUI Components
        // Helper to create a standardized button
        private System.Windows.Forms.Button CreateDialogButton(string text, DialogResult result, bool isPrimary = false)
        {
            return new System.Windows.Forms.Button
            {
                Text = text,
                DialogResult = result,
                Width = 100,
                Height = 40,
                Font = isPrimary ? new Font("Segoe UI", 10, FontStyle.Bold) : new Font("Segoe UI", 10),
                BackColor = Color.White,
                Anchor = AnchorStyles.None,
                Margin = new Padding(10, 0, 0, 0)
            };
        }
        #endregion
    }

}
