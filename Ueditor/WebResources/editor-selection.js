function normalizeSelection(selection = state.selection) {
    if (!selection) return null;
    const a = selection.start;
    const b = selection.end;
    if (a.line < b.line || (a.line === b.line && a.column <= b.column)) {
        return { start: a, end: b, isColumn: !!selection.isColumn };
    }
    return { start: b, end: a, isColumn: !!selection.isColumn };
}

function hasCustomSelection() {
    const normalized = normalizeSelection();
    return !!normalized &&
        (normalized.start.line !== normalized.end.line ||
            normalized.start.column !== normalized.end.column);
}

function activeColumnSelection() {
    const selection = normalizeSelection();
    return selection && selection.isColumn && hasCustomSelection() ? selection : null;
}

function cloneEditorSelection(selection) {
    if (!selection) return null;
    return {
        start: { line: selection.start.line, column: selection.start.column },
        end: { line: selection.end.line, column: selection.end.column },
        isColumn: !!selection.isColumn
    };
}

function isPositionInsideSelection(position) {
    const selection = normalizeSelection();
    if (!selection || !position) return false;
    if (position.line < selection.start.line || position.line > selection.end.line) return false;
    if (position.line === selection.start.line && position.column < selection.start.column) return false;
    if (position.line === selection.end.line && position.column > selection.end.column) return false;
    return true;
}

function selectionBoundsForLine(lineNumber, textLength) {
    const selection = normalizeSelection();
    if (!selection || lineNumber < selection.start.line || lineNumber > selection.end.line) {
        return null;
    }

    if (selection.isColumn) {
        const start = Math.min(selection.start.column, selection.end.column);
        const end = Math.max(selection.start.column, selection.end.column);
        return {
            start: Math.max(0, Math.min(start, textLength)),
            end: Math.max(0, Math.min(end, textLength))
        };
    }

    const rawStart = lineNumber === selection.start.line ? selection.start.column : 0;
    const rawEnd = lineNumber === selection.end.line ? selection.end.column : textLength;
    const start = Math.max(0, Math.min(rawStart, textLength));
    const end = Math.max(0, Math.min(rawEnd, textLength));

    const spansMultipleLines = selection.start.line !== selection.end.line;
    const isEndBoundaryAtLineStart = spansMultipleLines &&
        lineNumber === selection.end.line &&
        selection.end.column <= 0;
    if (start === end && isEndBoundaryAtLineStart) {
        return null;
    }

    return { start, end };
}

function drawEditableSelectionOverlays() {
    viewport.querySelectorAll('.editable-selection-overlay').forEach(el => el.remove());

    const selection = normalizeSelection();
    if (!selection || !hasCustomSelection()) return;

    const activeElement = document.activeElement?.closest?.('.line-text') || null;
    for (const element of viewport.querySelectorAll('.line-text[contenteditable="true"]')) {
        const lineNumber = Number(element.dataset.line || 0);
        if (!lineNumber) continue;

        if (state.editingLine !== lineNumber && activeElement !== element) continue;

        const text = lineTextFromElement(element);
        const bounds = selectionBoundsForLine(lineNumber, text.length);
        if (!bounds) continue;

        const start = Math.max(0, Math.min(bounds.start, text.length));
        const end = Math.max(0, Math.min(bounds.end, text.length));

        if (start === end) {
            drawEditableColumnCursorOverlay(element, start);
            continue;
        }

        drawEditableSelectionRangeOverlay(element, start, end);
    }
}

function drawEditableSelectionRangeOverlay(element, start, end) {
    const row = element.closest('.line-row');
    const textNode = [...element.childNodes].find(node => node.nodeType === Node.TEXT_NODE);
    if (!row || !textNode) return;

    const _ = row.offsetHeight;

    const length = textNode.textContent.length;
    const safeStart = Math.max(0, Math.min(start, length));
    const safeEnd = Math.max(safeStart, Math.min(end, length));
    if (safeStart === safeEnd) return;

    const range = document.createRange();
    range.setStart(textNode, safeStart);
    range.setEnd(textNode, safeEnd);

    const rowRect = row.getBoundingClientRect();
    const startBoundary = caretBoundaryRect(textNode, safeStart, false);
    const endBoundary = caretBoundaryRect(textNode, safeEnd, true);
    const sameVisualRow = (a, b) => a && b && Math.abs(a.top - b.top) < 2;
    const rects = [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0);

    for (const rect of rects) {
        let left = rect.left;
        let right = rect.right;

        if (sameVisualRow(rect, startBoundary)) {
            left = Math.max(left, startBoundary.left);
        }
        if (sameVisualRow(rect, endBoundary)) {
            right = Math.min(right, endBoundary.left);
        }

        if (right > left) {
            appendEditableSelectionOverlay(row, left - rowRect.left, rect.top - rowRect.top, right - left, rect.height);
        }
    }

    range.detach?.();
}

