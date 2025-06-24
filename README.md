# Monovera

**Monovera** (One Truth) is an open-source Windows Forms application that allows users to efficiently **visualize**, **search**, and **navigate** a Jira requirement repository structured using **parent-child relationships**.

It is ideal for product development, QA, and business analysis teams working with structured Jira data. Monovera provides a user-friendly interface to explore hierarchical issue relationships and access detailed issue information — including attachments, links, history, and even embedded `.feature` files from SVN.

---

## ✨ Features

- 🧭 **Visual Tree View of Requirements**  
  Displays the full parent-child issue hierarchy using collapsible tree nodes.

- 🔍 **Powerful Search Dialog (Ctrl+Shift+S)**  
  Search by text, issue type, and status across selected projects — shows results grouped by **Title match** and **Description match**.

- 📋 **Issue Details Viewer**  
  Clicking an issue displays full detail:
  - Summary and rendered description
  - Linked issues (Parent, Children, Related)
  - Inline change history with diff
  - Attachments with previews and downloads
  - Raw JSON response

- 💡 **Icon Support**  
  Rich icons for issue types and statuses, fully customizable via config.

- 🔗 **Jira Link Detection**  
  Auto-converts inline Jira links and SVN-style links into clickable in-app links.

- 🧩 **SVN Integration**  
  Automatically fetches and embeds `.feature` file content referenced via SVN links in descriptions.

---

## 🛠 Configuration

Before launching the app, create a `configuration.json` file in the application directory with the following structure:

```json
{
  "Jira": {
    "Url": "<https://YOUR_DOMAIN.atlassian.net>",
    "Email": "YOUR_EMAIL@YOUR_DOMAIN.com",
    "Token": "YOUR_TOKEN"
  },
  "Projects": [
    {
      "Project": "PROJECT1",
      "Root": "PRJ1-100",
      "LinkTypeName": "Blocks",
      "Types": {
        "Issue Type1": "type_icon1.png",
        "Issue Type2": "type_icon2.png"
      },
      "Status": {
        "Status 1": "status_1.png",
        "Status 2": "status_2.png"
      }
    },
    {
      "Project": "PROJECT2",
      "Root": "PRJ2-1",
      "LinkTypeName": "Blocks",
      "Types": {
        "Issue Type3": "type_icon3.png",
        "Issue Type4": "type_icon4.png"
      },
      "Status": {
        "Status X": "status_X.png",
        "Status Y": "status_Y.png"
      }
    }
  ]
}
```

- `Jira.Url`: Your Jira instance base URL  
- `Projects`: List of projects to load with root issue key, type and status icons  
- `Types` & `Status`: Maps for custom icons used for each type/status

🔒 **Note**: Your credentials are used locally only — no telemetry or remote logging.

---

## 🔎 Search Example

Press `Ctrl+Shift+S` to open the search dialog.

You can:
- Enter an issue key to jump directly
- Or search by text across selected projects
- Filter by Issue Type and Status

Results appear in a collapsible viewer with clickable links like:

```text
🧑 Add login validation [PRJ1-123]
🧑 Setup error messages [PRJ2-124]
```

Clicking a result auto-navigates the tree to that issue.

---

## 📁 Project Structure

- **Form1.cs** – Main form: loads tree, handles detail view, icons, history, and attachments  
- **SearchDialog.cs** – Dialog to search issues across loaded projects  
- **configuration.json** – Your Jira credentials, project roots, and icon mappings

---

## 📸 Screenshots

| Tree View | Issue Details | Search Dialog |
|-----------|----------------|----------------|
| ![](screenshots/tree.png) | ![](screenshots/details.png) | ![](screenshots/search.png) |

---

## 🧑‍💻 Build Instructions

1. Clone the repo:
   ```bash
   git clone https://github.com/KinithHettiarachchi/monovera.git
   ```

2. Open the solution in **Visual Studio**.

3. Requirements:
   - .NET 6+ or compatible .NET Framework
   - WebView2 runtime installed

4. Create the `images/` folder in your app directory and place the icons defined in your `configuration.json`.

5. Run the application.

---

## 🧱 Dependencies

- **WebView2** – For rendering HTML-based views  
- **SharpSVN** – To pull `.feature` file content from SVN repositories  
- **System.Text.Json**, **HttpClient** – For Jira API communication

---

## 🔓 License

Monovera is open-source and released under the [MIT License](LICENSE).

You are free to use, modify, and distribute this software. Contributions welcome!

---

## 🙌 Contributing

We welcome contributions to improve Monovera!

- Create feature branches  
- Write clear commit messages  
- Submit a pull request

For major changes, please open an issue first to discuss the proposal.

---

## 📬 Contact

If you use Monovera or would like to contribute, feel free to reach out via GitHub Issues or Discussions.
