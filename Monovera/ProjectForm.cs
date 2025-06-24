using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Monovera
{
    public partial class ProjectForm : Form
    {

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ProjectConfig Project { get; private set; }


        public ProjectForm(ProjectConfig existing = null)
        {
            InitializeComponent();
            Project = existing != null ? CloneProject(existing) : new ProjectConfig();

            if (existing != null)
            {
                txtProject.Text = existing.Project;
                txtRoot.Text = existing.Root;
                txtLinkType.Text=existing.LinkTypeName;
                foreach (var kv in existing.Types)
                    dgvTypes.Rows.Add(kv.Key, kv.Value);
                foreach (var kv in existing.Status)
                    dgvStatus.Rows.Add(kv.Key, kv.Value);
            }

            btnOk.Click += (s, e) =>
            {
                Project.Project = txtProject.Text;
                Project.Root = txtRoot.Text;
                Project.LinkTypeName = txtLinkType.Text;
                Project.Types = ReadFromGrid(dgvTypes);
                Project.Status = ReadFromGrid(dgvStatus);
                DialogResult = DialogResult.OK;
            };

            btnCancel.Click += (s, e) =>
            {
                this.Close();
            };
        }

        private Dictionary<string, string> ReadFromGrid(DataGridView dgv)
        {
            var dict = new Dictionary<string, string>();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                var key = row.Cells[0].Value?.ToString();
                var val = row.Cells[1].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                    dict[key] = val;
            }
            return dict;
        }

        private ProjectConfig CloneProject(ProjectConfig original)
        {
            return new ProjectConfig
            {
                Project = original.Project,
                Root = original.Root,
                LinkTypeName = original.LinkTypeName,
                Types = new Dictionary<string, string>(original.Types),
                Status = new Dictionary<string, string>(original.Status)
            };
        }
    }
}
