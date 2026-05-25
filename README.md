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

Designed for developers, writers, and power users, Ueditor provides a fluid, distraction-free environment that operates fully offline, requiring no external CDNs. It features standard workspace tools like a **live Markdown/HTML/LaTeX previewer**, a **built-in terminal**, **comprehensive Git integration**, **multi-provider secure AI assistance**, and a **virtualized editor core** capable of opening and editing files from small snippets to 200MB+ logs seamlessly.

---

## ✨ Key Features

### 📝 Virtualized Editor Core
*   **Massive File Support:** Instantly open and edit extremely large files (200MB+ logs or source code) with zero lag, keeping the editor highly responsive.
*   **Virtual Scrolling:** Renders only visible viewport lines plus an overscan buffer (`editor.html`), keeping DOM elements minimal and rendering fast.
*   **Inline Editing:** Smooth, direct text editing synchronized with the high-performance C# `VirtualTextModel` backend in real-time.
*   **Streaming Search:** Search inside massive files efficiently without loading the entire contents into memory.
*   **Monaco-Bridge IPC:** Inter-process communication automatically syncs selection, font styles, word wrap, and formatting.
*   **Rich Toolbar:** Quick-apply styling (bold, italic, underline, list, color, tables) to selected text.

### 🖥️ Native Premium Windows UI
*   **Mica Backdrop & Dark Mode:** Native Windows themes using a high-fidelity Mica backdrop (`MicaBackdrop` Base).
*   **Multi-Pane Splitters:** Easily adjust sidebars, preview sections, and terminal panes via interactive C# split-controls.
*   **Always on Top & Sticky Notes:** Pin your editor window or launch quick sticky notes directly from the toolbar.

### 👁️ Real-Time Preview
*   **Live Renderer:** View Markdown, HTML, or LaTeX (powered by KaTeX) in a split view or an external browser.
*   **Theme Sync:** Automatically inherits editor fonts, colors, and system theme.

### 🤖 Secure AI Assistant
*   **Multi-Provider:** Connect with Gemini, OpenAI, or local LM Studio endpoints.
*   **Secure Storage:** API keys are securely saved via native Windows Credential Manager.
*   **Context Actions:** Quick AI actions (Explain, Refactor, Summarize, Fix) on selected text.

### 💻 Embedded Native Terminal
*   **PowerShell & CMD:** Launch terminal sessions directly beneath your editor canvas.
*   **Path Syncing:** Automatically matches the terminal's working directory with the active workspace.

### 🌿 Git Panel
*   **Status Tracker:** View staged/unstaged changes, stage/unstage files, execute commits, and push to remotes.
*   **History Viewer:** Visual repository branch and commit history logs.

### 🔍 Advanced Search
*   **Global Lookup:** Folder-wide multi-file search with Match Case, Whole Word, and Regex filtering.
*   **Jump-to-Source:** Double-click search results to open the file and focus the exact line.

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
