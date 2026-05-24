# Ueditor

<p align="center">
  <img src="Ueditor/Assets/Ueditor.png" alt="Ueditor Logo" width="100" height="100" />
</p>

<h3 align="center">Ueditor</h3>

<p align="center">
  <strong>A Premium WinUI 3 Desktop Text Editor Shell with WebView2 Hybrid Core</strong>
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/download/dotnet/10.0"><img src="https://img.shields.io/badge/.NET-10.0-blueviolet.svg?style=flat-square" alt="Target Framework" /></a>
  <a href="https://learn.microsoft.com/en-us/windows/apps/winui/winui3/"><img src="https://img.shields.io/badge/WinUI-3-blue.svg?style=flat-square" alt="UI Framework" /></a>
  <a href="https://github.com/microsoft/WindowsAppSDK"><img src="https://img.shields.io/badge/Windows%20App%20SDK-2.1.3-orange.svg?style=flat-square" alt="SDK Version" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green.svg?style=flat-square" alt="License" /></a>
</p>

---

## 🌟 Overview

**Ueditor** is a high-performance, elegant, and modern desktop text editor shell built for Windows. It marries the robust, native desktop capabilities of **WinUI 3 (.NET 10.0)** with the rendering flexibility of a **WebView2-based hybrid core**. 

Designed for developers, writers, and power users, Ueditor provides a fluid, distraction-free environment that operates fully offline, requiring no external CDNs. It features standard workspace tools like a **live Markdown/HTML/LaTeX previewer**, a **built-in terminal**, **comprehensive Git integration**, **multi-provider secure AI assistance**, and a dedicated **virtualized Large File Mode** capable of opening 200MB+ files seamlessly.

---

## ✨ Key Features

### 🖥️ Native Premium Windows UI
*   **Mica Backdrop & Dark Mode:** Built-in support for fluid native Windows themes, using a high-fidelity Mica backdrop (`MicaBackdrop` Base) for a premium operating system look and feel.
*   **Responsive Multi-Pane Splitters:** Seamlessly adjust your workspace with interactive sidebars, preview sections, and terminal splitters using custom drag-to-resize C# split-controls.
*   **Custom Title Bar:** Clean native Windows title bar integration for standard control interactions.
*   **Always on Top & Sticky Notes:** Pin your editor window or launch quick sticky notes directly from the toolbar for rapid prototyping.

### 📝 Hybrid Monaco-Bridge Editor
*   **WebView2 Editor Core:** A highly configurable, local-resource-based textarea editor that emulates the behavior of Monaco.
*   **C# IPC MonacoBridge:** Inter-process communication lets C# seamlessly sync selection states, editor contents, font styles, word wrap settings, and trigger markdown formatting commands in real-time.
*   **Rich Markdown Toolbar:** Apply bold, italic, underline, highlight, quote, list, tables, and color styling to your text with a single click.

### 👁️ Built-in Real-Time Preview
*   **Unified Live Renderer:** Preview your content side-by-side using the right preview pane (`preview.html`).
*   **Multiple Modes:** Easily toggle rendering output modes between **Markdown**, **HTML Source**, and **LaTeX Blocks** (powered by a local **KaTeX** bundle).
*   **Theme Synchronization:** The preview automatically inherits the system theme, editor font, and colors.
*   **External View:** Open your rendered preview in an external default web browser with a single tap.

### ⚡ High-Performance Large File Mode
*   **Virtual Viewport Scrolling:** When large files (above the threshold, or 200MB+ by default) are detected, Ueditor switches into a read-only virtual scrolling view (`large-viewer.html`).
*   **Line-Offset Indexing:** Scans files to map line positions instantly, rendering only the lines visible in the viewport, which keeps the WinUI thread completely responsive.
*   **Streaming Search:** Search inside massive files efficiently without loading the entire contents into memory.

### 🤖 Secure Integrated AI Assistant
*   **Multi-Provider Support:** Fully integrates with **Gemini**, **OpenAI**, and local **LM Studio** endpoints.
*   **Windows Credential Storage:** API keys are stored securely using the native Windows Generic Credentials manager via the `CredentialService`.
*   **Selection-Context Quick Actions:** Select text in the active tab to instantly invoke context-aware AI prompts such as **Explain**, **Summarize**, **Refactor**, **Check Grammar**, or **Fix Code**.

### 💻 Embedded Native Terminal
*   **Powershell / CMD Integration:** Seamlessly launch and switch terminal sessions directly beneath your editor canvas using the `TerminalPane` control.
*   **Directory Synchronization:** Automatically matches the terminal's working directory with the current workspace or file explorer folder.

