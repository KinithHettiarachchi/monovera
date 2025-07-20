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
            menuActions = new ToolStripDropDownButton();
            mnuSearch = new ToolStripMenuItem();
            mnuReport = new ToolStripMenuItem();
            menuRead = new ToolStripMenuItem();
            mnuSettings = new ToolStripSplitButton();
            mnuUpdateHierarchy = new ToolStripMenuItem();
            configurationToolStripMenuItem = new ToolStripMenuItem();
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
            tree.BackColor = Color.Honeydew;
            tree.Dock = DockStyle.Fill;
            tree.Font = new Font("Calibri", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            tree.Location = new Point(0, 0);
            tree.Name = "tree";
            tree.Size = new Size(670, 1001);
            tree.TabIndex = 0;
            tree.AfterSelect += Tree_AfterSelect;
            // 
            // panelTabs
            // 
            panelTabs.Dock = DockStyle.Fill;
            panelTabs.Location = new Point(0, 0);
            panelTabs.Name = "panelTabs";
            panelTabs.Size = new Size(1230, 931);
            panelTabs.TabIndex = 1;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { lblUser, menuActions, mnuSettings, pbProgress, lblProgress, lblShortcuts });
            statusStrip1.Location = new Point(0, 931);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.RenderMode = ToolStripRenderMode.Professional;
            statusStrip1.Size = new Size(1230, 70);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // lblUser
            // 
            lblUser.Font = new Font("Calibri", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblUser.Name = "lblUser";
            lblUser.Size = new Size(113, 65);
            lblUser.Text = "    Not connected!    ";
            // 
            // menuActions
            // 
            menuActions.DropDownItems.AddRange(new ToolStripItem[] { mnuSearch, mnuReport, menuRead });
            menuActions.Image = Properties.Resources.Actions;
            menuActions.ImageScaling = ToolStripItemImageScaling.None;
            menuActions.ImageTransparentColor = Color.Magenta;
            menuActions.Name = "menuActions";
            menuActions.Size = new Size(124, 68);
            menuActions.Text = "Actions";
            // 
            // mnuSearch
            // 
            mnuSearch.Image = Properties.Resources.Search;
            mnuSearch.ImageScaling = ToolStripItemImageScaling.None;
            mnuSearch.Name = "mnuSearch";
            mnuSearch.ShortcutKeys = Keys.Control | Keys.Q;
            mnuSearch.Size = new Size(278, 70);
            mnuSearch.Text = "Search...";
            mnuSearch.Click += mnuSearch_Click;
            // 
            // mnuReport
            // 
            mnuReport.Image = Properties.Resources.Report;
            mnuReport.ImageScaling = ToolStripItemImageScaling.None;
            mnuReport.Name = "mnuReport";
            mnuReport.ShortcutKeys = Keys.Control | Keys.P;
            mnuReport.Size = new Size(278, 70);
            mnuReport.Text = "Generate Report...";
            mnuReport.Click += mnuReport_Click;
            // 
            // menuRead
            // 
            menuRead.Image = Properties.Resources.Speak;
            menuRead.ImageScaling = ToolStripItemImageScaling.None;
            menuRead.Name = "menuRead";
            menuRead.ShortcutKeys = Keys.Control | Keys.Shift | Keys.Z;
            menuRead.Size = new Size(278, 70);
            menuRead.Text = "Read out loud...";
            menuRead.Click += menuRead_Click;
            // 
            // mnuSettings
            // 
            mnuSettings.DropDownItems.AddRange(new ToolStripItem[] { mnuUpdateHierarchy, configurationToolStripMenuItem });
            mnuSettings.Image = Properties.Resources.settings;
            mnuSettings.ImageScaling = ToolStripItemImageScaling.None;
            mnuSettings.ImageTransparentColor = Color.Magenta;
            mnuSettings.Name = "mnuSettings";
            mnuSettings.Size = new Size(129, 68);
            mnuSettings.Text = "Settings";
            // 
            // mnuUpdateHierarchy
            // 
            mnuUpdateHierarchy.Font = new Font("Calibri", 12F);
            mnuUpdateHierarchy.Image = Properties.Resources.Update;
            mnuUpdateHierarchy.ImageScaling = ToolStripItemImageScaling.None;
            mnuUpdateHierarchy.Name = "mnuUpdateHierarchy";
            mnuUpdateHierarchy.Size = new Size(246, 70);
            mnuUpdateHierarchy.Text = "Update hierarchy...";
            mnuUpdateHierarchy.Click += updateHierarchyToolStripMenuItem_Click;
            // 
            // configurationToolStripMenuItem
            // 
            configurationToolStripMenuItem.Font = new Font("Calibri", 12F);
            configurationToolStripMenuItem.Image = Properties.Resources.Configuration;
            configurationToolStripMenuItem.ImageScaling = ToolStripItemImageScaling.None;
            configurationToolStripMenuItem.Name = "configurationToolStripMenuItem";
            configurationToolStripMenuItem.Size = new Size(246, 70);
            configurationToolStripMenuItem.Text = "Configuration...";
            configurationToolStripMenuItem.Click += configurationToolStripMenuItem_Click;
            // 
            // pbProgress
            // 
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(100, 64);
            // 
            // lblProgress
            // 
            lblProgress.Font = new Font("Calibri", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(19, 65);
            lblProgress.Text = "    ";
            // 
            // lblShortcuts
            // 
            lblShortcuts.Font = new Font("Calibri", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            lblShortcuts.ForeColor = SystemColors.ControlDarkDark;
            lblShortcuts.Name = "lblShortcuts";
            lblShortcuts.Size = new Size(62, 65);
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
        private ToolStripMenuItem configurationToolStripMenuItem;
        private ToolStripStatusLabel lblUser;
        private ToolStripDropDownButton menuActions;
        private ToolStripMenuItem mnuSearch;
        private ToolStripMenuItem mnuReport;
        private ToolStripMenuItem menuRead;
    }
}
