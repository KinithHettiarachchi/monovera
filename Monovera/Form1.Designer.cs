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
            splitContainer1 = new SplitContainer();
            tree = new TreeView();
            statusStrip1 = new StatusStrip();
            pbProgress = new ToolStripProgressBar();
            lblProgress = new ToolStripStatusLabel();
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
            splitContainer1.Panel2.Controls.Add(statusStrip1);
            splitContainer1.Size = new Size(1904, 1001);
            splitContainer1.SplitterDistance = 517;
            splitContainer1.TabIndex = 0;
            // 
            // tree
            // 
            tree.Dock = DockStyle.Fill;
            tree.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            tree.Location = new Point(0, 0);
            tree.Name = "tree";
            tree.Size = new Size(517, 1001);
            tree.TabIndex = 0;
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { pbProgress, lblProgress });
            statusStrip1.Location = new Point(0, 979);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1383, 22);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // pbProgress
            // 
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(100, 16);
            // 
            // lblProgress
            // 
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(16, 17);
            lblProgress.Text = "...";
            // 
            // frmMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1904, 1001);
            Controls.Add(splitContainer1);
            Name = "frmMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Monovera";
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
    }
}
