namespace Monovera
{
    partial class frmConfiguration
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmConfiguration));
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            txtUrl = new TextBox();
            txtEmail = new TextBox();
            txtToken = new TextBox();
            lstProjects = new ListBox();
            label4 = new Label();
            btnAddProject = new Button();
            btnEditProject = new Button();
            btnSave = new Button();
            btnDeleteProject = new Button();
            chkOffline = new CheckBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Microsoft Sans Serif", 11.25F);
            label1.Location = new Point(31, 29);
            label1.Name = "label1";
            label1.Size = new Size(73, 18);
            label1.TabIndex = 0;
            label1.Text = "JIRA URL";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Microsoft Sans Serif", 11.25F);
            label2.Location = new Point(29, 62);
            label2.Name = "label2";
            label2.Size = new Size(80, 18);
            label2.TabIndex = 1;
            label2.Text = "JIRA Email";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Microsoft Sans Serif", 11.25F);
            label3.Location = new Point(31, 98);
            label3.Name = "label3";
            label3.Size = new Size(85, 18);
            label3.TabIndex = 2;
            label3.Text = "JIRA Token";
            // 
            // txtUrl
            // 
            txtUrl.Font = new Font("Microsoft Sans Serif", 11.25F);
            txtUrl.Location = new Point(145, 25);
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new Size(499, 24);
            txtUrl.TabIndex = 3;
            // 
            // txtEmail
            // 
            txtEmail.Font = new Font("Microsoft Sans Serif", 11.25F);
            txtEmail.Location = new Point(145, 59);
            txtEmail.Name = "txtEmail";
            txtEmail.Size = new Size(641, 24);
            txtEmail.TabIndex = 4;
            // 
            // txtToken
            // 
            txtToken.Font = new Font("Microsoft Sans Serif", 11.25F);
            txtToken.Location = new Point(145, 95);
            txtToken.Name = "txtToken";
            txtToken.Size = new Size(641, 24);
            txtToken.TabIndex = 5;
            // 
            // lstProjects
            // 
            lstProjects.Font = new Font("Microsoft Sans Serif", 11.25F);
            lstProjects.FormattingEnabled = true;
            lstProjects.Location = new Point(145, 143);
            lstProjects.Name = "lstProjects";
            lstProjects.Size = new Size(499, 184);
            lstProjects.TabIndex = 6;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Microsoft Sans Serif", 11.25F);
            label4.Location = new Point(31, 143);
            label4.Name = "label4";
            label4.Size = new Size(98, 18);
            label4.TabIndex = 7;
            label4.Text = "JIRA Projects";
            // 
            // btnAddProject
            // 
            btnAddProject.Font = new Font("Microsoft Sans Serif", 11.25F);
            btnAddProject.Location = new Point(650, 143);
            btnAddProject.Name = "btnAddProject";
            btnAddProject.Size = new Size(130, 40);
            btnAddProject.TabIndex = 8;
            btnAddProject.Text = "Add";
            btnAddProject.UseVisualStyleBackColor = true;
            // 
            // btnEditProject
            // 
            btnEditProject.Font = new Font("Microsoft Sans Serif", 11.25F);
            btnEditProject.Location = new Point(650, 189);
            btnEditProject.Name = "btnEditProject";
            btnEditProject.Size = new Size(130, 41);
            btnEditProject.TabIndex = 9;
            btnEditProject.Text = "Modify";
            btnEditProject.UseVisualStyleBackColor = true;
            // 
            // btnSave
            // 
            btnSave.Font = new Font("Microsoft Sans Serif", 11.25F);
            btnSave.Location = new Point(650, 283);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(130, 41);
            btnSave.TabIndex = 10;
            btnSave.Text = "Save";
            btnSave.UseVisualStyleBackColor = true;
            // 
            // btnDeleteProject
            // 
            btnDeleteProject.Font = new Font("Microsoft Sans Serif", 11.25F);
            btnDeleteProject.Location = new Point(650, 236);
            btnDeleteProject.Name = "btnDeleteProject";
            btnDeleteProject.Size = new Size(130, 41);
            btnDeleteProject.TabIndex = 11;
            btnDeleteProject.Text = "Delete";
            btnDeleteProject.UseVisualStyleBackColor = true;
            // 
            // chkOffline
            // 
            chkOffline.AutoSize = true;
            chkOffline.Font = new Font("Microsoft Sans Serif", 11.25F);
            chkOffline.Location = new Point(666, 27);
            chkOffline.Name = "chkOffline";
            chkOffline.Size = new Size(111, 22);
            chkOffline.TabIndex = 13;
            chkOffline.Text = "Offline Mode";
            chkOffline.UseVisualStyleBackColor = true;
            chkOffline.CheckedChanged += chkOffline_CheckedChanged;
            // 
            // frmConfiguration
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(798, 340);
            Controls.Add(chkOffline);
            Controls.Add(btnDeleteProject);
            Controls.Add(btnSave);
            Controls.Add(btnEditProject);
            Controls.Add(btnAddProject);
            Controls.Add(label4);
            Controls.Add(lstProjects);
            Controls.Add(txtToken);
            Controls.Add(txtEmail);
            Controls.Add(txtUrl);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "frmConfiguration";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "C O N F I G U R A T I O N";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label label2;
        private Label label3;
        private TextBox txtUrl;
        private TextBox txtEmail;
        private TextBox txtToken;
        private ListBox lstProjects;
        private Label label4;
        private Button btnAddProject;
        private Button btnEditProject;
        private Button btnSave;
        private Button btnDeleteProject;
        private CheckBox chkOffline;
    }
}