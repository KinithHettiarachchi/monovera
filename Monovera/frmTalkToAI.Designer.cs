namespace Monovera
{
    partial class frmTalkToAI
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.WebBrowser webBrowserFallback; // Optional fallback
        private Microsoft.Web.WebView2.WinForms.WebView2 webViewTestCases;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmTalkToAI));
            webViewTestCases = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)webViewTestCases).BeginInit();
            SuspendLayout();
            // 
            // webViewTestCases
            // 
            webViewTestCases.AllowExternalDrop = true;
            webViewTestCases.BackColor = Color.White;
            webViewTestCases.CreationProperties = null;
            webViewTestCases.DefaultBackgroundColor = Color.White;
            webViewTestCases.Dock = DockStyle.Fill;
            webViewTestCases.Location = new Point(0, 0);
            webViewTestCases.Name = "webViewTestCases";
            webViewTestCases.Size = new Size(1605, 745);
            webViewTestCases.TabIndex = 0;
            webViewTestCases.ZoomFactor = 1D;
            // 
            // frmAITestCases
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1605, 745);
            Controls.Add(webViewTestCases);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimizeBox = false;
            MinimumSize = new Size(600, 400);
            Name = "frmAITestCases";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "G E N E R A T E  T E S T C A S E S";
            ((System.ComponentModel.ISupportInitialize)webViewTestCases).EndInit();
            ResumeLayout(false);
        }
    }
}
