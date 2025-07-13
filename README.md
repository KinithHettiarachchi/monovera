# 🌐 Monovera

**Monovera** _(Latin: "One Truth")_ is a powerful **Windows Forms application** that lets you **visualize, search, and explore Jira requirement hierarchies** with ease.  
Built for **Product Managers**, **QA Engineers**, and **Business Analysts**, Monovera simplifies structured Jira data and lets you **see the full story as a hieararchy — in one interactive UI**.

---

## 🚀 Overview

🔐 Securely connect to your Jira cloud instance, configure project roots, and **explore issues in a visual tree**.  
📂 Select any issue to view a **beautifully rendered HTML preview** — including its description, attachments, history, links, and even `.feature` files referenced from your SVN repositories.

Monovera helps teams gain **deep insights into requirements** — without clicking through dozens of Jira pages.

---

## ✨ Features at a Glance

- 🌲 **Hierarchical Tree View**  
  Intuitively view full parent–child Jira issue structures.
  The relationship to be used in determining the hierarchy can be easily configured in the configuration file.
  _i.e. Blocks or any other relationship that denotes your parent-child way of hiearchy_ 

- 🔍 **Advanced Search**  
  Search across projects by **text, type, and status**, with results categorized by where the match occurred (title vs. description).

- 📄 **Detailed HTML Viewer**  
  See:
  - ✅ Summary and rendered **Jira description** (with markup and colors)
  - 🔗 **Linked Issues** (Parents, Children, Related)
  - 📜 **Change History** with inline visual diffs
  - 📎 **Attachments** (view, download, and open)
  - 🧩 Embedded **`.feature` files** from SVN
  - 🧠 Raw **JSON** for power users

- 🧾 **Generate Hierarchical Reports**
Export a comprehensive, structured report starting from any selected issue, capturing the full hierarchy beneath it. The report maintains the parent–child structure so readers can seamlessly follow the flow of requirements in a single, continuous document.
This approach provides a clear and fast overview of the entire scope — much more effective than navigating separate pages for each issue, which can be fragmented and tiring to read. Ideal for reviews, stakeholder presentations, or documentation.

- 🎨 **Custom Icons**  
  Use personalized icons for issue types and statuses.

- 🔗 **Inline Jira & SVN Link Handling**  
  Automatically renders issue keys and SVN references as clickable links.

- 🛠️ **Configuration UI**  
  Manage your credentials, project list, and icon mappings with a built-in GUI.

---

## ⚙️ Quick Configuration

Create a file named `configuration.json` in the app directory:

```json
{
  "Jira": {
    "Url": "https://YOUR_DOMAIN.atlassian.net",
    "Email": "YOUR_EMAIL@domain.com",
    "Token": "YOUR_API_TOKEN"
  },
  "Projects": [
    {
      "Project": "PROJ1",
      "Root": "PROJ1-100",
      "Types": {
        "Story": "story_icon.png",
        "Bug": "bug_icon.png"
      },
      "Status": {
        "To Do": "todo_icon.png",
        "Done": "done_icon.png"
      }
    }
  ]
}
```

💡 **Tips**:  
- `Types` and `Status` sections let you define custom icons.  
- Place all icons in an `images/` folder within your application directory.

🔒 **Note**: Your credentials are stored locally and never transmitted or logged externally.

---

## 🧱 Project Structure

| File | Purpose |
|------|---------|
| `frmMain.cs` | Main UI and Jira integration logic |
| `ConfigForm.cs` | Dialog to configure Jira credentials and projects |
| `frmProject.cs` | Per-project editor for issue types, icons, and status |
| `frmSearch.cs` | Search dialog with live results in WebView2 |
| `JiraConfigRoot.cs` | Models for JSON config and internal mapping |
| `JiraHtmlReportGenerator.cs` | Generates full HTML reports for selected issue trees |
| `configuration.json` | Your project + credential settings |
| `images/` | Folder for all icons used in the UI |

---

## 📸 Screenshots

| Tree View | Issue Details | Search Dialog |
|-----------|----------------|----------------|
| ![](screenshots/tree.png) | ![](screenshots/details.png) | ![](screenshots/search.png) |

---

## 🧑‍💻 Build & Run

### 🛠 Requirements

- [.NET 6+ SDK](https://dotnet.microsoft.com/download)
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### 🚧 Steps

```bash
# 1. Clone the repo
git clone https://github.com/KinithHettiarachchi/monovera.git

# 2. Open in Visual Studio
# (Double-click the .sln file or open via File > Open > Project)

# 3. Add icon images to: images/

# 4. Create your configuration.json

# 5. Build & Run
```

---

## 📦 Dependencies

| Library | Purpose |
|--------|---------|
| **WebView2** | Render HTML views with modern browser support |
| **SharpSVN** | Read `.feature` files from SVN repositories |
| **System.Text.Json** | Fast and flexible JSON handling |
| **HttpClient** | Communicate with Jira's REST API |

---

## 📄 License

Licensed under the [MIT License](LICENSE).  
You're free to **use, modify, and distribute** Monovera.

---

## 🤝 Contributing

We welcome improvements, ideas, and PRs!

### ✅ How to contribute:
1. Fork this repository  
2. Create a new branch: `feature/your-feature-name`  
3. Commit your changes with clear messages  
4. Submit a pull request 🙌

📌 For major changes, **please open an issue** first to discuss your ideas.

---

## 📬 Contact

Have feedback, need help, or want to collaborate?  
📮 [Open an Issue](https://github.com/KinithHettiarachchi/monovera/issues)

---

> _“Truth is ever to be found in simplicity.” – Isaac Newton_  
> Let Monovera help you find that truth in your Jira data.
