# Ueditor Architecture

Ueditor is a WinUI 3 desktop text editor shell with WebView2-based editing and preview surfaces. The current MVP focuses on a standalone/offline editor path first: no CDN is required for the editor or preview renderer.

## Modules

- App Shell: `MainWindow.xaml` owns the toolbar, left explorer/favorites/library/search/Git tabs, center editor tabs, right preview/AI panel, splitters, and status bar.
- Editor Module: `Editor/MonacoBridge.cs` is the C# bridge for the WebView editor page. The current `WebResources/editor.html` is a standalone textarea editor with line numbers, selection IPC, word wrap, tab handling, and Markdown command support. The bridge name is kept so a local Monaco bundle can be swapped in later without rewriting the shell.
- Preview Module: `WebResources/preview.html` is a unified built-in renderer for Markdown, sanitized HTML, and lightweight LaTeX display. It receives debounced content updates from the active tab and syncs theme/font/color settings.
- Large File Module: `FileService` builds line-offset indexes and `WebResources/large-viewer.html` renders only the viewport lines. The current mode is read-only by default, with patch-save plumbing preserved for the later limited-edit phase.
- Settings Module: `SettingsService` persists JSON under `%USERPROFILE%\.ueditor\settings.json`. API keys are intentionally excluded from this file.
- LLM Module: `LLMService` dispatches to provider implementations and stores API keys via Windows Generic Credentials.
- Git Module: `GitService` delegates to the Git CLI for branch, status, diff, stage, unstage, and commit.
- Library Module: `SnippetService` stores reusable snippets/templates in local app data.

## MVP Stages

1. MVP 1: app launch, open/save, standalone editor surface, tabs, file explorer, Markdown/HTML/LaTeX preview, basic search, word wrap, settings persistence.
2. MVP 2: large file detection, read-only Large File Mode, chunk/line loading, virtual scroll, streaming search.
3. MVP 3: LLM provider settings, secure API key storage, selected-text explain/summarize/improve, prompt/snippet library.
4. MVP 4: Git changed files, diff view, stage/unstage, commit.
5. MVP 5: limited large-file editing, stronger undo/redo, advanced regex replace, richer snippet/template management.

## Performance Rules

- Files at or above the configured threshold are offered Large File Mode.
- Files at or above 200 MB open in Large File Mode by default.
- Preview updates are debounced.
- Large File Mode keeps the main UI responsive by rendering only visible lines.
- API requests only use selected text or explicit context, never an entire large file automatically.