### 🌿 Full Git Integration Panel
*   **Active Status Tracker:** Detects repository configurations on-the-fly and monitors branch statuses.
*   **Interactive Sidebar:** Switch branches, view staged/unstaged changes, toggle staging per-file or globally, and execute commits.
*   **Remote Synchronization:** Perform standard Git push actions directly from the side panel.
*   **Commit History Viewer:** Visually inspect your repository's recent commit history list.

### 🔍 Advanced Search & Replace
*   **Folder-Wide Search:** Perform fast multi-file lookups with robust filtering.
*   **Text & Pattern Matching:** Supports Match Case, Whole Word, and complex **Regex** search and replacements.
*   **Dynamic Results List:** View matches instantly with file names and lines, and double-click to jump straight to the source.

---

## 🛠️ Technical Architecture

Ueditor is built with clean, modular separation of concerns. Below is a high-level overview of the module hierarchy:

*   **App Shell (`MainWindow`):** Owns the main container grid, toolbar components, side tab views, editor canvas, previewer splitters, and status controls.
*   **Editor Module (`MonacoBridge`):** Interfaces C# with the local HTML5/JS editor. Can easily be updated to run a fully bundled local Monaco Editor package without rewriting core shell bindings.
*   **Terminal Module (`TerminalPane`):** Manages native console host redirection and UI integration.
*   **Core Services:**
    *   `FileService`: Manages line index parsing, large file offsets, and I/O pipelines.
    *   `GitService`: Communicates with the local Git CLI to map states, staging, and history.
    *   `LLMService`: Handles chat queries and handles providers (`GeminiProvider`, `OpenAIProvider`, `LMStudioProvider`).
    *   `CredentialService`: Securely hooks into Windows Credential Manager to protect API secrets.
    *   `SettingsService`: Persists workspace and app preferences in `%USERPROFILE%\.ueditor\settings.json`.

For a more comprehensive breakdown, please refer to [Ueditor/ARCHITECTURE.md](file:///d:/ASUNA/Tools/Visual_studio/Ueditor/Ueditor/ARCHITECTURE.md).

---

## 📁 Repository Structure

```
Ueditor/
├── .vs/                       # Visual Studio state folders
├── Ueditor.slnx               # Modern Visual Studio Solution configuration
└── Ueditor/
    ├── ARCHITECTURE.md        # Technical architecture documentation
    ├── App.xaml               # Application initialization markup
    ├── App.xaml.cs            # Custom application startup logic
    ├── MainWindow.xaml        # Main shell interface layout
    ├── MainWindow.xaml.cs     # Main shell event handlers and UI logic
    ├── app.manifest           # Application security manifests
    ├── Controls/
    │   ├── TerminalPane.xaml  # Embedded Terminal control
    │   └── TerminalPane.cs    # Native console redirection and terminal IPC
    ├── Core/
    │   ├── Interfaces/        # Interface contracts
    │   ├── Models/            # Domain models (Settings, Tabs, Terminal sessions)
    │   └── Services/          # Service layer (Git, File, LLM, Credentials)
    ├── Editor/
    │   └── MonacoBridge.cs    # C# WebView2 Javascript bridge
    └── WebResources/          # Local offline editor, preview, and KaTeX assets
```

---

## 🚀 Getting Started

### Prerequisites

To build and run Ueditor locally, make sure you have:
*   **Windows 10 / 11**
*   **Visual Studio 2022** (v17.10 or later recommended)
*   **.NET 10.0 SDK**
*   **Windows App SDK** component installed inside Visual Studio.
*   **WebView2 Runtime** (installed by default on modern Windows).

### How to Run

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/kirinonakar/Ueditor.git
    cd Ueditor
    ```
2.  **Open the Solution:**
    Open the `Ueditor.slnx` (or `Ueditor.csproj`) inside Visual Studio.
3.  **Restore & Build:**
    Visual Studio will automatically restore NuGet packages (such as `Microsoft.WindowsAppSDK`).
4.  **Run:**
    Set the startup project to `Ueditor` and press `F5` to build and run in unpackaged mode.

---

## 📦 Deployment & Publishing

Ueditor is configured as a **Self-Contained Unpackaged** desktop app (`<WindowsPackageType>None</WindowsPackageType>`), meaning it does not require MSIX packaging tools to run on target systems.

To publish a standalone executable:
```bash
dotnet publish Ueditor/Ueditor.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true
```
This produces a fully optimized, self-contained `win-x64` executable structure in the publish directory.

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

Copyright (c) 2026 **kirinonakar**. All rights reserved.
