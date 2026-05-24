# Ueditor Architecture

Ueditor is a WinUI 3 desktop text editor shell with WebView2-based editing and preview surfaces. The current MVP focuses on a standalone/offline editor path first: no CDN is required for the editor or preview renderer.

## Modules

- App Shell: `MainWindow.xaml` owns the toolbar, left explorer/favorites/library/search/Git tabs, center editor tabs, right preview/AI panel, splitters, and status bar.
- Terminal UI: `Controls/TerminalPane.xaml` owns embedded terminal session UI, native console hosting, redirected fallback I/O, and terminal session switching. `MainWindow` only controls panel placement and supplies the working directory.
- Editor Model Module: `Editor/VirtualTextModel.cs` owns the line-based text model, range/edit APIs, search, encoding-aware file loading, and streaming save. It keeps editor state out of `MainWindow`.
- Editor Module: `Editor/MonacoBridge.cs` is the C# bridge for the WebView editor page. `WebResources/editor.html` is now a unified virtualized editor: it asks the model for visible line ranges, renders only the viewport plus overscan, and sends line edits back to the model. The bridge name is kept so a local Monaco bundle can be swapped in later without rewriting the shell.
- Preview Module: `WebResources/preview.html` is a unified virtualized renderer for Markdown, sanitized HTML, LaTeX, and Aozora-style text. It receives debounced model invalidations from the active tab and requests only the visible source lines.
- Settings Module: `SettingsService` persists JSON under `%USERPROFILE%\.ueditor\settings.json`. API keys are intentionally excluded from this file.
- LLM Module: `LLMService` dispatches to provider implementations and stores API keys via Windows Generic Credentials.
- Git Module: `GitService` delegates to the Git CLI for branch, status, diff, stage, unstage, and commit.
- Library Module: `SnippetService` stores reusable snippets/templates in local app data.

## MVP Stages

1. MVP 1: app launch, open/save, standalone editor surface, tabs, file explorer, Markdown/HTML/LaTeX preview, basic search, word wrap, settings persistence.
2. MVP 2: unified virtual text model, line loading, virtual editor scroll, virtual preview scroll, model-backed search.
3. MVP 3: LLM provider settings, secure API key storage, selected-text explain/summarize/improve, prompt/snippet library.
4. MVP 4: Git changed files, diff view, stage/unstage, commit.
5. MVP 5: limited large-file editing, stronger undo/redo, advanced regex replace, richer snippet/template management.

## Performance Rules

- Editor tabs use the same virtualized path regardless of file size.
- The editor renders only visible lines plus overscan.
- Long lines are capped in the renderer to protect frame time.
- Preview updates are debounced and virtualized.
- API requests only use selected text or an explicitly capped file context, never an unbounded full file automatically.
