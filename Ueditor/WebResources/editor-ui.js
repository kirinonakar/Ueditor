// DOM Elements Queries
const scrollContainer = document.getElementById('scroll-container');
const virtualSpacer = document.getElementById('virtual-spacer');
const viewport = document.getElementById('viewport');
const findPanel = document.getElementById('find-panel');
const findInput = document.getElementById('find-input');
const findPrev = document.getElementById('find-prev');
const findNextButton = document.getElementById('find-next');
const findClose = document.getElementById('find-close');
const contextMenu = document.getElementById('context-menu');
const replaceInput = document.getElementById('replace-input');
const replaceBtn = document.getElementById('replace-btn');
const replaceAllBtn = document.getElementById('replace-all-btn');

// Main Render Loop
function render() {
    if (!state.initialized) return;

    const printContainer = document.getElementById('print-container');
    if (printContainer && printContainer.style.display === 'block') {
        return;
    }

    syncCustomSelectionClass();

    const range = visibleRange();
    const rangeKey = `${range.start}:${range.end}:${state.lineCount}:${scrollContainer.clientWidth}:${state.wordWrap}:${totalVirtualHeight()}:${state.cacheVersion}`;
    requestMissingLines(range.start, range.end);
    if (rangeKey === state.lastRangeKey) return;
    state.lastRangeKey = rangeKey;

    const activeEl = document.activeElement;
    const isFocused = activeEl && activeEl.closest('.line-text') && activeEl.getAttribute('contenteditable') === 'true';
    const activeLine = isFocused ? Number(activeEl.dataset.line) : null;
    const activeCaret = isFocused ? getCaretOffset(activeEl) : 0;

    const composingRow = state.isComposing && state.compositionLine
        ? viewport.querySelector(`.line-row[data-line="${state.compositionLine}"]`)
        : null;

    const offsetY = lineTop(range.start);
    viewport.style.transform = `translateY(${offsetY}px)`;
    const rows = [];
    for (let line = range.start; line <= range.end; line++) {
        if (composingRow && line === state.compositionLine) {
            rows.push(`<div class="line-row-placeholder" data-line="${line}"></div>`);
            continue;
        }

        const hasLine = state.cache.has(line);
        const text = hasLine ? state.cache.get(line) : '로딩 중...';
        const isLong = hasLine && text.length > MAX_RENDER_CHARS;
        const displayText = isLong
            ? `${text.slice(0, MAX_RENDER_CHARS)} ... [긴 줄: ${text.length.toLocaleString()}자, 렌더링 보호]`
            : text;
        const contentEditable = !state.readOnly && hasLine && !isLong ? 'true' : 'false';
        const selectionBounds = selectionBoundsForLine(line, displayText.length);
        const isInSelection = !!selectionBounds;
        const isSelectedEmptyLine = isInSelection && displayText.length === 0 && hasCustomSelection();
        const textClass = `line-text${hasLine ? '' : ' loading'}${isLong ? ' long-line' : ''}`;
        const renderPlainForEditableSelection = hasCustomSelection() &&
            hasLine &&
            !isLong &&
            (line === activeLine || line === state.editingLine);
        const lineContent = renderLineContent(line, displayText, renderPlainForEditableSelection);
        rows.push(
            `<div class="line-row${isInSelection ? ' selected-row' : ''}${isSelectedEmptyLine ? ' selected-empty-row' : ''}" data-line="${line}">` +
            `<div class="line-number">${line}</div>` +
            `<div class="${textClass}" contenteditable="${contentEditable}" spellcheck="false" data-line="${line}">${lineContent}</div>` +
            `</div>`
        );
    }

    viewport.innerHTML = rows.join('');

    if (composingRow) {
        const placeholder = viewport.querySelector(`.line-row-placeholder[data-line="${state.compositionLine}"]`);
        if (placeholder) {
            placeholder.replaceWith(composingRow);
        }
    }

    measureRenderedRows();

    if (isFocused && activeLine !== null) {
        const element = viewport.querySelector(`.line-text[data-line="${activeLine}"]`);
        if (element && element.getAttribute('contenteditable') === 'true') {
            setCaret(element, activeCaret);
        }
    }

    drawEditableSelectionOverlays();
}

// Auto-complete Popup State
const autocompleteState = {
    isOpen: false,
    candidates: [],
    activeIndex: 0,
    element: null,
    wordStart: 0,
    word: ''
};

function getWordUnderCaret(text, caretOffset) {
    let start = caretOffset;
    while (start > 0 && /[\w\-ㄱ-ㅎㅏ-ㅣ가-힣]/.test(text[start - 1])) {
        start--;
    }
    const word = text.slice(start, caretOffset);
    return { word, start };
}

function snippetKeywordSpecialPrefix(keyword) {
    const match = keyword.match(/^([^\wㄱ-ㅎㅏ-ㅣ가-힣]+)/);
    return match ? match[1] : '';
}

function getAutocompleteCandidates(currentWord, fullTextBeforeCaret) {
    if (!currentWord || currentWord.length < 1) return [];
    const candidates = [];
    const seen = new Set();
    const lowerCurrent = currentWord.toLowerCase();

    const containsHangul = /[\u3130-\u318F\uAC00-\uD7A3]/.test(currentWord);
    let regex = null;
    if (typeof HangulAutocomplete !== 'undefined' && containsHangul) {
        try {
            const pattern = HangulAutocomplete.makeRegex(currentWord);
            if (pattern) {
                regex = new RegExp('^' + pattern, 'i');
            }
        } catch (e) {
            console.error("HangulAutocomplete regex compile error:", e);
        }
    }

    for (const snippet of state.snippets) {
        const keyword = String(snippet.keyword || '').trim();
        const title = String(snippet.title || '').trim();
        const content = String(snippet.content || '');
        if (!keyword || !content) continue;

        const lowerKeyword = keyword.toLowerCase();

        const specialPrefix = snippetKeywordSpecialPrefix(keyword);
        let matched = false;
        let extraPrefixLen = 0;

        if (regex) {
            if (regex.test(keyword)) {
                matched = true;
            } else if (regex.test(title)) {
                matched = true;
            } else if (specialPrefix && fullTextBeforeCaret !== undefined) {
                try {
                    const prefixPattern = HangulAutocomplete.escapeRegExp(specialPrefix) + HangulAutocomplete.makeRegex(currentWord);
                    const prefixRegex = new RegExp('^' + prefixPattern, 'i');
                    if (prefixRegex.test(keyword)) {
                        const textBefore = fullTextBeforeCaret;
                        if (textBefore.endsWith(specialPrefix + currentWord) || textBefore.slice(-specialPrefix.length) === specialPrefix) {
                            matched = true;
                            extraPrefixLen = specialPrefix.length;
                        }
                    }
                } catch (e) {
                    console.error(e);
                }
            }
        } else {
            const lowerTitle = title.toLowerCase();
            if (lowerKeyword.startsWith(lowerCurrent)) {
                matched = true;
            } else if (lowerTitle.startsWith(lowerCurrent)) {
                matched = true;
            } else if (specialPrefix && fullTextBeforeCaret !== undefined) {
                const withPrefix = specialPrefix + currentWord;
                const lowerWithPrefix = withPrefix.toLowerCase();
                if (lowerKeyword.startsWith(lowerWithPrefix)) {
                    const textBefore = fullTextBeforeCaret;
                    if (textBefore.endsWith(specialPrefix + currentWord) || textBefore.slice(-specialPrefix.length) === specialPrefix) {
                        matched = true;
                        extraPrefixLen = specialPrefix.length;
                    }
                }
            }
        }

        if (!matched) continue;

        const key = `snippet:${lowerKeyword}`;
        if (seen.has(key)) continue;
        seen.add(key);
        const prefixText = state.autocompleteSnippetPrefix || '스니펫: ';
        const defaultText = state.autocompleteSnippet || '스니펫';
        candidates.push({
            kind: 'snippet',
            label: keyword,
            insertText: content,
            detail: title ? `${prefixText}${title}` : defaultText,
            extraPrefixLen
        });
    }

    for (const text of state.cache.values()) {
        if (!text) continue;
        const words = text.match(/[\w\-ㄱ-ㅎㅏ-ㅣ가-힣]+/g);
        if (!words) continue;
        for (const word of words) {
            if (word.length <= currentWord.length) continue;
            if (word === currentWord) continue;

            let isWordMatched = false;
            if (regex) {
                isWordMatched = regex.test(word);
            } else {
                isWordMatched = word.toLowerCase().startsWith(lowerCurrent);
            }

            if (isWordMatched) {
                const key = `word:${word.toLowerCase()}`;
                if (seen.has(key)) continue;
                seen.add(key);
                candidates.push({
                    kind: 'word',
                    label: word,
                    insertText: word,
                    detail: ''
                });
            }
        }
    }

    return candidates
        .sort((a, b) => {
            if (a.kind !== b.kind) return a.kind === 'snippet' ? -1 : 1;
            if (a.label.toLowerCase() === lowerCurrent) return -1;
            if (b.label.toLowerCase() === lowerCurrent) return 1;
            return a.label.localeCompare(b.label);
        })
        .slice(0, 10);
}

