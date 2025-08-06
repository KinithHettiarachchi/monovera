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
            mnuSettings = new ToolStripSplitButton();
            mnuUpdateHierarchy = new ToolStripMenuItem();
            mnuConfiguration = new ToolStripMenuItem();
            pbProgress = new ToolStripProgressBar();
            lblProgress = new ToolStripStatusLabel();
            lblShortcuts = new ToolStripStatusLabel();
            toolTip1 = new ToolTip(components);
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
            splitContainer1.Panel2.Controls.Add(panelTabs);
            splitContainer1.Panel2.Controls.Add(statusStrip1);
            splitContainer1.Size = new Size(1904, 1001);
            splitContainer1.SplitterDistance = 670;
            splitContainer1.TabIndex = 0;
            // 
            // tree
            // 
            tree.BackColor = Color.White;
            tree.Dock = DockStyle.Fill;
            tree.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            tree.Location = new Point(0, 0);
            tree.Name = "tree";
            tree.Size = new Size(670, 1001);
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
            panelTabs.Size = new Size(1230, 978);
            panelTabs.TabIndex = 1;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { lblUser, mnuActions, mnuSettings, pbProgress, lblProgress, lblShortcuts });
            statusStrip1.Location = new Point(0, 978);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.RenderMode = ToolStripRenderMode.Professional;
            statusStrip1.Size = new Size(1230, 23);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // lblUser
            // 
            lblUser.Font = new Font("Segoe UI", 9.75F);
            lblUser.Name = "lblUser";
            lblUser.Size = new Size(152, 18);
            lblUser.Text = "    ❌ Not connected!    ";
            // 
            // mnuActions
            // 
            mnuActions.DropDownItems.AddRange(new ToolStripItem[] { mnuRead, mnuReport, mnuRecentUpdates, mnuSearch });
            mnuActions.Font = new Font("Segoe UI", 9.75F);
            mnuActions.ImageTransparentColor = Color.Magenta;
            mnuActions.Name = "mnuActions";
            mnuActions.Size = new Size(63, 21);
            mnuActions.Text = "Actions";
            // 
            // mnuRead
            // 
            mnuRead.Font = new Font("Segoe UI", 9.75F);
            mnuRead.Name = "mnuRead";
            mnuRead.ShortcutKeys = Keys.Control | Keys.R;
            mnuRead.Size = new Size(226, 22);
            mnuRead.Text = "Read out loud...";
            mnuRead.Click += menuRead_Click;
            // 
            // mnuReport
            // 
            mnuReport.Font = new Font("Segoe UI", 9.75F);
            mnuReport.Name = "mnuReport";
            mnuReport.ShortcutKeys = Keys.Control | Keys.P;
            mnuReport.Size = new Size(226, 22);
            mnuReport.Text = "Generate Report...";
            mnuReport.Click += mnuReport_Click;
            // 
            // mnuRecentUpdates
            // 
            mnuRecentUpdates.Name = "mnuRecentUpdates";
            mnuRecentUpdates.ShortcutKeys = Keys.Control | Keys.N;
            mnuRecentUpdates.Size = new Size(226, 22);
            mnuRecentUpdates.Text = "Recent Updates...";
            mnuRecentUpdates.Click += mnuRecentUpdates_Click;
            // 
            // mnuSearch
            // 
            mnuSearch.Font = new Font("Segoe UI", 9.75F);
            mnuSearch.Name = "mnuSearch";
            mnuSearch.ShortcutKeys = Keys.Control | Keys.Q;
            mnuSearch.Size = new Size(226, 22);
            mnuSearch.Text = "Search...";
            mnuSearch.Click += mnuSearch_Click;
            // 
            // mnuSettings
            // 
            mnuSettings.DropDownItems.AddRange(new ToolStripItem[] { mnuUpdateHierarchy, mnuConfiguration });
            mnuSettings.Font = new Font("Segoe UI", 9.75F);
            mnuSettings.ImageTransparentColor = Color.Magenta;
            mnuSettings.Name = "mnuSettings";
            mnuSettings.Size = new Size(70, 21);
            mnuSettings.Text = "Settings";
            // 
            // mnuUpdateHierarchy
            // 
            mnuUpdateHierarchy.Font = new Font("Segoe UI", 9.75F);
            mnuUpdateHierarchy.Name = "mnuUpdateHierarchy";
            mnuUpdateHierarchy.Size = new Size(185, 22);
            mnuUpdateHierarchy.Text = "Update hierarchy...";
            mnuUpdateHierarchy.Click += updateHierarchyToolStripMenuItem_Click;
            // 
            // mnuConfiguration
            // 
            mnuConfiguration.Font = new Font("Segoe UI", 9.75F);
            mnuConfiguration.Name = "mnuConfiguration";
            mnuConfiguration.Size = new Size(185, 22);
            mnuConfiguration.Text = "Configuration...";
            mnuConfiguration.Click += configurationToolStripMenuItem_Click;
            // 
            // pbProgress
            // 
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(100, 17);
            // 
            // lblProgress
            // 
            lblProgress.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(24, 18);
            lblProgress.Text = "    ";
            // 
            // lblShortcuts
            // 
            lblShortcuts.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblShortcuts.ForeColor = SystemColors.ControlDarkDark;
            lblShortcuts.Name = "lblShortcuts";
            lblShortcuts.Size = new Size(65, 18);
            lblShortcuts.Text = "Welcome!";
            // 
            // toolTip1
            // 
            toolTip1.ToolTipIcon = ToolTipIcon.Info;
            // 
            // frmMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1904, 1001);
            Controls.Add(splitContainer1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "frmMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "M O N O V E R A ";
            WindowState = FormWindowState.Maximized;
            Load += frmMain_Load;
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            splitContainer1.Panel2.PerformLayout();
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
    }
}
