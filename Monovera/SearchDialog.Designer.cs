namespace Monovera
{
    partial class SearchDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SearchDialog));
            txtSearch = new TextBox();
            btnSearch = new Button();
            cmbType = new ComboBox();
            cmbStatus = new ComboBox();
            lblType = new Label();
            lblStatus = new Label();
            webViewResults = new Microsoft.Web.WebView2.WinForms.WebView2();
            topPanel = new Panel();
            lblProgress = new Label();
            pbProgress = new ProgressBar();
            cmbProject = new ComboBox();
            label1 = new Label();
            btnClose = new Button();
            ((System.ComponentModel.ISupportInitialize)webViewResults).BeginInit();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // txtSearch
            // 
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearch.Font = new Font("Segoe UI", 10F);
            txtSearch.Location = new Point(221, 13);
            txtSearch.Name = "txtSearch";
            txtSearch.PlaceholderText = "Enter issue key or search text...";
            txtSearch.Size = new Size(895, 25);
            txtSearch.TabIndex = 0;
            // 
            // btnSearch
            // 
            btnSearch.Anchor = AnchorStyles.Top;
            btnSearch.Font = new Font("Segoe UI", 10F);
            btnSearch.Location = new Point(1503, 11);
            btnSearch.Name = "btnSearch";
            btnSearch.Size = new Size(90, 27);
            btnSearch.TabIndex = 1;
            btnSearch.Text = "🔍 Search";
            btnSearch.UseVisualStyleBackColor = true;
            // 
            // cmbType
            // 
            cmbType.Anchor = AnchorStyles.Top;
            cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbType.Font = new Font("Segoe UI", 10F);
            cmbType.Location = new Point(1174, 11);
            cmbType.Name = "cmbType";
            cmbType.Size = new Size(120, 25);
            cmbType.TabIndex = 2;
            // 
            // cmbStatus
            // 
            cmbStatus.Anchor = AnchorStyles.Top;
            cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStatus.Font = new Font("Segoe UI", 10F);
            cmbStatus.Location = new Point(1354, 11);
            cmbStatus.Name = "cmbStatus";
            cmbStatus.Size = new Size(120, 25);
            cmbStatus.TabIndex = 3;
            // 
            // lblType
            // 
            lblType.Anchor = AnchorStyles.Top;
            lblType.AutoSize = true;
            lblType.Font = new Font("Segoe UI", 9F);
            lblType.Location = new Point(1134, 15);
            lblType.Name = "lblType";
            lblType.Size = new Size(35, 15);
            lblType.TabIndex = 4;
            lblType.Text = "Type:";
            // 
            // lblStatus
            // 
            lblStatus.Anchor = AnchorStyles.Top;
            lblStatus.AutoSize = true;
            lblStatus.Font = new Font("Segoe UI", 9F);
            lblStatus.Location = new Point(1304, 15);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(42, 15);
            lblStatus.TabIndex = 5;
            lblStatus.Text = "Status:";
            // 
            // webViewResults
            // 
            webViewResults.AllowExternalDrop = true;
            webViewResults.CreationProperties = null;
            webViewResults.DefaultBackgroundColor = Color.White;
            webViewResults.Dock = DockStyle.Fill;
            webViewResults.Location = new Point(0, 70);
            webViewResults.Name = "webViewResults";
            webViewResults.Size = new Size(1605, 608);
            webViewResults.TabIndex = 0;
            webViewResults.ZoomFactor = 1D;
            // 
            // topPanel
            // 
            topPanel.Controls.Add(lblProgress);
            topPanel.Controls.Add(pbProgress);
            topPanel.Controls.Add(cmbProject);
            topPanel.Controls.Add(label1);
            topPanel.Controls.Add(txtSearch);
            topPanel.Controls.Add(btnSearch);
            topPanel.Controls.Add(cmbType);
            topPanel.Controls.Add(cmbStatus);
            topPanel.Controls.Add(lblType);
            topPanel.Controls.Add(lblStatus);
            topPanel.Controls.Add(btnClose);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(1605, 70);
            topPanel.TabIndex = 1;
            // 
            // lblProgress
            // 
            lblProgress.AutoSize = true;
            lblProgress.Location = new Point(1480, 48);
            lblProgress.Name = "lblProgress";
            lblProgress.Size = new Size(16, 15);
            lblProgress.TabIndex = 10;
            lblProgress.Text = "...";
            // 
            // pbProgress
            // 
            pbProgress.Location = new Point(21, 48);
            pbProgress.Name = "pbProgress";
            pbProgress.Size = new Size(1453, 10);
            pbProgress.TabIndex = 9;
            // 
            // cmbProject
            // 
            cmbProject.Anchor = AnchorStyles.Top;
            cmbProject.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProject.Font = new Font("Segoe UI", 10F);
            cmbProject.Location = new Point(74, 15);
            cmbProject.Name = "cmbProject";
            cmbProject.Size = new Size(120, 25);
            cmbProject.TabIndex = 7;
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top;
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9F);
            label1.Location = new Point(21, 19);
            label1.Name = "label1";
            label1.Size = new Size(47, 15);
            label1.TabIndex = 8;
            label1.Text = "Project:";
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
            // SearchDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1605, 678);
            Controls.Add(webViewResults);
            Controls.Add(topPanel);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(600, 400);
            Name = "SearchDialog";
            Text = "M O N O V E R A - SEARCH";
            ((System.ComponentModel.ISupportInitialize)webViewResults).EndInit();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }
        private ComboBox cmbProject;
        private Label label1;
        private ProgressBar pbProgress;
        private Label lblProgress;
    }
}
