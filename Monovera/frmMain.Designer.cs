namespace Monovera
{
    partial class frmMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            splitContainer1 = new SplitContainer();
            tree = new TreeView();
            panelTabs = new Panel();
            statusStrip1 = new StatusStrip();
            lblUser = new ToolStripStatusLabel();
            mnuActions = new ToolStripDropDownButton();
            mnuRead = new ToolStripMenuItem();
            mnuReport = new ToolStripMenuItem();
            mnuRecentUpdates = new ToolStripMenuItem();
            mnuSearch = new ToolStripMenuItem();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            mnuAI = new ToolStripSplitButton();
            mnuAITestCases = new ToolStripMenuItem();
            mnuPutMeInContext = new ToolStripMenuItem();
            toolStripSeparator1 = new ToolStripSeparator();
            toolStripStatusLabel2 = new ToolStripStatusLabel();
            mnuSettings = new ToolStripSplitButton();
            mnuUpdateHierarchy = new ToolStripMenuItem();
            mnuConfiguration = new ToolStripMenuItem();
            toolStripStatusLabel3 = new ToolStripStatusLabel();
            pbProgress = new ToolStripProgressBar();
            lblProgress = new ToolStripStatusLabel();
            toolStripStatusLabel4 = new ToolStripStatusLabel();
            lblJiraUpdateProcessing = new ToolStripStatusLabel();
            toolStripStatusLabel5 = new ToolStripStatusLabel();
            lblSyncStatus = new ToolStripStatusLabel();
            toolStripStatusLabel7 = new ToolStripStatusLabel();
            lblShortcuts = new ToolStripStatusLabel();
            toolTip1 = new ToolTip(components);
            mnuOllama = new ToolStripMenuItem();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer1
            // 
            splitContainer1.BackColor = Color.White;
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(tree);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.BackColor = Color.White;
            splitContainer1.Panel2.Controls.Add(panelTabs);
            splitContainer1.Size = new Size(1904, 967);
            splitContainer1.SplitterDistance = 540;
            splitContainer1.TabIndex = 0;
            // 
            // tree
            // 
            tree.BackColor = Color.White;
            tree.Dock = DockStyle.Fill;
            tree.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            tree.Location = new Point(0, 0);
            tree.Name = "tree";
            tree.ShowNodeToolTips = true;
            tree.Size = new Size(540, 967);
            tree.TabIndex = 0;
            tree.AfterSelect += Tree_AfterSelect;
            tree.NodeMouseClick += tree_NodeMouseClick;
            // 
            // panelTabs
            // 
            panelTabs.BackColor = Color.White;
            panelTabs.Dock = DockStyle.Fill;
            panelTabs.Location = new Point(0, 0);
            panelTabs.Name = "panelTabs";
            panelTabs.Size = new Size(1360, 967);
            panelTabs.TabIndex = 1;
            // 
            // statusStrip1
            // 
            statusStrip1.AutoSize = false;
            statusStrip1.Items.AddRange(new ToolStripItem[] { lblUser, mnuActions, toolStripStatusLabel1, mnuAI, toolStripStatusLabel2, mnuSettings, toolStripStatusLabel3, pbProgress, lblProgress, toolStripStatusLabel4, lblJiraUpdateProcessing, toolStripStatusLabel5, lblSyncStatus, toolStripStatusLabel7, lblShortcuts });
            statusStrip1.Location = new Point(0, 967);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.RenderMode = ToolStripRenderMode.Professional;
            statusStrip1.Size = new Size(1904, 34);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // lblUser
            // 
            lblUser.Font = new Font("Microsoft Sans Serif", 9.75F);
            lblUser.Name = "lblUser";
            lblUser.Size = new Size(136, 29);
            lblUser.Text = "    ❌ Not connected!    ";
            // 
            // mnuActions
            // 
            mnuActions.DropDownItems.AddRange(new ToolStripItem[] { mnuRead, mnuReport, mnuRecentUpdates, mnuSearch });
            mnuActions.Font = new Font("Microsoft Sans Serif", 9.75F);
            mnuActions.ImageTransparentColor = Color.Magenta;
            mnuActions.Name = "mnuActions";
            mnuActions.Size = new Size(64, 32);
            mnuActions.Text = "Actions";
            // 
            // mnuRead
            // 
            mnuRead.Font = new Font("Segoe UI", 9.75F);
            mnuRead.Name = "mnuRead";
            mnuRead.ShortcutKeys = Keys.Control | Keys.R;
            mnuRead.Size = new Size(225, 22);
            mnuRead.Text = "Read out loud...";
            mnuRead.Click += menuRead_Click;
            // 
            // mnuReport
            // 
            mnuReport.Font = new Font("Segoe UI", 9.75F);
            mnuReport.Name = "mnuReport";
            mnuReport.ShortcutKeys = Keys.Control | Keys.P;
            mnuReport.Size = new Size(225, 22);
            mnuReport.Text = "Generate Report...";
            mnuReport.Click += mnuReport_Click;
            // 
            // mnuRecentUpdates
            // 
            mnuRecentUpdates.Name = "mnuRecentUpdates";
            mnuRecentUpdates.ShortcutKeys = Keys.Control | Keys.N;
            mnuRecentUpdates.Size = new Size(225, 22);
            mnuRecentUpdates.Text = "Recent Updates...";
            mnuRecentUpdates.Click += mnuRecentUpdates_Click;
            // 
            // mnuSearch
            // 
            mnuSearch.Font = new Font("Segoe UI", 9.75F);
            mnuSearch.Name = "mnuSearch";
            mnuSearch.ShortcutKeys = Keys.Control | Keys.Q;
            mnuSearch.Size = new Size(225, 22);
            mnuSearch.Text = "Search...";
            mnuSearch.Click += mnuSearch_Click;
            // 
            // toolStripStatusLabel1
            // 
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(19, 29);
            toolStripStatusLabel1.Text = "    ";
            // 
            // mnuAI
            // 
            mnuAI.DropDownItems.AddRange(new ToolStripItem[] { mnuAITestCases, mnuPutMeInContext, toolStripSeparator1, mnuOllama });
            mnuAI.Font = new Font("Microsoft Sans Serif", 9.75F);
            mnuAI.ImageTransparentColor = Color.Magenta;
            mnuAI.Name = "mnuAI";
            mnuAI.Size = new Size(92, 32);
            mnuAI.Text = "AI Assistant";
            // 
            // mnuAITestCases
            // 
            mnuAITestCases.Name = "mnuAITestCases";
            mnuAITestCases.Size = new Size(291, 22);
            mnuAITestCases.Text = "Generate Test Cases (Experimental)";
            mnuAITestCases.Click += mnuAITestCases_Click;
            // 
            // mnuPutMeInContext
            // 
            mnuPutMeInContext.Name = "mnuPutMeInContext";
            mnuPutMeInContext.Size = new Size(291, 22);
            mnuPutMeInContext.Text = "Put Me In Context (Experimental)";
            mnuPutMeInContext.Click += mnuPutMeInContext_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(288, 6);
            // 
            // toolStripStatusLabel2
            // 
            toolStripStatusLabel2.Name = "toolStripStatusLabel2";
            toolStripStatusLabel2.Size = new Size(19, 29);
            toolStripStatusLabel2.Text = "    ";
            // 
            // mnuSettings
            // 
            mnuSettings.DropDownItems.AddRange(new ToolStripItem[] { mnuUpdateHierarchy, mnuConfiguration });
            mnuSettings.Font = new Font("Microsoft Sans Serif", 9.75F);
            mnuSettings.ImageTransparentColor = Color.Magenta;
            mnuSettings.Name = "mnuSettings";
            mnuSettings.Size = new Size(71, 32);
            mnuSettings.Text = "Settings";
            // 
            // mnuUpdateHierarchy
            // 
            mnuUpdateHierarchy.Font = new Font("Segoe UI", 9.75F);
            mnuUpdateHierarchy.Name = "mnuUpdateHierarchy";
            mnuUpdateHierarchy.Size = new Size(184, 22);
            mnuUpdateHierarchy.Text = "Update hierarchy...";
            mnuUpdateHierarchy.Click += updateHierarchyToolStripMenuItem_Click;
            // 
            // mnuConfiguration
            // 
            mnuConfiguration.Font = new Font("Segoe UI", 9.75F);
            mnuConfiguration.Name = "mnuConfiguration";
            mnuConfiguration.Size = new Size(184, 22);
            mnuConfiguration.Text = "Configuration...";
            mnuConfiguration.Click += configurationToolStripMenuItem_Click;
            // 
            // toolStripStatusLabel3
            // 
            toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            toolStripStatusLabel3.Size = new Size(19, 29);
            toolStripStatusLabel3.Text = "    ";
            // 
            // pbProgress
            // 
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(100, 28);
            // 
            // lblProgress
            // 
            lblProgress.Font = new Font("Microsoft Sans Serif", 9.75F);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(19, 29);
            lblProgress.Text = "    ";
            // 
            // toolStripStatusLabel4
            // 
            toolStripStatusLabel4.Name = "toolStripStatusLabel4";
            toolStripStatusLabel4.Size = new Size(19, 29);
            toolStripStatusLabel4.Text = "    ";
            // 
            // lblJiraUpdateProcessing
            // 
            lblJiraUpdateProcessing.Font = new Font("Microsoft Sans Serif", 9.75F);
            lblJiraUpdateProcessing.ForeColor = Color.Gray;
            lblJiraUpdateProcessing.Name = "lblJiraUpdateProcessing";
            lblJiraUpdateProcessing.Size = new Size(158, 29);
            lblJiraUpdateProcessing.Text = "Checking update queue...";
            // 
            // toolStripStatusLabel5
            // 
            toolStripStatusLabel5.Name = "toolStripStatusLabel5";
            toolStripStatusLabel5.Size = new Size(19, 29);
            toolStripStatusLabel5.Text = "    ";
            // 
            // lblSyncStatus
            // 
            lblSyncStatus.Font = new Font("Microsoft Sans Serif", 9.75F);
            lblSyncStatus.ForeColor = Color.Gray;
            lblSyncStatus.Name = "lblSyncStatus";
            lblSyncStatus.Size = new Size(141, 29);
            lblSyncStatus.Text = "Checking sync status...";
            // 
            // toolStripStatusLabel7
            // 
            toolStripStatusLabel7.Name = "toolStripStatusLabel7";
            toolStripStatusLabel7.Size = new Size(19, 29);
            toolStripStatusLabel7.Text = "    ";
            // 
            // lblShortcuts
            // 
            lblShortcuts.Font = new Font("Microsoft Sans Serif", 9.75F);
            lblShortcuts.ForeColor = SystemColors.ControlDarkDark;
            lblShortcuts.Name = "lblShortcuts";
            lblShortcuts.Size = new Size(68, 29);
            lblShortcuts.Text = "Welcome!";
            // 
            // toolTip1
            // 
            toolTip1.ToolTipIcon = ToolTipIcon.Info;
            // 
            // mnuOllama
            // 
            mnuOllama.Name = "mnuOllama";
            mnuOllama.Size = new Size(291, 22);
            mnuOllama.Text = "Ollama Querying";
            mnuOllama.Click += mnuOllama_Click;
            // 
            // frmMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1904, 1001);
            Controls.Add(splitContainer1);
            Controls.Add(statusStrip1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "frmMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "M O N O V E R A ";
            WindowState = FormWindowState.Maximized;
            Load += frmMain_Load;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitContainer1;
        private TreeView tree;
        private StatusStrip statusStrip1;
        private ToolStripProgressBar pbProgress;
        private ToolStripStatusLabel lblProgress;
        private Panel panelTabs;
        private ToolTip toolTip1;
        private ToolStripStatusLabel lblShortcuts;
        private ToolStripSplitButton mnuSettings;
        private ToolStripMenuItem mnuUpdateHierarchy;
        private ToolStripMenuItem mnuConfiguration;
        private ToolStripStatusLabel lblUser;
        private ToolStripDropDownButton mnuActions;
        private ToolStripMenuItem mnuSearch;
        private ToolStripMenuItem mnuReport;
        private ToolStripMenuItem mnuRead;
        private ToolStripMenuItem mnuRecentUpdates;
        private ToolStripStatusLabel lblJiraUpdateProcessing;
        private ToolStripSplitButton mnuAI;
        private ToolStripMenuItem mnuAITestCases;
        private ToolStripMenuItem mnuPutMeInContext;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private ToolStripStatusLabel toolStripStatusLabel2;
        private ToolStripStatusLabel toolStripStatusLabel3;
        private ToolStripStatusLabel toolStripStatusLabel4;
        private ToolStripStatusLabel toolStripStatusLabel5;
        private ToolStripStatusLabel toolStripStatusLabel7;
        private ToolStripStatusLabel lblSyncStatus;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripMenuItem mnuOllama;
    }
}
