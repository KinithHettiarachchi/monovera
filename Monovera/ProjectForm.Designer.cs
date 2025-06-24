namespace Monovera
{
    partial class ProjectForm
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
            DataGridViewCellStyle dataGridViewCellStyle1 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle2 = new DataGridViewCellStyle();
            DataGridViewCellStyle dataGridViewCellStyle3 = new DataGridViewCellStyle();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ProjectForm));
            btnCancel = new Button();
            btnOk = new Button();
            txtRoot = new TextBox();
            txtProject = new TextBox();
            label2 = new Label();
            label1 = new Label();
            dgvTypes = new DataGridView();
            Names = new DataGridViewTextBoxColumn();
            Icons = new DataGridViewTextBoxColumn();
            label3 = new Label();
            label4 = new Label();
            dgvStatus = new DataGridView();
            dataGridViewTextBoxColumn1 = new DataGridViewTextBoxColumn();
            dataGridViewTextBoxColumn2 = new DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)dgvTypes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvStatus).BeginInit();
            SuspendLayout();
            // 
            // btnCancel
            // 
            btnCancel.Font = new Font("Segoe UI", 9.75F);
            btnCancel.Location = new Point(327, 432);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new Size(109, 40);
            btnCancel.TabIndex = 6;
            btnCancel.Text = "Cancel";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOk
            // 
            btnOk.Font = new Font("Segoe UI", 9.75F);
            btnOk.Location = new Point(212, 432);
            btnOk.Name = "btnOk";
            btnOk.Size = new Size(109, 40);
            btnOk.TabIndex = 4;
            btnOk.Text = "Ok";
            btnOk.UseVisualStyleBackColor = true;
            btnOk.Click += btnOk_Click;
            // 
            // txtRoot
            // 
            txtRoot.Font = new Font("Segoe UI", 9.75F);
            txtRoot.Location = new Point(94, 58);
            txtRoot.Name = "txtRoot";
            txtRoot.Size = new Size(342, 25);
            txtRoot.TabIndex = 1;
            // 
            // txtProject
            // 
            txtProject.Font = new Font("Segoe UI", 9.75F);
            txtProject.Location = new Point(94, 24);
            txtProject.Name = "txtProject";
            txtProject.Size = new Size(342, 25);
            txtProject.TabIndex = 0;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 9.75F);
            label2.Location = new Point(24, 61);
            label2.Name = "label2";
            label2.Size = new Size(69, 17);
            label2.TabIndex = 11;
            label2.Text = "Root Issue";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9.75F);
            label1.Location = new Point(26, 28);
            label1.Name = "label1";
            label1.Size = new Size(48, 17);
            label1.TabIndex = 10;
            label1.Text = "Project";
            // 
            // dgvTypes
            // 
            dgvTypes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            dgvTypes.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvTypes.BackgroundColor = SystemColors.Control;
            dataGridViewCellStyle1.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = SystemColors.Control;
            dataGridViewCellStyle1.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            dataGridViewCellStyle1.ForeColor = SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = Color.LimeGreen;
            dataGridViewCellStyle1.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = DataGridViewTriState.True;
            dgvTypes.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            dgvTypes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvTypes.Columns.AddRange(new DataGridViewColumn[] { Names, Icons });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = SystemColors.Window;
            dataGridViewCellStyle2.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            dataGridViewCellStyle2.ForeColor = SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = Color.LimeGreen;
            dataGridViewCellStyle2.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.False;
            dgvTypes.DefaultCellStyle = dataGridViewCellStyle2;
            dgvTypes.Location = new Point(94, 101);
            dgvTypes.Name = "dgvTypes";
            dgvTypes.Size = new Size(342, 150);
            dgvTypes.TabIndex = 2;
            // 
            // Names
            // 
            Names.HeaderText = "Name";
            Names.Name = "Names";
            Names.Width = 68;
            // 
            // Icons
            // 
            Icons.HeaderText = "Icon";
            Icons.Name = "Icons";
            Icons.Width = 57;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 9.75F);
            label3.Location = new Point(24, 101);
            label3.Name = "label3";
            label3.Size = new Size(41, 17);
            label3.TabIndex = 17;
            label3.Text = "Types";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 9.75F);
            label4.Location = new Point(24, 268);
            label4.Name = "label4";
            label4.Size = new Size(56, 17);
            label4.TabIndex = 19;
            label4.Text = "Statuses";
            // 
            // dgvStatus
            // 
            dgvStatus.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            dgvStatus.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            dgvStatus.BackgroundColor = SystemColors.Control;
            dgvStatus.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvStatus.Columns.AddRange(new DataGridViewColumn[] { dataGridViewTextBoxColumn1, dataGridViewTextBoxColumn2 });
            dataGridViewCellStyle3.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = SystemColors.Window;
            dataGridViewCellStyle3.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            dataGridViewCellStyle3.ForeColor = SystemColors.ControlText;
            dataGridViewCellStyle3.SelectionBackColor = Color.LimeGreen;
            dataGridViewCellStyle3.SelectionForeColor = SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = DataGridViewTriState.False;
            dgvStatus.DefaultCellStyle = dataGridViewCellStyle3;
            dgvStatus.Location = new Point(94, 268);
            dgvStatus.Name = "dgvStatus";
            dgvStatus.Size = new Size(342, 150);
            dgvStatus.TabIndex = 3;
            // 
            // dataGridViewTextBoxColumn1
            // 
            dataGridViewTextBoxColumn1.HeaderText = "Name";
            dataGridViewTextBoxColumn1.Name = "dataGridViewTextBoxColumn1";
            dataGridViewTextBoxColumn1.Width = 64;
            // 
            // dataGridViewTextBoxColumn2
            // 
            dataGridViewTextBoxColumn2.HeaderText = "Icon";
            dataGridViewTextBoxColumn2.Name = "dataGridViewTextBoxColumn2";
            dataGridViewTextBoxColumn2.Width = 55;
            // 
            // ProjectForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(464, 484);
            Controls.Add(label4);
            Controls.Add(dgvStatus);
            Controls.Add(label3);
            Controls.Add(dgvTypes);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);
            Controls.Add(txtRoot);
            Controls.Add(txtProject);
            Controls.Add(label2);
            Controls.Add(label1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ProjectForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "M O N O V E R A - Project";
            Load += ProjectForm_Load;
            ((System.ComponentModel.ISupportInitialize)dgvTypes).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvStatus).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnCancel;
        private Button btnOk;
        private TextBox txtRoot;
        private TextBox txtProject;
        private Label label2;
        private Label label1;
        private DataGridView dgvTypes;
        private Label label3;
        private DataGridViewTextBoxColumn Names;
        private DataGridViewTextBoxColumn Icons;
        private Label label4;
        private DataGridView dgvStatus;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn1;
        private DataGridViewTextBoxColumn dataGridViewTextBoxColumn2;
    }
}