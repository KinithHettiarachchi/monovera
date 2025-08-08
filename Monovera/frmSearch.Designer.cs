namespace Monovera
{
    partial class frmSearch
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnSearch;
        private System.Windows.Forms.ComboBox cmbType;
        private System.Windows.Forms.ComboBox cmbStatus;
        private System.Windows.Forms.Label lblType;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.WebBrowser webBrowserFallback; // Optional fallback
        private Microsoft.Web.WebView2.WinForms.WebView2 webViewResults;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.Button btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmSearch));
            txtSearch = new TextBox();
            btnSearch = new Button();
            cmbType = new ComboBox();
            cmbStatus = new ComboBox();
            lblType = new Label();
            lblStatus = new Label();
            webViewResults = new Microsoft.Web.WebView2.WinForms.WebView2();
            topPanel = new Panel();
            chkJQL = new CheckBox();
            lblProgress = new Label();
            pbProgress = new ProgressBar();
            cmbProject = new ComboBox();
            lblProject = new Label();
            btnClose = new Button();
            pnlSearch = new Panel();
            ((System.ComponentModel.ISupportInitialize)webViewResults).BeginInit();
            topPanel.SuspendLayout();
            pnlSearch.SuspendLayout();
            SuspendLayout();
            // 
            // txtSearch
            // 
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearch.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            txtSearch.Location = new Point(76, 14);
            txtSearch.Name = "txtSearch";
            txtSearch.PlaceholderText = "Enter issue key or search text...";
            txtSearch.Size = new Size(582, 33);
            txtSearch.TabIndex = 0;
            // 
            // btnSearch
            // 
            btnSearch.Anchor = AnchorStyles.Top;
            btnSearch.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            btnSearch.Location = new Point(1480, 11);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(113, 37);
            btnSearch.TabIndex = 1;
            btnSearch.Text = "🔍 Search";
            btnSearch.UseVisualStyleBackColor = true;
            // 
            // cmbType
            // 
            cmbType.Anchor = AnchorStyles.Top;
            cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbType.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            cmbType.Location = new Point(939, 14);
            cmbType.Name = "cmbType";
            cmbType.Size = new Size(231, 33);
            cmbType.TabIndex = 2;
            // 
            // cmbStatus
            // 
            cmbStatus.Anchor = AnchorStyles.Top;
            cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStatus.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            cmbStatus.Location = new Point(1248, 14);
            cmbStatus.Name = "cmbStatus";
            cmbStatus.Size = new Size(216, 33);
            cmbStatus.TabIndex = 3;
            // 
            // lblType
            // 
            lblType.Anchor = AnchorStyles.Top;
            lblType.AutoSize = true;
            lblType.Font = new Font("Segoe UI", 12F);
            lblType.Location = new Point(894, 20);
            lblType.Name = "lblType";
            lblType.Size = new Size(42, 21);
            lblType.TabIndex = 4;
            lblType.Text = "Type";
            // 
            // lblStatus
            // 
            lblStatus.Anchor = AnchorStyles.Top;
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI", 12F);
            lblStatus.Location = new Point(1194, 21);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(52, 21);
            lblStatus.TabIndex = 5;
            lblStatus.Text = "Status";
            // 
            // webViewResults
            // 
            webViewResults.AllowExternalDrop = true;
            webViewResults.BackColor = Color.White;
            webViewResults.CreationProperties = null;
            webViewResults.DefaultBackgroundColor = Color.White;
            webViewResults.Dock = DockStyle.Fill;
            webViewResults.Location = new Point(0, 70);
            webViewResults.Name = "webViewResults";
            webViewResults.Size = new Size(1605, 675);
            webViewResults.TabIndex = 0;
            webViewResults.ZoomFactor = 1D;
            // 
            // topPanel
            // 
            topPanel.BackColor = Color.White;
            topPanel.Controls.Add(lblProgress);
            topPanel.Controls.Add(pbProgress);
            topPanel.Controls.Add(cmbProject);
            topPanel.Controls.Add(lblProject);
            topPanel.Controls.Add(txtSearch);
            topPanel.Controls.Add(btnSearch);
            topPanel.Controls.Add(cmbType);
            topPanel.Controls.Add(cmbStatus);
            topPanel.Controls.Add(lblType);
            topPanel.Controls.Add(lblStatus);
            topPanel.Controls.Add(btnClose);
            topPanel.Controls.Add(pnlSearch);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(1605, 70);
            topPanel.TabIndex = 1;
            // 
            // chkJQL
            // 
            chkJQL.AutoSize = true;
            chkJQL.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            chkJQL.Location = new Point(10, 17);
            chkJQL.Name = "chkJQL";
            chkJQL.Size = new Size(55, 25);
            chkJQL.TabIndex = 11;
            chkJQL.Text = "JQL";
            chkJQL.UseVisualStyleBackColor = true;
            // 
            // lblProgress
            // 
            lblProgress.AutoSize = true;
            lblProgress.Location = new Point(1480, 53);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(16, 15);
            lblProgress.TabIndex = 10;
            lblProgress.Text = "...";
            // 
            // pbProgress
            // 
            pbProgress.Location = new Point(12, 56);
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(1462, 5);
            pbProgress.TabIndex = 9;
            // 
            // cmbProject
            // 
            cmbProject.Anchor = AnchorStyles.Top;
            cmbProject.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProject.Font = new Font("Segoe UI", 14.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            cmbProject.Location = new Point(732, 15);
            cmbProject.Name = "cmbProject";
            cmbProject.Size = new Size(141, 33);
            cmbProject.TabIndex = 1;
            // 
            // lblProject
            // 
            lblProject.Anchor = AnchorStyles.Top;
            lblProject.AutoSize = true;
            lblProject.Font = new Font("Segoe UI", 12F);
            lblProject.Location = new Point(672, 21);
            lblProject.Name = "lblProject";
            lblProject.Size = new Size(58, 21);
            lblProject.TabIndex = 8;
            lblProject.Text = "Project";
            // 
            // btnClose
            // 
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Font = new Font("Segoe UI", 9F);
            btnClose.Location = new Point(2285, 11);
            btnClose.Name = "btnClose";
            btnClose.Size = new Size(75, 27);
            btnClose.TabIndex = 6;
            btnClose.Text = "Close";
            btnClose.UseVisualStyleBackColor = true;
            // 
            // pnlSearch
            // 
            pnlSearch.BorderStyle = BorderStyle.Fixed3D;
            pnlSearch.Controls.Add(chkJQL);
            pnlSearch.Dock = DockStyle.Fill;
            pnlSearch.Location = new Point(0, 0);
            pnlSearch.Name = "pnlSearch";
            pnlSearch.Size = new Size(1605, 70);
            pnlSearch.TabIndex = 12;
            // 
            // frmSearch
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1605, 745);
            Controls.Add(webViewResults);
            Controls.Add(topPanel);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(600, 400);
            Name = "frmSearch";
            Opacity = 0.95D;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "S E A R C H";
            ((System.ComponentModel.ISupportInitialize)webViewResults).EndInit();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            pnlSearch.ResumeLayout(false);
            pnlSearch.PerformLayout();
            ResumeLayout(false);
        }
        private ComboBox cmbProject;
        private Label lblProject;
        private ProgressBar pbProgress;
        private Label lblProgress;
        private CheckBox chkJQL;
        private Panel pnlSearch;
    }
}