function getCaretCoordinates() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    const range = sel.getRangeAt(0).cloneRange();
    range.collapse(false);
    const rects = range.getClientRects();
    if (rects.length > 0) {
        return rects[0];
    }
    return null;
}

function triggerAutocomplete(element) {
    if (!state.autocompleteOnEnter && !state.autocompleteOnTab) return;
    if (hasCustomSelection()) {
        hideAutocomplete();
        return;
    }
    const text = lineTextFromElement(element);
    const caret = getCaretOffset(element);
    const { word, start } = getWordUnderCaret(text, caret);

    if (!word || word.length < 1) {
        hideAutocomplete();
        return;
    }

    const textBeforeCaret = text.slice(0, caret);
    const candidates = getAutocompleteCandidates(word, textBeforeCaret);
    if (candidates.length === 0) {
        hideAutocomplete();
        return;
    }

    const preserveActiveIndex = autocompleteState.isOpen && autocompleteState.word === word;

    autocompleteState.isOpen = true;
    autocompleteState.candidates = candidates;
    autocompleteState.activeIndex = preserveActiveIndex ? Math.min(autocompleteState.activeIndex, candidates.length - 1) : 0;
    autocompleteState.element = element;
    autocompleteState.wordStart = start;
    autocompleteState.word = word;
    autocompleteState.textBeforeCaret = textBeforeCaret;

    renderAutocomplete();
}

function renderAutocomplete() {
    const popup = document.getElementById('autocomplete-popup');
    if (!popup) return;
    const caretRect = getCaretCoordinates();
    if (!caretRect) {
        hideAutocomplete();
        return;
    }

    const itemsHtml = autocompleteState.candidates.map((candidate, idx) => {
        const isActive = idx === autocompleteState.activeIndex ? ' active' : '';
        const detail = candidate.detail
            ? `<span class="autocomplete-detail">${escapeHtml(candidate.detail)}</span>`
            : '';
        return `<button class="autocomplete-item${isActive}" type="button" data-index="${idx}"><span class="autocomplete-label">${escapeHtml(candidate.label)}</span>${detail}</button>`;
    }).join('');

    popup.innerHTML = itemsHtml;
    popup.hidden = false;

    const popupRect = popup.getBoundingClientRect();
    let left = caretRect.left;
    let top = caretRect.bottom + 4;

    if (left + popupRect.width > window.innerWidth) {
        left = window.innerWidth - popupRect.width - 10;
    }
    if (top + popupRect.height > window.innerHeight) {
        top = caretRect.top - popupRect.height - 4;
    }

    popup.style.left = `${Math.max(10, left)}px`;
    popup.style.top = `${Math.max(10, top)}px`;
}

function hideAutocomplete() {
    autocompleteState.isOpen = false;
    autocompleteState.candidates = [];
    autocompleteState.activeIndex = 0;
    autocompleteState.element = null;
    const popup = document.getElementById('autocomplete-popup');
    if (popup) popup.hidden = true;
}

function scrollAutocompleteActiveIntoView() {
    const popup = document.getElementById('autocomplete-popup');
    if (!popup) return;
    const activeItem = popup.querySelector('.autocomplete-item.active');
    if (activeItem) {
        activeItem.scrollIntoView({ block: 'nearest' });
    }
}

function insertSelectedCandidate() {
    const candidate = autocompleteState.candidates[autocompleteState.activeIndex];
    const element = autocompleteState.element;
    if (!candidate || !element) {
        hideAutocomplete();
        return;
    }

    const text = lineTextFromElement(element);
    let wordStart = autocompleteState.wordStart;
    const caret = getCaretOffset(element);

    if (candidate.kind === 'snippet') {
        const specialPrefix = snippetKeywordSpecialPrefix(candidate.label || '');
        if (specialPrefix && wordStart >= specialPrefix.length) {
            const textBefore = text.slice(wordStart - specialPrefix.length, wordStart);
            if (textBefore === specialPrefix) {
                wordStart -= specialPrefix.length;
            }
        }
    }

    replaceWordWithAutocompleteText(element, wordStart, caret, candidate.insertText || candidate.label || '');
    hideAutocomplete();
}

