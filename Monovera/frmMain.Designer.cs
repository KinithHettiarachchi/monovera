namespace Monovera
{
    partial class frmMain
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmMain));
            mainWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)mainWebView).BeginInit();
            SuspendLayout();

            // mainWebView — full client area
            mainWebView.Dock = DockStyle.Fill;
            mainWebView.Name = "mainWebView";
            mainWebView.TabIndex = 0;

            // frmMain
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1600, 900);
            Controls.Add(mainWebView);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "frmMain";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "M O N O V E R A";
            WindowState = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.Sizable;
            Load += frmMain_Load;

            ((System.ComponentModel.ISupportInitialize)mainWebView).EndInit();
            ResumeLayout(false);
        }

        // Single WebView2 that hosts the entire SPA
        private Microsoft.Web.WebView2.WinForms.WebView2 mainWebView;

        // ── Stub properties so business-logic code still compiles ─────────────
        // These are no-ops; status is surfaced via /api/progress endpoint instead.
        private _StubLabel _lblUser = new();
        private _StubLabel _lblProgress = new();
        private _StubLabel _lblJiraUpdateProcessing = new();
        private _StubLabel _lblSyncStatus = new();
        private _StubLabel _lblShortcuts = new();
        private _StubProgressBar _pbProgress = new();

        // Expose with the same names used throughout frmMain.cs
        private _StubLabel lblUser => _lblUser;
        private _StubLabel lblProgress => _lblProgress;
        private _StubLabel lblJiraUpdateProcessing => _lblJiraUpdateProcessing;
        private _StubLabel lblSyncStatus => _lblSyncStatus;
        private _StubLabel lblShortcuts => _lblShortcuts;
        private _StubProgressBar pbProgress => _pbProgress;

        // Stub tree (not displayed; kept so code that populates issueDict/childrenByParent still compiles)
        private TreeView tree = new TreeView();
        // Panel stubs (not displayed)
        private Panel panelTabs = new Panel();
        private ToolTip toolTip1 = new ToolTip();
        // tabDetails stub – the SPA handles tabs; this keeps legacy code compilable without a visible control
        private TabControl tabDetails = new TabControl();

        // ── Stub label / progress-bar types ───────────────────────────────────
        private sealed class _StubLabel
        {
            public string Text { get; set; } = "";
            public System.Drawing.Color ForeColor { get; set; }
            public bool Visible { get; set; } = true;
        }
        private sealed class _StubProgressBar
        {
            public int Value { get; set; }
            public int Maximum { get; set; } = 100;
            public System.Windows.Forms.ProgressBarStyle Style { get; set; }
            public bool Visible { get; set; } = true;
        }
    }
}