function caretBoundaryRect(textNode, offset, preferPrevious = false) {
    const length = textNode?.textContent?.length || 0;
    if (!textNode || length === 0) return null;

    const safeOffset = Math.max(0, Math.min(Number(offset || 0), length));
    const range = document.createRange();

    try {
        if (safeOffset < length && !preferPrevious) {
            range.setStart(textNode, safeOffset);
            range.setEnd(textNode, safeOffset + 1);
            const rect = firstUsableRect(range);
            if (rect) {
                return { left: rect.left, top: rect.top, bottom: rect.bottom, height: rect.height };
            }
        }

        if (safeOffset > 0) {
            range.setStart(textNode, safeOffset - 1);
            range.setEnd(textNode, safeOffset);
            const rect = lastUsableRect(range);
            if (rect) {
                return { left: rect.right, top: rect.top, bottom: rect.bottom, height: rect.height };
            }
        }

        if (safeOffset < length) {
            range.setStart(textNode, safeOffset);
            range.setEnd(textNode, safeOffset + 1);
            const rect = firstUsableRect(range);
            if (rect) {
                return { left: rect.left, top: rect.top, bottom: rect.bottom, height: rect.height };
            }
        }
    } finally {
        range.detach?.();
    }

    return null;
}

function firstUsableRect(range) {
    return [...range.getClientRects()].find(rect => rect.width > 0 && rect.height > 0) || null;
}

function lastUsableRect(range) {
    const rects = [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0);
    return rects.length ? rects[rects.length - 1] : null;
}

function drawEditableColumnCursorOverlay(element, column) {
    const selection = normalizeSelection();
    if (!selection?.isColumn) return;

    const row = element.closest('.line-row');
    if (!row) return;

    const textNode = [...element.childNodes].find(node => node.nodeType === Node.TEXT_NODE);
    const rowRect = row.getBoundingClientRect();
    const lineRect = element.getBoundingClientRect();
    const height = Math.max(1, Math.min(lineRect.height, state.lineHeight));

    if (!textNode || textNode.textContent.length === 0) {
        appendEditableSelectionOverlay(row, lineRect.left - rowRect.left, lineRect.top - rowRect.top, 2, height, 'column-cursor-overlay');
        return;
    }

    const length = textNode.textContent.length;
    const safeColumn = Math.max(0, Math.min(column, length));
    const range = document.createRange();
    range.setStart(textNode, safeColumn);
    range.setEnd(textNode, safeColumn);

    let rect = range.getBoundingClientRect();
    if ((!rect || (rect.width === 0 && rect.height === 0)) && safeColumn > 0) {
        range.setStart(textNode, safeColumn - 1);
        range.setEnd(textNode, safeColumn);
        const prevRect = range.getBoundingClientRect();
        rect = {
            left: prevRect.right,
            top: prevRect.top,
            height: prevRect.height || state.lineHeight
        };
    }

    if (rect && rect.height > 0) {
        appendEditableSelectionOverlay(row, rect.left - rowRect.left, rect.top - rowRect.top, 2, rect.height, 'column-cursor-overlay');
    }

    range.detach?.();
}

function appendEditableSelectionOverlay(row, left, top, width, height, extraClass = '') {
    const overlay = document.createElement('div');
    overlay.className = `editable-selection-overlay${extraClass ? ' ' + extraClass : ''}`;
    overlay.style.left = `${Math.max(0, left)}px`;
    overlay.style.top = `${Math.max(0, top)}px`;
    overlay.style.width = `${Math.max(1, width)}px`;
    overlay.style.height = `${Math.max(1, height)}px`;
    row.appendChild(overlay);
}