function replaceWordWithAutocompleteText(element, wordStart, caret, insertText) {
    const text = lineTextFromElement(element);
    const normalized = String(insertText || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
    if (!normalized.includes('\n')) {
        const nextText = text.slice(0, wordStart) + normalized + text.slice(caret);
        updateSingleLine(element, nextText, wordStart + normalized.length);
        return;
    }

    const lineNumber = Number(element.dataset.line || 1);
    const before = text.slice(0, wordStart);
    const after = text.slice(caret);
    const parts = normalized.split('\n');
    const insertedCount = parts.length - 1;
    const firstLine = before + parts[0];
    const lastLineNumber = lineNumber + insertedCount;

    state.cache.set(lineNumber, firstLine);
    shiftCachedLines(lineNumber + 1, insertedCount);
    post({ type: 'lineChanged', lineNumber, text: firstLine });

    for (let i = 1; i < parts.length; i++) {
        const nextText = i === parts.length - 1 ? parts[i] + after : parts[i];
        const nextLineNumber = lineNumber + i;
        state.cache.set(nextLineNumber, nextText);
        post({ type: 'insertLine', lineNumber: nextLineNumber, text: nextText });
    }

    state.lineCount += insertedCount;
    setupVirtualHeight();
    post({ type: 'contentChanged' });
    queueRender(true);
    setTimeout(() => focusLine(lastLineNumber, parts[parts.length - 1]?.length || 0), 0);
}

// Find & Replace panel operations
function openFindPanel() {
    findPanel.hidden = false;
    const replaceRow = document.getElementById('replace-row');
    if (replaceRow) {
        replaceRow.style.display = state.readOnly ? 'none' : 'flex';
    }
    const replaceActionsRow = document.getElementById('replace-actions-row');
    if (replaceActionsRow) {
        replaceActionsRow.style.display = state.readOnly ? 'none' : 'flex';
    }
    const selected = selectedText();
    if (selected && !/[\r\n]/.test(selected)) {
        findInput.value = selected;
    }
    findInput.focus();
    findInput.select();
    requestFindAll();
}

function executeReplace() {
    if (state.readOnly || !state.activeSearch) return;

    const replaceText = replaceInput.value || '';
    const { lineNumber, indexOfMatch, matchLength, query } = state.activeSearch;
    const originalText = state.cache.get(lineNumber);
    if (originalText === undefined) return;

    if (indexOfMatch + matchLength > originalText.length) return;

    let nextText = originalText;
    if (state.findRegex) {
        try {
            const regex = new RegExp(query, state.findMatchCase ? 'g' : 'gi');
            let replaced = false;
            nextText = originalText.replace(regex, (m, ...args) => {
                const offset = args[args.length - 2];
                if (offset === indexOfMatch && !replaced) {
                    replaced = true;
                    const cleanQuery = query.replace(/^\^/, '').replace(/\$$/, '');
                    const cleanRegex = new RegExp(cleanQuery, state.findMatchCase ? '' : 'i');
                    return m.replace(cleanRegex, replaceText);
                }
                return m;
            });
        } catch (e) {
            nextText = originalText.slice(0, indexOfMatch) + replaceText + originalText.slice(indexOfMatch + matchLength);
        }
    } else {
        nextText = originalText.slice(0, indexOfMatch) + replaceText + originalText.slice(indexOfMatch + matchLength);
    }

    state.cache.set(lineNumber, nextText);
    post({ type: 'lineChanged', lineNumber: lineNumber, text: nextText });
    post({ type: 'contentChanged' });

    const currentQuery = findInput.value;
    if (currentQuery) {
        post({ type: 'findAll', query: currentQuery, matchCase: state.findMatchCase, isRegex: state.findRegex });
    } else {
        queueRender(true);
    }
}

function executeReplaceAll() {
    if (state.readOnly || state.searchMatches.length === 0) return;

    const query = findInput.value;
    if (!query) return;

    const replaceText = replaceInput.value || '';
    post({
        type: 'replaceAll',
        query: query,
        replace: replaceText,
        matchCase: state.findMatchCase,
        isRegex: state.findRegex
    });
}

function closeFindPanel() {
    findPanel.hidden = true;
    state.searchQuery = '';
    state.searchMatches = [];
    state.searchIndex = -1;
    state.activeSearch = null;
    queueRender(true);
    focusLine(state.currentLine, Math.max(0, state.currentColumn - 1));
}

function requestFindAll() {
    const query = findInput.value;
    if (!query) {
        state.searchQuery = '';
        state.searchMatches = [];
        state.searchIndex = -1;
        state.activeSearch = null;
        queueRender(true);
        return;
    }
    state.searchQuery = query;
    post({ type: 'findAll', query, matchCase: state.findMatchCase, isRegex: state.findRegex });
}

function requestFind(reverse = false) {
    const query = findInput.value;
    if (!query || state.searchMatches.length === 0) return;

    if (state.searchIndex < 0) state.searchIndex = 0;

    if (reverse) {
        state.searchIndex = (state.searchIndex - 1 + state.searchMatches.length) % state.searchMatches.length;
    } else {
        state.searchIndex = (state.searchIndex + 1) % state.searchMatches.length;
    }

    const match = state.searchMatches[state.searchIndex];
    state.activeSearch = {
        lineNumber: match.lineNumber,
        indexOfMatch: match.indexOfMatch,
        matchLength: match.matchLength,
        query
    };
    revealLine(match.lineNumber, match.indexOfMatch, match.matchLength, query, true);
}

// Context Menu Operations
function showContextMenu(clientX, clientY) {
    for (const button of contextMenu.querySelectorAll('.context-menu-button')) {
        const requiresEdit = button.dataset.requiresEdit === 'true';
        button.disabled = requiresEdit && state.readOnly;
    }

    const scrollSyncBtn = contextMenu.querySelector('[data-action="toggleScrollSync"]');
    if (scrollSyncBtn) {
        scrollSyncBtn.textContent = (state.scrollSyncEnabled ? '✓ ' : '') + (state.menuScrollSync || '스크롤 동기화');
    }

    contextMenu.hidden = false;
    const menuRect = contextMenu.getBoundingClientRect();
    const left = Math.min(clientX, window.innerWidth - menuRect.width - 4);
    const top = Math.min(clientY, window.innerHeight - menuRect.height - 4);
    contextMenu.style.left = `${Math.max(4, left)}px`;
    contextMenu.style.top = `${Math.max(4, top)}px`;

    for (const item of contextMenu.querySelectorAll('.context-menu-item.has-submenu')) {
        const submenu = item.querySelector(':scope > .submenu');
        if (!submenu) continue;

        const prevDisplay = submenu.style.display;
        const prevVisibility = submenu.style.visibility;
        submenu.style.visibility = 'hidden';
        submenu.style.display = 'block';
        submenu.style.left = '100%';
        submenu.style.right = 'auto';
        submenu.style.top = '-4px';
        submenu.style.bottom = 'auto';

        const itemRect = item.getBoundingClientRect();
        const sw = submenu.offsetWidth;
        const sh = submenu.offsetHeight;

        submenu.style.display = prevDisplay;
        submenu.style.visibility = prevVisibility;

        const goLeft = (itemRect.right + sw) > window.innerWidth;
        submenu.style.left = goLeft ? 'auto' : '100%';
        submenu.style.right = goLeft ? '100%' : 'auto';

        const goUp = (itemRect.top - 4 + sh) > window.innerHeight;
        submenu.style.top = goUp ? 'auto' : '-4px';
        submenu.style.bottom = goUp ? '-4px' : 'auto';
    }
}

function hideContextMenu() {
    contextMenu.hidden = true;
}

// C# Host Message Handling
function handleCsharpMessage(msg) {
    switch (msg.action) {
        case 'initModel':
            state.initialized = true;
            state.language = msg.language || 'plaintext';
            applyOptions(msg);
            setupModel(msg.lineCount || 1);
            if (receiveLineBlock(msg.initialStartLine || 1, msg.initialLines || []) > 0) {
                queueRender(true);
            }
            document.getElementById('loading-overlay')?.classList.add('hidden');
            break;
        case 'setText':
            {
                if (state.isComposing) {
                    break;
                }
                const text = msg.text || '';
                const lines = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
                state.selection = null;
                syncCustomSelectionClass();
                const targetLine = Math.min(state.currentLine, lines.length);
                const targetCol = Math.min(Math.max(0, state.currentColumn - 1), (lines[targetLine - 1] || '').length);
                setupModel(Math.max(1, lines.length));
                lines.forEach((line, index) => state.cache.set(index + 1, line));
                queueRender(true);
                if (msg.shouldFocus !== false) {
                    setTimeout(() => focusLine(targetLine, targetCol), 20);
                }
            }
            break;
        case 'updateLine':
            {
                updateLineFromHost(msg.lineNumber || 1, msg.text || '', !!msg.isComposing);
            }
            break;
        case 'receiveLines':
            {
                receiveLineBlock(msg.startLine || 1, msg.lines || []);
                runPendingLineActions();
                if (!state.isComposing) {
                    queueRender(true);
                }
            }
            break;
        case 'lineCountChanged':
            state.lineCount = Math.max(1, Number(msg.lineCount || 1));
            state.cacheVersion++;
            setupVirtualHeight();
            queueRender(true);
            break;
        case 'setLanguage':
            state.language = msg.language || 'plaintext';
            queueRender(true);
            break;
        case 'updateOptions':
            applyOptions(msg);
            break;
        case 'updateSnippets':
            state.snippets = Array.isArray(msg.snippets) ? msg.snippets : [];
            if (autocompleteState.isOpen) {
                const element = autocompleteState.element;
                if (element) triggerAutocomplete(element);
            }
            break;
        case 'triggerFind':
            openFindPanel();
            break;
        case 'getSelection':
            post({ type: 'selectionResult', text: selectedText() });
            break;
        case 'flushForSave':
            flushPendingEditForSave(msg.requestId || 0);
            break;
        case 'insertText':
            insertTextAtCaret(msg.text || '');
            break;
        case 'markdownCommand':
            applyMarkdownCommand(msg.command, msg.color);
            break;
        case 'revealLine':
            revealLine(msg.lineNumber || 1, msg.indexOfMatch || 0, msg.matchLength || 0, msg.query || '');
            break;
        case 'findAllResult':
            state.searchQuery = msg.query || '';
            state.searchMatches = msg.matches || [];
            state.searchIndex = state.searchMatches.length > 0 ? 0 : -1;
            state.activeSearch = null;
            if (state.searchIndex >= 0) {
                const match = state.searchMatches[0];
                state.activeSearch = {
                    lineNumber: match.lineNumber,
                    indexOfMatch: match.indexOfMatch,
                    matchLength: match.matchLength,
                    query: state.searchQuery
                };
                revealLine(match.lineNumber, match.indexOfMatch, match.matchLength, state.searchQuery, true);
            }
            queueRender(true);
            break;
        case 'findResult':
            if (msg.found) {
                revealLine(msg.lineNumber, msg.indexOfMatch || 0, msg.matchLength || 0, msg.query || findInput.value, true);
            }
            break;
        case 'focus':
            focusLine(state.currentLine, Math.max(0, state.currentColumn - 1));
            break;
        case 'clipboardReadResult':
            {
                const requestId = Number(msg.requestId || 0);
                const pending = state.clipboardRequests.get(requestId);
                if (pending) {
                    clearTimeout(pending.timer);
                    state.clipboardRequests.delete(requestId);
                    pending.resolve(String(msg.text || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n'));
                }
            }
            break;
        case 'scrollSyncChanged':
            state.scrollSyncEnabled = !!msg.enabled;
            break;
        case 'syncScroll':
            if (state.scrollSyncEnabled && msg.firstLine) {
                isSyncingScroll = true;
                const targetScrollTop = lineTop(msg.firstLine) + (msg.offset || 0);
                scrollContainer.scrollTop = targetScrollTop;
                requestAnimationFrame(() => {
                    isSyncingScroll = false;
                });
            }
            break;
    }
}

function revealLine(lineNumber, indexOfMatch = 0, matchLength = 0, query = '', preventFocus = false) {
    const safeLine = Math.min(Math.max(1, Number(lineNumber || 1)), state.lineCount);
    state.currentLine = safeLine;
    state.currentColumn = Math.max(1, Number(indexOfMatch || 0) + 1);
    state.activeSearch = query
        ? { lineNumber: safeLine, indexOfMatch, matchLength, query }
        : null;
    scrollContainer.scrollTop = Math.max(0, lineTop(safeLine) - state.lineHeight * state.overscan);
    requestLines(Math.max(1, safeLine - state.overscan), state.overscan * 2 + 1);
    queueRender(true);
    if (!preventFocus) {
        setTimeout(() => focusLine(safeLine, Math.max(0, indexOfMatch || 0)), 40);
    }
}

// Printing Support
function printDocument(fullText) {
    var printContainer = document.getElementById('print-container');
    if (!printContainer) {
        printContainer = document.createElement('div');
        printContainer.id = 'print-container';
        document.body.appendChild(printContainer);
    }
    printContainer.textContent = fullText;

    var editorHost = document.getElementById('editor-host');
    var currentBg = getComputedStyle(document.documentElement).getPropertyValue('--bg').trim() || '#fff';
    var currentFg = getComputedStyle(document.documentElement).getPropertyValue('--fg').trim() || '#000';
    
    printContainer.style.cssText = 'display:block; font-family: ' + getComputedStyle(document.documentElement).getPropertyValue('--font-family').trim() + '; font-size: ' + getComputedStyle(document.documentElement).getPropertyValue('--font-size').trim() + '; white-space: pre; padding: 20px; color: ' + currentFg + '; background: ' + currentBg + '; margin: 0; position: absolute; inset: 0; z-index: 1000; overflow: auto;';
    editorHost.style.display = 'none';

    var baseFontSize = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--font-size').trim()) || 13;
    var currentZoom = 1.0;
    printContainer.style.fontSize = baseFontSize + 'px';
    
    printContainer.onwheel = function (e) {
        if (e.ctrlKey) {
            e.preventDefault();
            if (e.deltaY < 0) {
                currentZoom = Math.min(3.0, currentZoom + 0.1);
            } else {
                currentZoom = Math.max(0.5, currentZoom - 0.1);
            }
            printContainer.style.fontSize = (baseFontSize * currentZoom) + 'px';
        }
    };

    window.onafterprint = function () {
        editorHost.style.display = '';
        printContainer.style.cssText = 'display:none;';
        printContainer.onwheel = null;
        window.onafterprint = null;
        queueRender(true);
    };

    setTimeout(function () {
        window.print();
    }, 100);
}

// ----------------------------------------------------
// Core DOM Event Listeners bindings
// ----------------------------------------------------
viewport.addEventListener('input', event => {
    if (shouldSuppressNativeBeforeInput(event)) {
        return;
    }
    const element = lineElementFromEvent(event);
    if (element) {
        if (!state.isComposing && isPendingImeSelectionCollapseFor(element, event)) {
            return;
        }
        if (!state.columnComposition) {
            state.selection = null;
            syncCustomSelectionClass();
        }
        commitLine(element);
        triggerAutocomplete(element);
    }
});

viewport.addEventListener('focusin', event => {
    const element = lineElementFromEvent(event);
    if (element && element.getAttribute('contenteditable') === 'true') {
        state.editingLine = Number(element.dataset.line || state.currentLine || 1);
        queueRender();
    }
});

viewport.addEventListener('focusout', () => {
    setTimeout(() => {
        if (!document.activeElement?.closest?.('.line-text')) {
            state.editingLine = null;
            queueRender(true);
        }
    }, 0);
});

viewport.addEventListener('compositionstart', event => {
    let element = lineElementFromEvent(event) || activeEditableElement();
    const pendingCompositionSelection = compositionSelectionRange();
    let collapsedSelectionForComposition = false;

    if (pendingCompositionSelection && !pendingCompositionSelection.isColumn) {
        element = replaceSelectionForCompositionStart(element) || element;
        collapsedSelectionForComposition = true;
    }

    if (isPendingImeSelectionCollapseFor(element)) {
        clearPendingImeSelectionCollapse();
    }

    state.isComposing = true;
    state.compositionLine = element ? Number(element.dataset.line || state.currentLine || 1) : state.currentLine;

    if (element && element.getAttribute('contenteditable') === 'true') {
        state.editingLine = state.compositionLine;

        if (collapsedSelectionForComposition) {
            state.columnComposition = null;
            return;
        }

        const savedCaret = getCaretOffset(element);
        makeEditablePlainText(element, null, false);
        if (savedCaret > 0) {
            const text = element.textContent || '';
            const col = Math.max(0, Math.min(savedCaret, text.length));
            const textNode = element.firstChild;
            if (textNode && textNode.nodeType === Node.TEXT_NODE) {
                const range = document.createRange();
                range.setStart(textNode, col);
                range.collapse(true);
                const sel = window.getSelection();
                if (sel) {
                    sel.removeAllRanges();
                    sel.addRange(range);
                }
            }
        }
        beginColumnComposition(element);
    } else {
        state.columnComposition = null;
    }
});

viewport.addEventListener('compositionupdate', event => {
    state.isComposing = true;
});

viewport.addEventListener('compositionend', event => {
    const element = lineElementFromEvent(event) || activeEditableElement();
    const lineNumber = element ? Number(element.dataset.line || state.compositionLine || state.currentLine) : state.compositionLine;

    state.isComposing = false;
    clearPendingImeSelectionCollapse();
    state.compositionLine = null;

    if (finishColumnComposition(element, lineNumber)) {
        return;
    }

    if (element && element.getAttribute('contenteditable') === 'true') {
        state.selection = null;
        syncCustomSelectionClass();
        state.editingLine = lineNumber;
        setTimeout(() => {
            const current = viewport.querySelector(`.line-text[data-line="${lineNumber}"]`) || element;
            if (current && current.getAttribute('contenteditable') === 'true') {
                commitLine(current);
                triggerAutocomplete(current);
            }
        }, 0);
    }
});

let lastPointerDownTime = 0;
let lastPointerDownPosition = null;

scrollContainer.addEventListener('pointerdown', event => {
    if (event.button !== 0 || findPanel.contains(event.target)) return;

    const lineNumEl = event.target.closest('.line-number');
    if (lineNumEl) {
        const row = lineNumEl.closest('.line-row');
        if (row) {
            const line = Number(row.dataset.line || 1);
            const text = state.cache.get(line) || '';
            const lineLength = text.length;

            event.preventDefault();
            scrollContainer.setPointerCapture?.(event.pointerId);

            state.selectionAnchor = { line: line, column: 0 };
            state.selection = { start: { line: line, column: 0 }, end: { line: line, column: lineLength || 1 } };
            syncCustomSelectionClass();
            state.isSelecting = true;
            state.isLineSelecting = true;
            document.body.classList.add('selecting');
            state.currentLine = line;
            state.currentColumn = lineLength + 1;

            queueRender(true);
            setTimeout(() => {
                const element = viewport.querySelector(`.line-text[data-line="${line}"]`);
                if (element && element.getAttribute('contenteditable') === 'true') {
                    setCaret(element, lineLength);
                }
            }, 0);
            reportCursorAndSelection(row.querySelector('.line-text'));
            return;
        }
    }

    const position = positionFromPointer(event);
    if (!position) return;

    const now = Date.now();
    const isDoubleClick = (event.detail >= 2) ||
        ((now - lastPointerDownTime < 350) &&
            lastPointerDownPosition &&
            (lastPointerDownPosition.line === position.line) &&
            (Math.abs(lastPointerDownPosition.column - position.column) < 5));

    lastPointerDownTime = now;
    lastPointerDownPosition = position;

    if (isDoubleClick && !event.shiftKey) {
        if (selectWordAtPointer(event)) {
            event.preventDefault();
            return;
        }
    }

    const isEditable = position.element.getAttribute('contenteditable') === 'true';
    event.preventDefault();
    scrollContainer.setPointerCapture?.(event.pointerId);

    const hadSelection = hasCustomSelection();
    const positionText = state.cache.get(position.line) ?? lineTextFromElement(position.element);
    state.dragStartPosition = {
        line: position.line,
        column: position.column,
        isEmptyLine: positionText.length === 0,
        clientX: event.clientX,
        clientY: event.clientY
    };
    const isColumnSelect = event.altKey;
    const anchor = event.shiftKey && state.selectionAnchor
        ? state.selectionAnchor
        : { line: position.line, column: position.column };
    state.selectionAnchor = anchor;
    state.selection = event.shiftKey
        ? { start: anchor, end: { line: position.line, column: position.column }, isColumn: isColumnSelect }
        : null;
    syncCustomSelectionClass();
    state.isSelecting = true;
    state.isLineSelecting = false;
    document.body.classList.add('selecting');
    state.currentLine = position.line;
    state.currentColumn = position.column + 1;
    if (isEditable) {
        setCaret(position.element, position.column);
    }
    if (event.shiftKey || hadSelection) {
        queueRender(true);
        setTimeout(() => focusLine(state.currentLine, Math.max(0, state.currentColumn - 1)), 0);
    }
    reportCursorAndSelection(position.element);
});

function endSelection(event) {
    if (!state.isSelecting) return;
    state.isSelecting = false;
    state.isLineSelecting = false;
    document.body.classList.remove('selecting');
    syncCustomSelectionClass();
    if (event && event.pointerId !== undefined) {
        try {
            scrollContainer.releasePointerCapture?.(event.pointerId);
        } catch (e) { }
    }
    state.dragStartPosition = null;
    const hadSelection = hasCustomSelection();
    const selection = normalizeSelection();
    if (selection && !hasCustomSelection()) {
        state.selection = null;
        syncCustomSelectionClass();
    } else if (selection) {
        const targetLine = hasCustomSelection() && !selection.isColumn ? selection.start.line : state.currentLine;
        const targetColumn = hasCustomSelection() && !selection.isColumn ? selection.start.column : Math.max(0, state.currentColumn - 1);
        setTimeout(() => focusLine(targetLine, targetColumn), 0);
    }
    if (hadSelection || hasCustomSelection()) {
        queueRender(true);
    }
    reportCursorAndSelection(document.activeElement);
}

scrollContainer.addEventListener('pointermove', event => {
    if (!state.isSelecting) return;
    if ((event.buttons & 1) === 0) {
        endSelection(event);
        return;
    }
    const position = positionFromPointer(event);
    if (!position) return;

    event.preventDefault();

    let newSelection;
    if (state.isLineSelecting) {
        const startLine = state.selectionAnchor.line;
        const endLine = position.line;
        if (startLine <= endLine) {
            const endText = state.cache.get(endLine) || '';
            newSelection = {
                start: { line: startLine, column: 0 },
                end: { line: endLine, column: endText.length }
            };
        } else {
            const startText = state.cache.get(startLine) || '';
            newSelection = {
                start: { line: startLine, column: startText.length },
                end: { line: endLine, column: 0 }
            };
        }
    } else {
        newSelection = {
            start: state.selectionAnchor || { line: position.line, column: position.column },
            end: { line: position.line, column: position.column },
            isColumn: event.altKey
        };
    }

    const selectionChanged = !state.selection ||
        state.selection.start.line !== newSelection.start.line ||
        state.selection.start.column !== newSelection.start.column ||
        state.selection.end.line !== newSelection.end.line ||
        state.selection.end.column !== newSelection.end.column;

    const isEmpty = newSelection.start.line === newSelection.end.line &&
        newSelection.start.column === newSelection.end.column;

    if (isEmpty && !state.selection) {
        const emptyLineDragDistance = state.dragStartPosition?.isEmptyLine &&
            state.dragStartPosition.line === position.line
            ? Math.hypot(event.clientX - state.dragStartPosition.clientX, event.clientY - state.dragStartPosition.clientY)
            : 0;
        if (emptyLineDragDistance > 4) {
            newSelection = {
                start: { line: position.line, column: 0 },
                end: { line: position.line, column: 1 }
            };
        } else {
            return;
        }
    }

    const finalSelectionChanged = selectionChanged ||
        newSelection.start.column !== newSelection.end.column;

    if (finalSelectionChanged) {
        const finalSelectionIsEmpty = newSelection.start.line === newSelection.end.line &&
            newSelection.start.column === newSelection.end.column;
        state.selection = finalSelectionIsEmpty ? null : newSelection;
        syncCustomSelectionClass();
        state.currentLine = position.line;
        state.currentColumn = position.column + 1;
        queueRender(true);
        reportCursorAndSelection(position.element);
    }
});

window.addEventListener('pointerup', event => {
    endSelection(event);
});

window.addEventListener('pointercancel', event => {
    if (!state.isSelecting) return;
    state.isSelecting = false;
    state.isLineSelecting = false;
    document.body.classList.remove('selecting');
    syncCustomSelectionClass();
    queueRender(true);
    reportCursorAndSelection(document.activeElement);
});

document.addEventListener('keydown', event => {
    const earlyCtrl = event.ctrlKey || event.metaKey;
    const earlyKey = event.key ? event.key.toLowerCase() : '';
    if (earlyCtrl && earlyKey === 's') {
        event.preventDefault();
        post({ type: 'shortcut', name: 'save' });
        return;
    }

    if (autocompleteState.isOpen) {
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            autocompleteState.activeIndex = (autocompleteState.activeIndex + 1) % autocompleteState.candidates.length;
            renderAutocomplete();
            scrollAutocompleteActiveIntoView();
            return;
        }
        if (event.key === 'ArrowUp') {
            event.preventDefault();
            autocompleteState.activeIndex = (autocompleteState.activeIndex - 1 + autocompleteState.candidates.length) % autocompleteState.candidates.length;
            renderAutocomplete();
            scrollAutocompleteActiveIntoView();
            return;
        }
        if (event.key === 'Enter') {
            if (state.autocompleteOnEnter) {
                event.preventDefault();
                insertSelectedCandidate();
                return;
            } else {
                hideAutocomplete();
            }
        }
        if (event.key === 'Tab' && state.autocompleteOnTab) {
            event.preventDefault();
            insertSelectedCandidate();
            return;
        }
        if (event.key === 'Escape') {
            event.preventDefault();
            hideAutocomplete();
            return;
        }
    }

    // ESC를 누르면 한글 조합 중이어도 자동완성 팝업을 즉시 닫기
    if (event.key === 'Escape' && (event.isComposing || state.isComposing)) {
        hideAutocomplete();
        // isComposing 상태에서 ESC는 IME가 처리하므로 계속 진행
    }

    if (isHangulImeKeyEvent(event)) {
        const active = document.activeElement;
        const isFindOrInput = active && (
            active.closest?.('#find-panel') ||
            active.tagName === 'INPUT' ||
            active.tagName === 'TEXTAREA'
        );
        if (!isFindOrInput && !state.isComposing && !event.ctrlKey && !event.metaKey && !event.altKey) {
            const imeElement = lineElementFromEvent(event) || activeEditableElement();
            const pendingSelection = compositionSelectionRange();
            if (imeElement && pendingSelection && !pendingSelection.isColumn) {
                const replacedElement = replaceSelectionForCompositionStart(imeElement, true) || imeElement;
                state.compositionLine = Number(replacedElement.dataset.line || state.currentLine || 1);
                state.editingLine = state.compositionLine;
            }
        }
        return;
    }

    if (event.key === 'F9') {
        event.preventDefault();
        post({ type: 'shortcut', name: 'f9' });
        return;
    }
    if (event.key === 'F10') {
        event.preventDefault();
        post({ type: 'shortcut', name: 'f10' });
        return;
    }
    if (event.key === 'F12') {
        event.preventDefault();
        post({ type: 'shortcut', name: 'f12' });
        return;
    }

    const ctrl = event.ctrlKey || event.metaKey;
    const key = event.key ? event.key.toLowerCase() : '';

    if (ctrl) {
        if (key === '1') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'toggleLeftPanel' });
            return;
        }
        if (key === '2') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'toggleRightPanel' });
            return;
        }
        if (key === 'n') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'newTab' });
            return;
        }
        if (key === 's') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'save' });
            return;
        }
        if (key === 'o') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'open' });
            return;
        }
        if (key === 'w') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'closeTab' });
            return;
        }
        if (key === 'f') {
            event.preventDefault();
            if (event.shiftKey) post({ type: 'shortcut', name: 'searchAll' });
            else openFindPanel();
            return;
        }
        if (event.code === 'Backquote' || event.key === '`' || event.key === '~' || event.key === 'Dead') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'terminal' });
            return;
        }
        if (key === 'a') {
            event.preventDefault();
            selectAll();
            return;
        }
        if (key === 'z') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'undo' });
            return;
        }
        if (key === 'y') {
            event.preventDefault();
            post({ type: 'shortcut', name: 'redo' });
            return;
        }
    }

    if (document.activeElement && (document.activeElement.closest('#find-panel') || document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA')) {
        return;
    }

    const element = activeEditableElement();
    if (!element || element.getAttribute('contenteditable') !== 'true') return;

    const columnSelection = activeColumnSelection();
    if (columnSelection && isPlainTextKey(event)) {
        event.preventDefault();
        replaceSelectionWith(columnSelection, event.key);
        return;
    }

    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        event.preventDefault();
        moveCaretHorizontal(element, event.key === 'ArrowLeft' ? -1 : 1, event.shiftKey);
        return;
    }

    if (event.key === 'ArrowUp') {
        event.preventDefault();
        const lineNumber = Number(element.dataset.line || 1);
        if (lineNumber > 1) {
            const col = getCaretOffset(element);
            const prevText = state.cache.get(lineNumber - 1) || '';
            const targetCol = Math.min(col, prevText.length);
            focusLine(lineNumber - 1, targetCol);
        }
        return;
    }

    if (event.key === 'ArrowDown') {
        event.preventDefault();
        const lineNumber = Number(element.dataset.line || 1);
        if (lineNumber < state.lineCount) {
            const col = getCaretOffset(element);
            const nextText = state.cache.get(lineNumber + 1) || '';
            const targetCol = Math.min(col, nextText.length);
            focusLine(lineNumber + 1, targetCol);
        }
        return;
    }

    if ((event.key === ' ' || event.code === 'Space') && !event.ctrlKey && !event.metaKey && !event.altKey) {
        event.preventDefault();
        markNativeBeforeInputHandled(['insertSpace'], 80);
        insertPlainTextByModel(element, ' ');
        return;
    }

    if (event.key === 'Tab') {
        event.preventDefault();
        if (event.shiftKey || hasCustomSelection()) {
            changeLineIndent(event.shiftKey ? -1 : 1);
            return;
        }
        insertPlainTextByModel(element, ' '.repeat(state.tabSize));
        return;
    }

    if (isPlainTextKey(event)) {
        event.preventDefault();
        markNativeBeforeInputHandled(['insertText'], 80);
        insertPlainTextByModel(element, event.key);
        triggerAutocomplete(activeEditableElement() || element);
        return;
    }

    if (isModelRepeatKey(event)) {
        event.preventDefault();
        const keyName = normalizedModelRepeatKey(event);
        state.lastDeleteKeyDown = {
            key: keyName,
            line: Number(element.dataset.line || state.currentLine || 1),
            column: getCaretOffset(element),
            time: performance.now()
        };
        markNativeBeforeInputHandled(keyName === 'Backspace'
            ? ['deleteContentBackward']
            : ['deleteContentForward']);
        scheduleModelRepeatEdit(keyName, event.repeat);
        return;
    }

    if (event.key === 'Enter') {
        event.preventDefault();
        splitCurrentLine(element);
        return;
    }
});

