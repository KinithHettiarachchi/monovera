using Microsoft.VisualBasic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SharpSvn;
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
        string tempFolder = "";

        public static string cssPath = "";
        public static string cssHref = "";
        public string LoadingHtml = "";

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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            notifyIcon?.Dispose();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Alphanumeric comparer for natural sorting (e.g. 1,2,10,11).
        /// </summary>
        private class AlphanumericComparer : IComparer<object>
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

            // Set up tab control for details panel
            tabDetails = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "tabDetails",
                BackColor = Color.White
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

            // Always drop as child
            var result = MessageBox.Show(
                $"Are you sure you want to move '{nodeToMove.Text}' under '{targetNode.Text}'?\n\n" +
                $"🌳 {targetNode.Tag}\n" +
                $"   └── 🌱 {nodeToMove.Tag}",
                "Confirm Parent Change!",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            // Capture old parent key before removing
            string oldParentKey = nodeToMove.Parent?.Tag as string;

            tree.Enabled = false; // Disable tree to prevent further interaction during operation

            string newParentKey = targetNode.Tag as string;
            string movedKey = nodeToMove.Tag as string;
            string linkTypeName = hierarchyLinkTypeName.Split(',')[0];


            tree.Enabled = false; // Disable tree during operation

            try
            {
                await jiraService.UpdateParentLinkAsync(movedKey, oldParentKey, newParentKey, linkTypeName);
                // Remove from old parent
                if (nodeToMove.Parent != null)
                    nodeToMove.Parent.Nodes.Remove(nodeToMove);
                else
                    tree.Nodes.Remove(nodeToMove);

                targetNode.Nodes.Add(nodeToMove);
                targetNode.Expand();
                tree.Enabled = true;
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

            // Re-sequence children

            tree.Enabled = false; // Disable tree during operation

            for (int i = 0; i < targetNode.Nodes.Count; i++)
            {
                string siblingKey = targetNode.Nodes[i].Tag as string;
                int sequence = i + 1;
                
                try
                {
                    await jiraService.UpdateSequenceFieldAsync(siblingKey, sequence);
                    tree.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Jira Field Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    tree.Enabled = true;
                    return;
                }
            }
                      
            tree.SelectedNode = nodeToMove;
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

        private async void MoveNodeInTree(TreeNode node, int direction)
        {
            if (node == null) return;
            TreeNodeCollection siblings = node.Parent?.Nodes ?? tree.Nodes;
            int index = siblings.IndexOf(node);
            int newIndex = index + direction;

            // Only move if within bounds
            if (newIndex < 0 || newIndex >= siblings.Count)
                return;

            // Remove and insert at new position
            siblings.RemoveAt(index);
            siblings.Insert(newIndex, node);

            tree.Enabled= false;

            // Update sequence for all siblings
            for (int i = 0; i < siblings.Count; i++)
            {
                string siblingKey = siblings[i].Tag as string;
                int sequence = i + 1;
                try
                {
                    await jiraService.UpdateSequenceFieldAsync(siblingKey, sequence);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Jira Field Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    // Remove and insert at new position
                    tree.Enabled = true;
                    tree.SelectedNode = node;
                    node.EnsureVisible();
                    return;
                }
            }

            // Reselect the moved node
            tree.SelectedNode = node;
            tree.Enabled = true;
            node.EnsureVisible();
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
                ShowSearchDialog(this.tree);
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
        private static Dictionary<string, JiraIssueDto> issueDtoDict = new();
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

            // Create icons for menu items using Unicode characters
            var iconSearch = CreateUnicodeIcon("🔍");
            var iconReport = CreateUnicodeIcon("📄");

            // Create the search menu item with shortcut and icon
            searchMenuItem = new ToolStripMenuItem("Search")
            {
                Image = iconSearch,
                ShortcutKeys = Keys.Control | Keys.Q,
                ShowShortcutKeys = true
            };

            // Create the report menu item with shortcut and icon
            reportMenuItem = new ToolStripMenuItem("Generate Report")
            {
                Image = iconReport,
                ShortcutKeys = Keys.Control | Keys.P,
                ShowShortcutKeys = true
            };

            // Attach event handlers for menu item clicks
            searchMenuItem.Click += (s, e) => ShowSearchDialog(tree);

            reportMenuItem.Click += async (s, e) =>
            {
                GenerateReport();
            };

            // Add menu items to the context menu
            treeContextMenu.Items.Add(searchMenuItem);
            treeContextMenu.Items.Add(reportMenuItem);

            // Assign the context menu to the tree view
            tree.ContextMenuStrip = treeContextMenu;

            if (editorMode)
            {
                AddLinkRelatedMenu();
                AddChangeParentMenu();
                AddCreateIssueMenus();
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
                    || item.Text.StartsWith("Add Sibling")) {
                        item.Enabled = canCreate;
                    } 
                    else if (item.Text.StartsWith("Link related") 
                    || item.Text.StartsWith("Change Parent") 
                    || item.Text.StartsWith("Move Up") 
                    || item.Text.StartsWith("Move Down")) { 
                        item.Enabled = canModify;
                    }
                }
            };
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

            // Gather all keys and summaries from the tree
            var allKeys = new List<string>();
            var keySummaryDict = new Dictionary<string, string>();
            void CollectKeys(TreeNodeCollection nodes)
            {
                foreach (TreeNode node in nodes)
                {
                    if (node.Tag is string key && !string.IsNullOrWhiteSpace(key))
                    {
                        allKeys.Add(key);
                        keySummaryDict[key] = node.Text;
                    }
                    CollectKeys(node.Nodes);
                }
            }
            CollectKeys(tree.Nodes);

            var dlg = new Form
            {
                Text = $"Link related issues to {baseKey}",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.Manual,
                Width = 700,
                Height = 440,
                BackColor = GetCSSColor_Tree_Background(cssPath),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10)
            };

            // Set dialog location to mouse position
            var mousePos = Control.MousePosition;
            dlg.Location = mousePos;

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
                ForeColor= GetCSSColor_DataGrid_Foreground(cssPath),
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
                BackColor =GetCSSColor_Tree_Background(cssPath),
                AutoSize = false,
                Height = 56 // Should match or exceed button height
            };
            var btnLink = new System.Windows.Forms.Button
            {
                Text = "Link",
                DialogResult = DialogResult.OK,
                Width = 100,
                Height = 40, // Increased height for better visibility
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.White
            };
            var btnCancel = new System.Windows.Forms.Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 100,
                Height = 40, // Increased height for better visibility
                Font = new Font("Segoe UI", 10),
                BackColor = Color.White
            };
            buttonPanel.Controls.Add(btnLink);
            buttonPanel.Controls.Add(btnCancel);
            layout.Controls.Add(buttonPanel, 0, 3);

            dlg.Controls.Add(layout);
            //dlg.AcceptButton = btnLink;
            dlg.CancelButton = btnCancel;

            btnLink.Enter += (s, e) => dlg.AcceptButton = btnLink;
            txtInput.Enter += (s, e) => dlg.AcceptButton = null;

            if (dlg.ShowDialog(tree) == DialogResult.OK)
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

                using (var dlg = new Form())
                {
                    dlg.Text = $"Change Parent of {childKey}";
                    dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = tree.PointToScreen(tree.PointToClient(Control.MousePosition));
                    dlg.Width = 420;
                    dlg.Height = 210;
                    dlg.BackColor = GetCSSColor_Tree_Background(cssPath);
                    dlg.MaximizeBox = false;
                    dlg.MinimizeBox = false;
                    dlg.ShowInTaskbar = false;
                    dlg.Font = new Font("Segoe UI", 10);

                    var layout = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        ColumnCount = 2,
                        RowCount = 3,
                        Padding = new Padding(24, 18, 24, 18),
                        BackColor=GetCSSColor_Tree_Background(cssPath),
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
                        BackColor=GetCSSColor_Tree_Background(cssPath)
                    };
                    var btnOk = new System.Windows.Forms.Button
                    {
                        Text = "Link",
                        DialogResult = DialogResult.OK,
                        Width = 100,
                        Height = 36,
                        Font = new Font("Segoe UI", 10, FontStyle.Bold),
                        BackColor = Color.White
                    };
                    var btnCancel = new System.Windows.Forms.Button
                    {
                        Text = "Cancel",
                        DialogResult = DialogResult.Cancel,
                        Width = 100,
                        Height = 36,
                        Font = new Font("Segoe UI", 10),
                        BackColor = Color.White
                    };
                    buttonPanel.Controls.Add(btnOk);
                    buttonPanel.Controls.Add(btnCancel);
                    layout.Controls.Add(buttonPanel, 1, 2);

                    dlg.Controls.Add(layout);
                    dlg.AcceptButton = btnOk;
                    dlg.CancelButton = btnCancel;

                    if (dlg.ShowDialog(tree) == DialogResult.OK)
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
                        TreeNode newParentNode = FindNodeByKey(tree.Nodes, newParentKey, false);
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
            string baseKey= tree.SelectedNode.Tag?.ToString();

            if (mode == "Sibling")
            {
                selectedKey = tree.SelectedNode.Parent.Tag.ToString();
            }
            else if (mode == "Child")
            {
                 selectedKey = tree.SelectedNode.Tag?.ToString();
            } else
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
            var dlg = new Form
            {
                Text = $"Add {mode.ToLower()} node to {baseKey}",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.Manual,
                Location = tree.PointToScreen(tree.PointToClient(Control.MousePosition)),
                Width = 700,
                Height = 280,
                BackColor = GetCSSColor_Tree_Background(cssPath),
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 10)
            };

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
            var btnCreate = new System.Windows.Forms.Button
            {
                Text = "Create",
                DialogResult = DialogResult.OK,
                Width = 100,
                Height = 36,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.White
            };
            var btnCancel = new System.Windows.Forms.Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 100,
                Height = 36,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.White
            };
            buttonPanel.Controls.Add(btnCreate);
            buttonPanel.Controls.Add(btnCancel);
            layout.Controls.Add(buttonPanel, 1, 4);

            dlg.Controls.Add(layout);
            dlg.AcceptButton = btnCreate;
            dlg.CancelButton = btnCancel;


            if (dlg.ShowDialog(tree) == DialogResult.OK)
            {
                string linkMode = cmbMode.SelectedItem.ToString();
                string issueType = cmbType.SelectedItem.ToString();
                string summary = txtSummary.Text.Trim();
                //string description = txtDesc.Text.Trim();

                if (string.IsNullOrWhiteSpace(summary))
                {
                    MessageBox.Show("Summary is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Call async create method
                // Example usage in frmMain.cs
                string? newIssueKey = await jiraService.CreateAndLinkJiraIssueAsync(
                    selectedKey,
                    linkMode,
                    issueType,
                    summary,
                    config);

                MessageBox.Show($"New issue {newIssueKey} has been created as a {mode} of {tree.SelectedNode.Tag?.ToString()}.");
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
        private void ShowSearchDialog(System.Windows.Forms.TreeView tree)
        {
            using (var dlg = new frmSearch(tree))
            {
                dlg.ShowDialog(this);
            }
        }

        /// <summary>
        /// Generates a hierarchical HTML report for the selected Jira issue and its children.
        /// </summary>
        private async void GenerateReport()
        {
            if (tree.SelectedNode?.Tag is string rootKey)
            {
                var result = MessageBox.Show(
                    "This will generate a hierarchical HTML report including all the child issues recursively.\n\nAre you sure you want to continue?",
                    "Generate Report",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
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
                    // Select the tab to the left after closing
                    int idx = i;
                    tabDetails.TabPages.Remove(tab);

                    if (tabDetails.TabPages.Count > 0)
                    {
                        int newIdx = Math.Max(0, idx - 1);
                        tabDetails.SelectedTab = tabDetails.TabPages[newIdx];
                    }
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
                jiraUserName=jiraService.GetConnectedUserNameAsync().Result;
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


            LoadingHtml = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='UTF-8'>
  <link rel='stylesheet' href='{cssHref}' />
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

            // Load all Jira projects and their issues into the tree view
            await LoadAllProjectsToTreeAsync();

            // Show a tab with recently updated issuesup
            ShowRecentlyUpdatedIssuesAsync(tabDetails);
        }

        /// <summary>
        /// Loads all Jira projects and their issues into the tree view.
        /// Optionally forces a fresh sync from the server, bypassing cache.
        /// </summary>
        /// <param name="forceSync">If true, ignores cache and fetches from Jira.</param>
        private async Task LoadAllProjectsToTreeAsync(bool forceSync = false)
        {
            pbProgress.Visible = true;
            pbProgress.Value = 0;
            pbProgress.Maximum = 100;
            lblProgress.Visible = true;
            lblProgress.Text = "Loading...";

            issueDict.Clear();
            childrenByParent.Clear();
            issueDtoDict.Clear();

            int totalProjects = projectList.Count;
            int currentProject = 0;

            var allIssues = new List<JiraIssue>();

            foreach (var project in projectList)
            {
                currentProject++;
                var projectConfig = config.Projects.FirstOrDefault(p => p.Project == project);
                string sortingField = projectConfig?.SortingField ?? "summary";
                string linkTypeName = projectConfig?.LinkTypeName ?? "Blocks";
                lblProgress.Text = $"Loading project ({currentProject}/{totalProjects}) - {project}...";

                var fieldsList = new List<string> { "summary", "issuetype", "issuelinks", sortingField };
                var issues = await jiraService.GetAllIssuesForProject(project, fieldsList, sortingField, linkTypeName, forceSync, (completed, total, percent) =>
                {
                    pbProgress.Value = (int)Math.Round(percent);
                    lblProgress.Text = $"Loading project ({currentProject}/{totalProjects}) - {project} : {completed}/{total} ({percent:0.0}%)...";
                });



                foreach (var myIssue in issues)
                {
                    var issue = new JiraIssue
                    {
                        Key = myIssue.Key,
                        Summary = myIssue.Summary,
                        Type = myIssue.Type,
                        ParentKey = null,
                        RelatedIssueKeys = new List<string>()
                    };

                    // Use the correct LinkTypeName for this project
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

            // Build parent-child relationships using all issues
            BuildDictionaries(allIssues);

            pbProgress.Visible = false;
            lblProgress.Visible = false;

            // Populate the tree view with root issues and their children
            tree.Invoke(() =>
            {
                tree.Nodes.Clear();
                tree.BeginUpdate();

                var rootKeys = root_key.Split(',').Select(k => k.Trim()).ToHashSet();

                foreach (var rootIssue in issueDict.Values.Where(i => i.ParentKey == null || !issueDict.ContainsKey(i.ParentKey)))
                {
                    if (!rootKeys.Contains(rootIssue.Key)) continue;

                    var projectConfig = config.Projects.FirstOrDefault(p => rootIssue.Key.StartsWith(p.Root));
                    var rootNode = CreateTreeNode(rootIssue);
                    AddChildNodesRecursively(rootNode, rootIssue.Key, projectConfig);
                    tree.Nodes.Add(rootNode);
                    rootNode.Expand();
                }

                tree.EndUpdate();
            });
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
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", "monovera.png");

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
                BackColor= GetCSSColor_Tree_Background(cssPath)
            };

            var webView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill,
                BackColor= GetCSSColor_Tree_Background(cssPath)
            };

            homePage.Controls.Add(webView);
            tabDetails.TabPages.Add(homePage);
            tabDetails.SelectedTab = homePage;

            // Start initializing WebView2
            await webView.EnsureCoreWebView2Async();

            // Show loading page after WebView is ready and attached to UI
            string htmlFilePath = Path.Combine(tempFolder, $"LoadingHtml.html");
            File.WriteAllText(htmlFilePath, LoadingHtml);
            webView.CoreWebView2.Navigate(htmlFilePath);

            // Handle script dialogs
            webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
            {
                var deferral = args.GetDeferral();
                try { args.Accept(); } finally { deferral.Complete(); }
            };

            // Delay a bit to simulate loading or let the user see loading animation
            await Task.Delay(800); // Adjust delay as needed

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
        public async Task ShowRecentlyUpdatedIssuesAsync(TabControl tabDetails)
        {
            // --- Step 1: Prepare WebView2 control and TabPage first ---
            var webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            webView.BackColor= GetCSSColor_Tree_Background(cssPath);

            // Create the tab page immediately
            var updatePage = new TabPage("Recent Updates!")
            {
                ImageKey = "updates",
                ToolTipText = "Issues that were updated during past 30 days!",
                BackColor= GetCSSColor_Tree_Background(cssPath)
            };
            updatePage.Controls.Add(webView);

            // Add to tab control first, so loading screen is visible
            tabDetails.TabPages.Add(updatePage);
            tabDetails.SelectedTab = updatePage;

            // Ensure the tab has an icon list
            if (tabDetails.ImageList == null)
            {
                tabDetails.ImageList = new ImageList { ImageSize = new Size(16, 16) };
            }

            // Load icon if exists
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

            // --- Step 2: Initialize WebView2 and show Loading ---
            await webView.EnsureCoreWebView2Async();

            string htmlFilePath = Path.Combine(tempFolder, $"LoadingHtml.html");
            File.WriteAllText(htmlFilePath, LoadingHtml);
            webView.CoreWebView2.Navigate(htmlFilePath);

            // Handle dialogs and messages
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
                    {
                        SelectAndLoadTreeNode(message);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("WebMessageReceived error: " + ex.Message);
                }
            };

            // Optional: delay a little so user sees loading
            await Task.Delay(800);

            // --- Step 3: Build the JQL and get issues ---
            DateTime oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
            string jql = $"({string.Join(" OR ", projectList.Select(p => $"project = \"{p}\""))}) AND updated >= -14d ORDER BY updated DESC";
            var rawIssues = await frmSearch.SearchJiraIssues(jql, null);

            var tasks = rawIssues.Select(async issue =>
            {
                if (await HasSummaryOrDescriptionChangeAsync(issue.Key))
                    return issue;
                return null;
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

            foreach (var group in grouped)
            {
                sb.AppendLine($@"
<details open>
  <summary>{group.Key:yyyy-MM-dd} ({group.Count()} issues)</summary>
  <section>
    <table>");

                foreach (var issue in group)
                {
                    string summary = HttpUtility.HtmlEncode(issue.Summary ?? "");
                    string key = issue.Key;
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
                                iconPath = $"<img src='data:image/png;base64,{base64}' width='20' height='20' />";
                            }
                            catch { }
                        }
                    }

                    sb.AppendLine($"<tr><td><a href=\"#\" data-key=\"{key}\">{iconPath} {summary} [{key}]</a></td></tr>");
                }

                sb.AppendLine("</table></section></details>");
            }

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

            // --- Step 5: Show the final HTML ---
            string tempFilePath = Path.Combine(tempFolder, "monovera_updated.html");
            File.WriteAllText(tempFilePath, html);
            webView.CoreWebView2.Navigate(tempFilePath);
        }


        /// <summary>
        /// Checks if a Jira issue has had its summary or description changed in the last 30 days.
        /// This is used to filter issues for the "Recently Updated" tab.
        /// </summary>
        /// <param name="issueKey">The Jira issue key (e.g. "REQ-123").</param>
        /// <returns>
        /// True if the issue's summary or description was changed in the last 30 days; otherwise, false.
        /// </returns>
        private async Task<bool> HasSummaryOrDescriptionChangeAsync(string issueKey)
        {
            // Build the REST API URL to fetch issue changelog
            var url = $"{jiraBaseUrl}/rest/api/3/issue/{issueKey}?expand=changelog";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{jiraEmail}:{jiraToken}")));

            // Request the issue data from Jira
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return false;

            // Parse the JSON response
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            // Look for changelog histories
            if (doc.RootElement.TryGetProperty("changelog", out var changelog) &&
                changelog.TryGetProperty("histories", out var histories))
            {
                foreach (var history in histories.EnumerateArray())
                {
                    // Check if the change was made in the last 30 days
                    if (history.TryGetProperty("created", out var createdProp) &&
                        DateTime.TryParse(createdProp.GetString(), out var created) &&
                        created >= DateTime.UtcNow.AddDays(-30))
                    {
                        // Check if the change was to summary or description
                        if (history.TryGetProperty("items", out var items))
                        {
                            foreach (var item in items.EnumerateArray())
                            {
                                if (item.TryGetProperty("field", out var fieldName))
                                {
                                    string field = fieldName.GetString();
                                    if (field == "summary" || field == "description")
                                        return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
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
            if (suppressAfterSelect)
                return;

            // Prevent tab loading on right-click selection
            if (lastTreeMouseButton == MouseButtons.Right)
                return;

            if (e.Node?.Tag is not string issueKey || string.IsNullOrWhiteSpace(issueKey))
                return;


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

            TabPage pageTab = null;
            foreach (TabPage page in tabDetails.TabPages)
            {
                if (page.Text == issueKey)
                {
                    pageTab = page;
                    break;
                }
            }

            Microsoft.Web.WebView2.WinForms.WebView2 webView = null;

            if (pageTab == null)
            {
                // Tab does not exist, create and load it
                webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                await webView.EnsureCoreWebView2Async();
                string htmlFilePath = Path.Combine(tempFolder, $"LoadingHtml.html");
                File.WriteAllText(htmlFilePath, LoadingHtml);
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
            }
            else
            {
                tabDetails.SelectedTab = pageTab;
                // Always reload if Ctrl+LeftClick, even if tab is already focused
                if (isLeftClick && isCtrlPressed)
                {
                    // Always reload, even if tab is already focused
                    webView = pageTab.Controls.OfType<Microsoft.Web.WebView2.WinForms.WebView2>().FirstOrDefault();
                    if (webView == null)
                    {
                        webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
                        await webView.EnsureCoreWebView2Async();
                        pageTab.Controls.Clear();
                        pageTab.Controls.Add(webView);
                    }
                     string htmlFilePath = Path.Combine(tempFolder, $"LoadingHtml.html");
            File.WriteAllText(htmlFilePath, LoadingHtml);
            webView.CoreWebView2.Navigate(htmlFilePath);
                }
                else
                {
                    // Just focus the tab, do not reload
                    return;
                }
            }

            tabDetails.SelectedTab = pageTab;

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

                /**
                 * Build summary and issue information section
                 */
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

                /**
                * Build description sections
                */
                string descriptinHTML = "";
                if (root.TryGetProperty("renderedFields", out var renderedFields) &&
                    renderedFields.TryGetProperty("description", out var descProp) &&
                    descProp.ValueKind == JsonValueKind.String)
                {
                    descriptinHTML = descProp.GetString() ?? "";
                }
                string resolvedDescriptionHTML = ReplaceJiraLinksAndSVNFeatures(descriptinHTML);

                string encodedSummary = WebUtility.HtmlEncode(summary);
                string iconImg = string.IsNullOrEmpty(iconUrl) ? "" : $"<img src='{iconUrl}' style='height: 24px; vertical-align: middle; margin-right: 8px;'>";
                string headerLine = $"<h2>{iconImg}{encodedSummary} [{issueKey}]</h2>";

               /**
               * Build attachments section
               */
                string attachmentsHtml = BuildAttachmentsHtml(fields, issueKey);
                
                /**
                * Build links section
                */
                string linksHtml =
                             BuildLinksTable(fields, "Parent", hierarchyLinkTypeName.Split(",")[0].ToString(), "inwardIssue") +
                             BuildLinksTable(fields, "Children", hierarchyLinkTypeName.Split(",")[0].ToString(), "outwardIssue") +
                             BuildLinksTable(fields, "Related", "Relates", null);

                /**
                * Build history section
                */
                string historyHtml = BuildHistoryHtml(root);

               /**
               * Build JSON response section
               */
                string responseHTML = WebUtility.HtmlEncode(FormatJson(json));

                /**
                * Combine all sections in to one HTML detail page
                 */
                string tempFolder = Path.Combine(System.Windows.Forms.Application.StartupPath, "temp"); // or use Path.GetTempPath() if preferred
                string htmlFilePath = Path.Combine(tempFolder, $"{issueKey}.html");

                // Ensure the temp folder exists
                Directory.CreateDirectory(tempFolder);

                // Build the full HTML
                string html = BuildIssueDetailFullPageHtml(
                    headerLine, issueType, statusIcon, status,
                    createdDate, lastUpdated, issueUrl,
                    resolvedDescriptionHTML, attachmentsHtml,
                    linksHtml, historyHtml, responseHTML
                );

                // Write to file (overwrite if exists)
                File.WriteAllText(htmlFilePath, html);

                // Load into WebView2
                webView.Source = new Uri(htmlFilePath);

                webView.CoreWebView2.ScriptDialogOpening += (s, args) =>
                {
                    var deferral = args.GetDeferral();
                    try { args.Accept(); } finally { deferral.Complete(); }
                };

                webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                tree.SelectedNode = e.Node;
                e.Node.EnsureVisible();
                tree.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not connect to fetch the information you requested.\nPlease check your connection and other settings are ok.\n{ex.Message}", "Could not connect!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string BuildAttachmentsHtml(JsonElement fields, string issueKey)
        {
            if (!fields.TryGetProperty("attachment", out var attachments) || attachments.ValueKind != JsonValueKind.Array || attachments.GetArrayLength() == 0)
                return "<div class='no-attachments'>No attachments found.</div>";

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

        private string BuildLinksTable(JsonElement fields, string title, string linkType, string prop)
        {
            var sb = new StringBuilder();
            int matchCount = 0;

            sb.AppendLine($"<div class='subsection'><h4>{HttpUtility.HtmlEncode(title)}</h4>");

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

                            TreeNode foundNode = FindNodeByKey(tree.Nodes, key, false);
                            string iconImgInner = "";
                            if (foundNode != null &&
                                !string.IsNullOrEmpty(foundNode.ImageKey) &&
                                tree.ImageList != null &&
                                tree.ImageList.Images.ContainsKey(foundNode.ImageKey))
                            {
                                using var ms = new MemoryStream();
                                tree.ImageList.Images[foundNode.ImageKey].Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                var base64 = Convert.ToBase64String(ms.ToArray());
                                iconImgInner = $"<img src='data:image/png;base64,{base64}' style='height:24px; width:24px; vertical-align:middle; margin-right:8px; border-radius:4px; background:#e8f5e9;' />";
                            }
                            else
                            {
                                // Use green square emoji if no image
                                iconImgInner = "<span style='font-size:22px; vertical-align:middle; margin-right:8px;'>🟩</span>";
                            }

                            tableRows.AppendLine($@"
<tr>
    <td style='width:36px;'>{iconImgInner}</td>
    <td>
        <a href='#' data-key='{HttpUtility.HtmlEncode(key)}'>
            {HttpUtility.HtmlEncode(sum)} [{HttpUtility.HtmlEncode(key)}]
        </a>
    </td>
</tr>");
                            matchCount++;
                        }
                    }
                }

                if (matchCount > 0)
                {
                    sb.AppendLine(@"
<table style='width:100%; border-collapse:collapse; margin-bottom:10px;'>
    <tbody>");
                    sb.Append(tableRows);
                    sb.AppendLine("</tbody></table>");
                }
                else
                {
                    sb.AppendLine($"<div class='no-links'>No {HttpUtility.HtmlEncode(title)} issues found.</div>");
                }
            }
            else
            {
                sb.AppendLine($"<div class='no-links'>No {HttpUtility.HtmlEncode(title)} issues found.</div>");
            }

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        public static string BuildHistoryHtml(JsonElement root)
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
    <td><input type='checkbox' class='compare-check' value='{changeId}' data-from='{fromEsc}' data-to='{toEsc}' data-field='{HttpUtility.HtmlEncode(field)}'></td>
    <td>{createdStr}<br><small>{timeStr}</small></td>
    <td>{HttpUtility.HtmlEncode(author)}</td>
    <td>{fieldIcon} <strong>{HttpUtility.HtmlEncode(field)}</strong></td>
    <td class='from-val'>{HttpUtility.HtmlEncode(from)}</td>
    <td class='to-val'>{HttpUtility.HtmlEncode(to)}</td>
</tr>");
                    changeId++;
                }
            }

            var sb = new StringBuilder();

            sb.AppendLine(@"
<div class='filter-section'>
    <label>Date: <input type='date' id='filterDate'></label>
    <label>User: <input type='text' id='filterUser'></label>
    <label>Type: <input type='text' id='filterField'></label>
    <button onclick='applyFilters()'>✅ Apply Filter</button>
    <button onclick='clearFilters()'>❌ Clear</button>
    <button onclick='viewSelectedDiff()'>🔍 Show Difference</button>
</div>

<table class='history-table'>
    <thead>
        <tr>
            <th></th>
            <th>Date</th>
            <th>User</th>
            <th>Type</th>
            <th>Before</th>
            <th>After</th>
        </tr>
    </thead>
    <tbody>
        " + string.Join("\n", changes) + @"
    </tbody>
</table>

<div class='diff-overlay' id='diffOverlay' style='display:none'>
    <div class='diff-close' onclick=""document.getElementById('diffOverlay').style.display='none'"">✖</div>
    <h3 id='diffTitle'></h3>
    <div class='diff-columns'>
        <div><h4>Older Version</h4><div id='diffFrom'></div></div>
        <div><h4>New Version</h4><div id='diffTo'></div></div>
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

    let htmlFrom = escapeHtml(commonPrefix);
    if(fromDeleted.length > 0) {
        htmlFrom += `<span class='diff-deleted'>${escapeHtml(fromDeleted)}</span>`;
    }

    let htmlTo = escapeHtml(commonPrefix);
    if(toAdded.length > 0) {
        htmlTo += `<span class='diff-added'>${escapeHtml(toAdded)}</span>`;
    }

    return { htmlFrom, htmlTo };
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

    document.querySelectorAll('.history-table tbody tr').forEach(row => {
        const matchesDate = !date || row.dataset.date === date;
        const matchesUser = !user || row.dataset.user.toLowerCase().includes(user);
        const matchesField = !field || row.dataset.field.toLowerCase().includes(field);
        row.style.display = (matchesDate && matchesUser && matchesField) ? '' : 'none';
    });
}

function clearFilters() {
    document.getElementById('filterDate').value = '';
    document.getElementById('filterUser').value = '';
    document.getElementById('filterField').value = '';
    applyFilters();
}
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
</body>
</html>";
        }


        /// <summary>
        /// Generates a simple HTML diff between two strings, highlighting added and removed text.
        /// Finds the common prefix, then marks removed text from the original and added text from the new value.
        /// Used for inline change visualization in history views.
        /// </summary>
        /// <param name="from">The original string value (may be null).</param>
        /// <param name="to">The new string value (may be null).</param>
        /// <returns>
        /// HTML string showing removed and added text, or empty string if values are identical.
        /// </returns>
        private static string DiffText(string? from, string? to)
        {
            if (from == to) return "";

            from ??= "";
            to ??= "";

            // Function to remove all {color} tags
            string StripColorTags(string text) =>
                Regex.Replace(text, @"\{color(:[^}]*)?\}", "", RegexOptions.IgnoreCase);

            // Strip color tags for clean comparison
            string fromClean = StripColorTags(from);
            string toClean = StripColorTags(to);

            if (fromClean == toClean)
                return ""; // Only formatting changed

            var diff = new StringBuilder();
            int minLen = Math.Min(fromClean.Length, toClean.Length);
            int i = 0;

            // Find common prefix
            while (i < minLen && fromClean[i] == toClean[i])
            {
                i++;
            }

            // Deleted part (from)
            if (i < fromClean.Length)
            {
                var deleted = WebUtility.HtmlEncode(fromClean.Substring(i));
                diff.Append($@"<br/><br/>Removed <br/><span class='diff-deleted'>{deleted}</span>");
            }

            // Added part (to)
            if (i < toClean.Length)
            {
                var added = WebUtility.HtmlEncode(toClean.Substring(i));
                diff.Append($@"<br/><br/>Added <br/><span class='diff-added'>{added}</span>");
            }

            return diff.ToString();
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

            // Handle complex nested Jira macro links
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

            // Replace [svn://...feature|...] style SVN links
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

        /// <summary>
        /// Selects and loads a tree node by its Jira issue key.
        /// If the node is found, it is selected, made visible, and focused in the tree view.
        /// Used for navigation from WebView2 messages and other UI actions.
        /// </summary>
        /// <param name="key">The Jira issue key to select (e.g. "REQ-123").</param>
        private void SelectAndLoadTreeNode(string key)
        {
            var node = FindNodeByKey(tree.Nodes, key);
            if (node != null)
            {
                tree.SelectedNode = node;
                node.EnsureVisible();
                tree.Focus();                      // Return focus to tree
                tree.SelectedNode = node;          // Restore visual highlight
            }
        }

        private TreeNode FindNodeByKey(TreeNodeCollection nodes, string key, bool showMessage = true)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag?.ToString() == key)
                    return node;

                var child = FindNodeByKey(node.Nodes, key, false);
                if (child != null)
                    return child;
            }

            if (!key.ToLower().StartsWith("recent updates") && !key.ToLower().StartsWith("welcome to") && showMessage)
            {
                ShowTrayNotification(key);
            }

            return null;
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
            var selectedTab = tabDetails.SelectedTab;
            if (selectedTab == null) return;

            string issueKey = selectedTab.Text;
            if (string.IsNullOrEmpty(issueKey)) return;
            SelectAndLoadTreeNode(issueKey);
            //SelectTreeNodeByKey(issueKey);
        }

        /// <summary>
        /// Handles the click event for the "Update Hierarchy" menu item.
        /// Prompts the user for confirmation, then triggers a full hierarchy sync if confirmed.
        /// </summary>
        /// <param name="sender">The menu item clicked.</param>
        /// <param name="e">Event arguments.</param>
        private void updateHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to update the hierarchy?\nThis will take some time depending on your network bandwidth.\n\nAre you sure you want to continue?",
                "Update Hierarchy",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result == DialogResult.Yes)
            {
                SyncHierarchy();
            }
        }

        /// <summary>
        /// Performs a full hierarchy sync by reloading all Jira projects and issues from the server.
        /// Forces a fresh sync, bypassing any cached data.
        /// </summary>
        private async void SyncHierarchy()
        {
            await LoadAllProjectsToTreeAsync(true);
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

        private void AddChildNodesRecursively(TreeNode parentNode, string parentKey, JiraProjectConfig projectConfig)
        {
            if (childrenByParent.TryGetValue(parentKey, out var children))
            {
                string sortingField = projectConfig?.SortingField ?? "summary";

                var sortedChildren = children.OrderBy(child =>
                {
                    if (issueDtoDict.TryGetValue(child.Key, out var dto))
                    {
                        return dto.SortingField ?? "";
                    }
                    return child.Summary ?? "";
                }, new AlphanumericComparer()).ToList();

                foreach (var child in sortedChildren)
                {
                    var childNode = CreateTreeNode(child);
                    AddChildNodesRecursively(childNode, child.Key, projectConfig);
                    parentNode.Nodes.Add(childNode);
                }
            }
        }

        /// <summary>
        /// Helper to get the value of a field for sorting.
        /// Supports standard fields and custom fields if present in JiraIssueDto.
        /// </summary>
        /// <param name="issueKey">The Jira issue key.</param>
        /// <param name="fieldName">The field name to retrieve (case-insensitive).</param>
        /// <returns>The value of the field, or an empty string if not found.</returns>
        private object GetFieldValue(string issueKey, string fieldName)
        {
            // Try to get the JiraIssueDto for this key
            if (issueDtoDict.TryGetValue(issueKey, out var dto))
            {
                // Use reflection for custom fields if needed
                var prop = typeof(JiraIssueDto).GetProperty(fieldName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                    return prop.GetValue(dto) ?? "";
                // For custom fields, you may need to parse from a dictionary or JSON
                // If you store custom fields in a dictionary, add logic here
            }
            // Fallback to summary from issueDict
            if (issueDict.TryGetValue(issueKey, out var issue))
                return issue.Summary ?? "";
            return "";
        }

        // In your frmMain constructor or initialization method:
        private void InitializeTabContextMenu()
        {
            tabContextMenu = new ContextMenuStrip
            {
                ShowImageMargin = true, // Make sure image space is shown
                ImageScalingSize = new Size(24, 24) // Increase image space if needed
            };

            //tabContextMenu.Items.Add("Edit", null, (s, e) => EditCurrentIssue());
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close This Tab", CreateIconFromUnicode("❌"), (s, e) => CloseTab(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close All Other Tabs", CreateIconFromUnicode("🔀"), (s, e) => CloseAllOtherTabs(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close Tabs on Left", CreateIconFromUnicode("⬅️"), (s, e) => CloseTabsOnLeft(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close Tabs on Right", CreateIconFromUnicode("➡️"), (s, e) => CloseTabsOnRight(rightClickedTab)) { ImageScaling = ToolStripItemImageScaling.None });
            tabContextMenu.Items.Add(new ToolStripMenuItem("Close All Tabs", CreateIconFromUnicode("🗑"), (s, e) => CloseAllTabs()) { ImageScaling = ToolStripItemImageScaling.None });

            tabDetails.MouseUp += TabDetails_MouseUp;

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
            tabDetails.TabPages.Remove(tab);

            // Select the tab to the left, if any; otherwise, select the first tab if any remain
            if (tabDetails.TabPages.Count > 0)
            {
                int newIdx = Math.Max(0, idx - 1);
                tabDetails.SelectedTab = tabDetails.TabPages[newIdx];
            }
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
            // Prevent drag if dragTabIndex is out of bounds (can happen after tab close)
            if (e.Button == MouseButtons.Left && dragTabIndex != -1 && dragTabIndex < tabDetails.TabPages.Count)
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
            TabPage draggedTab = (TabPage)e.Data.GetData(typeof(TabPage));
            Point pt = tabDetails.PointToClient(new Point(e.X, e.Y));
            for (int i = 0; i < tabDetails.TabCount; i++)
            {
                if (tabDetails.GetTabRect(i).Contains(pt))
                {
                    tabDetails.TabPages.Remove(draggedTab);
                    tabDetails.TabPages.Insert(i, draggedTab);
                    tabDetails.SelectedTab = draggedTab;
                    break;
                }
            }
            dragTabIndex = -1;
        }

        private void mnuReport_Click(object sender, EventArgs e)
        {
            GenerateReport();
        }

        private void mnuSearch_Click(object sender, EventArgs e)
        {
            ShowSearchDialog(tree);
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
    }
}
