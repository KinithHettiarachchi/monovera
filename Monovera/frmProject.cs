using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Monovera
{
    /// <summary>
    /// Dialog form for adding or editing a Jira project configuration.
    /// Allows the user to specify project key, root issue, link type, and mappings for types and statuses.
    /// </summary>
    public partial class frmProject : Form
    {
        /// <summary>
        /// The project configuration being edited or created.
        /// This property is set when the dialog is accepted (OK).
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ProjectConfig Project { get; private set; }

        /// <summary>
        /// Initializes the project configuration dialog.
        /// If an existing ProjectConfig is provided, its values are loaded into the UI for editing.
        /// </summary>
        /// <param name="existing">Optional existing ProjectConfig to edit; if null, creates a new config.</param>
        public frmProject(ProjectConfig existing = null)
        {
            InitializeComponent();

            // Clone the existing project config if editing, or create a new one
            Project = existing != null ? CloneProject(existing) : new ProjectConfig();

            // If editing, populate UI fields with existing values
            if (existing != null)
            {
                txtProject.Text = existing.Project;
                txtRoot.Text = existing.Root;
                txtLinkType.Text = existing.LinkTypeName;
                txtSortingField.Text = existing.SortingField; // <-- Add this

                // Populate types mapping grid
                foreach (var kv in existing.Types)
                    dgvTypes.Rows.Add(kv.Key, kv.Value);

                // Populate status mapping grid
                foreach (var kv in existing.Status)
                    dgvStatus.Rows.Add(kv.Key, kv.Value);
            }

            // OK button: read values from UI and update Project, then close dialog with OK result
            btnOk.Click += (s, e) =>
            {
                // Read basic fields
                Project.Project = txtProject.Text;
                Project.Root = txtRoot.Text;
                Project.LinkTypeName = txtLinkType.Text;
                Project.SortingField = txtSortingField.Text; // <-- Add this

                // Read type and status mappings from grids
                Project.Types = ReadFromGrid(dgvTypes);
                Project.Status = ReadFromGrid(dgvStatus);

                DialogResult = DialogResult.OK;
            };

            // Cancel button: close dialog without saving changes
            btnCancel.Click += (s, e) =>
            {
                this.Close();
            };
        }

        /// <summary>
        /// Reads key-value pairs from a DataGridView and returns them as a dictionary.
        /// Skips empty rows and rows with missing keys or values.
        /// </summary>
        /// <param name="dgv">The DataGridView to read from.</param>
        /// <returns>Dictionary of key-value pairs from the grid.</returns>
        private Dictionary<string, string> ReadFromGrid(DataGridView dgv)
        {
            var dict = new Dictionary<string, string>();
            foreach (DataGridViewRow row in dgv.Rows)
            {
                // Skip the new row placeholder
                if (row.IsNewRow) continue;

                // Read key and value from the first two columns
                var key = row.Cells[0].Value?.ToString();
                var val = row.Cells[1].Value?.ToString();

                // Only add non-empty key-value pairs
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                    dict[key] = val;
            }
            return dict;
        }

        /// <summary>
        /// Creates a deep copy of a ProjectConfig object.
        /// Used to avoid modifying the original config when editing.
        /// </summary>
        /// <param name="original">The original ProjectConfig to clone.</param>
        /// <returns>A new ProjectConfig with copied values.</returns>
        private ProjectConfig CloneProject(ProjectConfig original)
        {
            return new ProjectConfig
            {
                Project = original.Project,
                Root = original.Root,
                LinkTypeName = original.LinkTypeName,
                SortingField = original.SortingField, // <-- Add this
                Types = new Dictionary<string, string>(original.Types),
                Status = new Dictionary<string, string>(original.Status)
            };
        }
    }
}