viewport.addEventListener('beforeinput', event => {
    let element = lineElementFromEvent(event);

    if (event.isComposing || state.isComposing ||
        event.inputType === 'insertCompositionText' ||
        event.inputType === 'deleteCompositionText') {
        const pendingCompositionSelection = compositionSelectionRange(!state.isComposing);
        if (pendingCompositionSelection && !pendingCompositionSelection.isColumn) {
            const replacedElement = replaceSelectionForCompositionStart(element || activeEditableElement());
            if (replacedElement) {
                element = replacedElement;
                state.compositionLine = Number(replacedElement.dataset.line || state.currentLine || 1);
                state.editingLine = state.compositionLine;
            }
        }
        return;
    }

    if (isPendingImeSelectionCollapseFor(element, event)) {
        return;
    }

    if (shouldSuppressNativeBeforeInput(event)) {
        event.preventDefault();
        return;
    }

    const columnSelection = activeColumnSelection();
    if (columnSelection && event.inputType?.startsWith('insert') &&
        event.inputType !== 'insertCompositionText' &&
        event.inputType !== 'insertFromPaste' &&
        event.inputType !== 'insertFromDrop') {
        event.preventDefault();
        replaceSelectionWith(columnSelection, event.inputType === 'insertLineBreak' || event.inputType === 'insertParagraph' ? '\n' : (event.data || ''));
        return;
    }

    if (isSpaceInputEvent(event)) {
        event.preventDefault();
        const target = element || activeEditableElement();
        if (!target || target.getAttribute('contenteditable') !== 'true') return;

        if (hasCustomSelection()) {
            const sel = normalizeSelection();
            if (sel) replaceSelectionWith(sel, ' ');
            return;
        }

        const text = lineTextFromElement(target);
        const range = inputRangeInElement(event, target);
        const start = range ? range.start : getCaretOffset(target);
        const end = range ? range.end : start;
        makeEditablePlainText(target, start);
        updateSingleLine(target, text.slice(0, start) + ' ' + text.slice(end), start + 1);
        return;
    }

    if (event.inputType === 'deleteContentBackward' || event.inputType === 'deleteContentForward') {
        event.preventDefault();
        const target = element || activeEditableElement();
        if (!target || target.getAttribute('contenteditable') !== 'true') return;

        if (hasCustomSelection()) {
            const sel = normalizeSelection();
            if (sel) replaceSelectionWith(sel, '');
            return;
        }

        const text = lineTextFromElement(target);
        const range = inputRangeInElement(event, target);
        if (range && range.start !== range.end) {
            makeEditablePlainText(target, range.start);
            updateSingleLine(target, text.slice(0, range.start) + text.slice(range.end), range.start);
            return;
        }

        const caret = range ? range.start : getCaretOffset(target);
        makeEditablePlainText(target, caret);
        if (event.inputType === 'deleteContentBackward') {
            if (caret > 0) {
                const tabSize = state.tabSize || 4;
                const prefix = text.slice(0, caret);
                const onlySpacesBefore = prefix.length > 0 && /^ *$/.test(prefix);
                if (onlySpacesBefore && prefix.length % tabSize === 0) {
                    const deleteStart = caret - Math.min(tabSize, caret);
                    updateSingleLine(target, text.slice(0, deleteStart) + text.slice(caret), deleteStart);
                } else {
                    const deleteStart = graphemeDeleteStart(text, caret);
                    updateSingleLine(target, text.slice(0, deleteStart) + text.slice(caret), deleteStart);
                }
            } else {
                mergeLineBackward(target);
            }
        } else {
            if (caret < text.length) {
                const delEnd = graphemeDeleteEnd(text, caret);
                updateSingleLine(target, text.slice(0, caret) + text.slice(delEnd), caret);
            } else {
                mergeLineForward(target);
            }
        }
        return;
    }

    if (!hasCustomSelection()) {
        if (element && element.getAttribute('contenteditable') === 'true' && event.inputType?.startsWith('insert')) {
            makeEditablePlainText(element);
        }
        return;
    }

    const sel = normalizeSelection();
    if (!sel) return;

    if (event.inputType === 'insertText') {
        event.preventDefault();
        replaceSelectionWith(sel, event.data || '');
    } else if (event.inputType === 'insertLineBreak' || event.inputType === 'insertParagraph') {
        event.preventDefault();
        replaceSelectionWith(sel, '\n');
    } else if (event.inputType === 'insertFromPaste' || event.inputType === 'insertFromDrop') {
        event.preventDefault();
    } else if (event.inputType && event.inputType.startsWith('insert')) {
        event.preventDefault();
        replaceSelectionWith(sel, event.data || '');
    }
});

viewport.addEventListener('keyup', event => {
    if (isModelRepeatKey(event)) {
        clearPendingRepeatEdit();
    }

    const element = lineElementFromEvent(event);
    reportCursorAndSelection(element || document.activeElement);

    if (event.key === 'Shift' && hasCustomSelection() && !state.isComposing) {
        const sel = normalizeSelection();
        if (sel && !sel.isColumn) {
            const startTextElement = viewport.querySelector(`.line-row[data-line="${sel.start.line}"] .line-text`);
            if (startTextElement && document.activeElement !== startTextElement) {
                focusLine(sel.start.line, sel.start.column);
            }
        }
    }

    if ((state.autocompleteOnEnter || state.autocompleteOnTab) && element && element.getAttribute('contenteditable') === 'true') {
        const ignoredKeys = [
            'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
            'Enter', 'Escape', 'Tab', 'Shift', 'Control', 'Alt', 'Meta',
            'CapsLock', 'Home', 'End', 'PageUp', 'PageDown', 'Backspace', 'Delete',
            'Process'
        ];
        if (!ignoredKeys.includes(event.key) && event.keyCode !== 229 && !event.ctrlKey && !event.metaKey) {
            triggerAutocomplete(element);
        }
    }
});

viewport.addEventListener('click', event => {
    const element = lineElementFromEvent(event);
    if (element && element.getAttribute('contenteditable') === 'true') {
        state.editingLine = Number(element.dataset.line || state.currentLine || 1);
    }
    reportCursorAndSelection(element || document.activeElement);
});

viewport.addEventListener('dblclick', event => {
    if (findPanel.contains(event.target)) return;
    if (event.target.closest?.('.line-number')) return;
    if (selectWordAtPointer(event)) {
        event.preventDefault();
        event.stopPropagation();
    }
});

viewport.addEventListener('contextmenu', event => {
    if (findPanel.contains(event.target)) return;
    event.preventDefault();

    const position = positionFromPointer(event);
    const keepSelection = hasCustomSelection() && isPositionInsideSelection(position);
    if (position && !keepSelection) {
        const hadSelection = hasCustomSelection();
        state.selection = null;
        syncCustomSelectionClass();
        state.selectionAnchor = { line: position.line, column: position.column };
        state.currentLine = position.line;
        state.currentColumn = position.column + 1;
        if (position.element.getAttribute('contenteditable') === 'true') {
            setCaret(position.element, position.column);
        }
        if (hadSelection) queueRender(true);
    }

    showContextMenu(event.clientX, event.clientY);
});

document.addEventListener('copy', event => {
    if (event.target && (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA' || event.target.closest('#find-panel'))) {
        return;
    }
    const text = selectedText();
    if (text) {
        event.clipboardData?.setData('text/plain', text);
        event.preventDefault();
    }
});

document.addEventListener('cut', event => {
    if (event.target && (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA' || event.target.closest('#find-panel'))) {
        return;
    }
    const text = selectedText();
    if (!text) return;

    event.clipboardData?.setData('text/plain', text);
    event.preventDefault();
    if (state.readOnly) return;

    if (hasCustomSelection()) {
        const sel = normalizeSelection();
        if (sel) replaceSelectionWith(sel, '');
        return;
    }

    const element = activeEditableElement();
    const selection = window.getSelection();
    if (element && selection?.rangeCount && element.contains(selection.anchorNode)) {
        document.execCommand('delete');
        commitLine(element);
    }
});

document.addEventListener('paste', event => {
    if (event.target && (event.target.tagName === 'INPUT' || event.target.tagName === 'TEXTAREA' || event.target.closest('#find-panel'))) {
        return;
    }
    event.preventDefault();
    const clipboardText = (event.clipboardData?.getData('text/plain') || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
    const element = document.activeElement?.closest?.('.line-text');
    if (!element || element.getAttribute('contenteditable') !== 'true') return;

    if (hasCustomSelection()) {
        const sel = normalizeSelection();
        if (sel) {
            replaceSelectionWith(sel, clipboardText);
            return;
        }
    }

    insertTextAtCaret(clipboardText);
});

contextMenu.addEventListener('click', async event => {
    const button = event.target.closest('.context-menu-button');
    if (!button || button.disabled) return;
    const action = button.dataset.action;
    if (!action) return;

    hideContextMenu();

    switch (action) {
        case 'cut':
            await cutSelectionToClipboard();
            break;
        case 'copy':
            await copySelectionToClipboard();
            break;
        case 'paste':
            await pasteFromClipboard();
            break;
        case 'delete':
            deleteSelectionOrForward();
            break;
        case 'selectAll':
            selectAll();
            break;
        case 'toggleComment':
            toggleComment();
            break;
        case 'indentLines':
            changeLineIndent(1);
            break;
        case 'outdentLines':
            changeLineIndent(-1);
            break;
        case 'sortAsc':
        case 'sortDesc':
        case 'removeDuplicates':
        case 'removeEmptyLines':
        case 'collapseConsecutiveEmptyLines':
        case 'trimSpaces':
            handleLineSortingAndCleanup(action);
            break;
        case 'toUpperCase':
        case 'toLowerCase':
        case 'toSentenceCase':
        case 'toTitleCase':
        case 'insertDivider':
        case 'urlEncode':
        case 'urlDecode':
        case 'base64Encode':
        case 'base64Decode':
        case 'hexToDec':
        case 'decToHex':
            handleTextConversion(action);
            break;
        case 'formatText':
            handleFormatText();
            break;
        case 'toggleScrollSync':
            state.scrollSyncEnabled = !state.scrollSyncEnabled;
            post({ type: 'scrollSyncChanged', enabled: state.scrollSyncEnabled });
            break;
    }
});

document.addEventListener('pointerdown', event => {
    if (!contextMenu.hidden && !contextMenu.contains(event.target)) {
        hideContextMenu();
    }
    const popup = document.getElementById('autocomplete-popup');
    if (autocompleteState.isOpen && popup && !popup.contains(event.target)) {
        hideAutocomplete();
    }
});

const autocompletePopup = document.getElementById('autocomplete-popup');
if (autocompletePopup) {
    autocompletePopup.addEventListener('pointerdown', event => {
        event.preventDefault();
        const button = event.target.closest('.autocomplete-item');
        if (button) {
            const index = Number(button.dataset.index);
            autocompleteState.activeIndex = index;
            insertSelectedCandidate();
        }
    });
}

document.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
        hideContextMenu();
        hideAutocomplete();
    }
});

let nativeSelectionReportTimer = 0;
document.addEventListener('selectionchange', () => {
    if (state.isSelecting) return;
    clearTimeout(nativeSelectionReportTimer);
    nativeSelectionReportTimer = setTimeout(() => {
        reportCursorAndSelection(document.activeElement);
    }, 30);
});

let isSyncingScroll = false;
scrollContainer.addEventListener('scroll', () => {
    hideContextMenu();
    queueRender();
    if (state.scrollSyncEnabled && !isSyncingScroll) {
        const firstVisible = lineAt(scrollContainer.scrollTop);
        const offset = scrollContainer.scrollTop - lineTop(firstVisible);
        post({
            type: 'editorScroll',
            firstLine: firstVisible,
            offset: offset
        });
    }
});

window.addEventListener('resize', () => queueRender(true));
window.addEventListener('dragover', event => event.preventDefault(), false);
window.addEventListener('drop', event => event.preventDefault(), false);

let findDebounceTimer;
findInput.addEventListener('input', () => {
    clearTimeout(findDebounceTimer);
    findDebounceTimer = setTimeout(() => requestFindAll(), 200);
});

findInput.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
        event.preventDefault();
        requestFind(event.shiftKey);
    } else if (event.key === 'Escape') {
        event.preventDefault();
        closeFindPanel();
    }
});

findPrev.addEventListener('click', () => requestFind(true));
findNextButton.addEventListener('click', () => requestFind(false));
findClose.addEventListener('click', closeFindPanel);

const findMatchCase = document.getElementById('find-match-case');
const findRegex = document.getElementById('find-regex');

findMatchCase.addEventListener('click', () => {
    state.findMatchCase = !state.findMatchCase;
    findMatchCase.classList.toggle('active', state.findMatchCase);
    requestFindAll();
});

findRegex.addEventListener('click', () => {
    state.findRegex = !state.findRegex;
    findRegex.classList.toggle('active', state.findRegex);
    requestFindAll();
});

replaceBtn.addEventListener('click', () => executeReplace());
replaceAllBtn.addEventListener('click', () => executeReplaceAll());
replaceInput.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
        event.preventDefault();
        executeReplace();
    } else if (event.key === 'Escape') {
        event.preventDefault();
        closeFindPanel();
    }
});

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', event => {
        const msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        handleCsharpMessage(msg);
    });
}

setupVirtualHeight();
post({ type: 'ready', virtualized: true });
