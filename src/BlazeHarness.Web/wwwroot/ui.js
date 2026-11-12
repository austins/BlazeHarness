// Small helpers for UI primitives that need the DOM.
const openers = [];
const settingsAfterCultureChangeKey = "bh-settings-after-culture-change";
let rightButtonDown = false;
let pendingContextMenu = null;

document.addEventListener("pointerdown", (event) => {
    rightButtonDown = (event.buttons & 2) !== 0;
}, true);
document.addEventListener("pointerup", (event) => {
    rightButtonDown = (event.buttons & 2) !== 0;
    if (!rightButtonDown && pendingContextMenu) {
        const show = pendingContextMenu;
        pendingContextMenu = null;
        // Firefox light-dismisses popovers on right-button release; open after that event finishes.
        requestAnimationFrame(show);
    }
}, true);
window.addEventListener("blur", () => {
    rightButtonDown = false;
    pendingContextMenu = null;
});

window.bhUi = {
    // Keep in sync with the @media (max-width: 48rem) breakpoint in app.css.
    isNarrow: () => window.matchMedia("(max-width: 48rem)").matches,
    focusSelect: (input) => {
        if (input) {
            input.focus();
            input.select();
        }
    },
    setCulture: async (culture) => {
        // The server sets the cookie: one written to document.cookie can miss the reload that follows it.
        await fetch(`api/culture/${encodeURIComponent(culture)}`, { method: "POST" });
        try {
            sessionStorage.setItem(settingsAfterCultureChangeKey, "true");
        } catch {
            // Storage can be blocked; settings then stay closed after the reload.
        }
        window.location.reload();
    },
    restoreSettingsAfterCultureChange: () => {
        try {
            const reopen = sessionStorage.getItem(settingsAfterCultureChangeKey) === "true";
            sessionStorage.removeItem(settingsAfterCultureChangeKey);
            return reopen;
        } catch {
            // Storage can be blocked.
            return false;
        }
    },
    skipToChat: () => {
        const target = document.querySelector(".composer-input") ?? document.querySelector("main");
        if (target) {
            target.focus();
        }
    },
    // A dialog is removed from the page when it closes, so the browser can't return focus to what opened it.
    showModal: (dialog) => {
        if (dialog && !dialog.open) {
            openers.push(document.activeElement);
            dialog.showModal();
            // A click on the dialog element itself lands on its backdrop; ignore drags that started inside it.
            let pressedOnBackdrop = false;
            dialog.addEventListener("mousedown", (e) => { pressedOnBackdrop = e.target === dialog; });
            dialog.addEventListener("click", (e) => {
                if (e.target === dialog && pressedOnBackdrop) {
                    dialog.dispatchEvent(new Event("cancel", { cancelable: true }));
                }
            });
        }
    },
    // Opens a popover menu at the pointer, then nudges it back inside the viewport.
    showContextMenu: (menu, x, y) => {
        const show = () => {
            if (!menu?.isConnected) {
                return;
            }

            if (menu.matches(":popover-open")) {
                menu.hidePopover();
            }

            menu.style.left = `${x}px`;
            menu.style.top = `${y}px`;
            menu.showPopover();
            const box = menu.getBoundingClientRect();
            menu.style.left = `${Math.max(0, Math.min(x, window.innerWidth - box.width))}px`;
            menu.style.top = `${Math.max(0, Math.min(y, window.innerHeight - box.height))}px`;
        };

        if (rightButtonDown) {
            pendingContextMenu = show;
        } else {
            show();
        }
    },
    restoreFocus: () => {
        const opener = openers.pop();
        if (opener instanceof HTMLElement && opener.isConnected) {
            opener.focus();
        }
    },
};

// Global shortcuts. Ctrl+K and Ctrl+Shift+O are ones browsers let a page take over.
document.addEventListener("keydown", (event) => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey) {
        return;
    }

    const key = event.key.toLowerCase();
    let target = null;
    if (key === "f" && !event.shiftKey && document.activeElement && document.activeElement.closest(".chat")) {
        // Only while the chat has focus; elsewhere the browser's own find stays.
        target = document.querySelector("[data-shortcut=find]");
    } else if (key === "k" && !event.shiftKey) {
        // The box is only there while the search is open; otherwise the toggle opens it.
        target = document.querySelector("[data-shortcut=search-input]") ?? document.querySelector("[data-shortcut=search]");
    } else if (key === "o" && event.shiftKey) {
        target = document.querySelector("[data-shortcut=new-chat]");
    } else if (key === "," && !event.shiftKey) {
        target = document.querySelector("[data-shortcut=settings]");
    }

    if (target) {
        event.preventDefault();
        if (target.tagName === "INPUT") {
            target.focus();
            target.select();
        } else {
            target.click();
        }
    }
});

// Click-to-enlarge for chat images. The image opens in a dialog, not by navigating to the file, so an SVG is never opened as a page.
document.addEventListener("click", (event) => {
    const image = event.target instanceof Element ? event.target.closest(".chat-thread img.msg-image") : null;
    if (!image || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey) {
        return;
    }

    event.preventDefault();
    let dialog = document.querySelector("dialog.lightbox");
    if (!dialog) {
        dialog = document.createElement("dialog");
        dialog.className = "lightbox";
        dialog.setAttribute("aria-label", document.documentElement.dataset.lightboxLabel);
        const big = document.createElement("img");
        const close = document.createElement("button");
        close.type = "button";
        close.className = "btn btn-sm lightbox-close";
        close.textContent = document.documentElement.dataset.closeLabel;
        close.addEventListener("click", () => dialog.close());
        dialog.addEventListener("click", (e) => {
            if (e.target === dialog) {
                dialog.close();
            }
        });
        dialog.append(big, close);
        document.body.append(dialog);
    }

    const big = dialog.querySelector("img");
    big.src = image.currentSrc || image.src;
    big.alt = image.alt;
    dialog.showModal();
});

// Menus are popovers, so the browser closes them on Escape or a click outside; these add the focus and arrow keys a menu needs.
const menuItems = (menu) => [...menu.querySelectorAll(".menu-item:not(:disabled)")];
document.addEventListener("toggle", (event) => {
    const menu = event.target;
    if (event.newState === "open" && menu instanceof HTMLElement && menu.matches("[data-bh-menu]")) {
        const items = menuItems(menu);
        (items.find((item) => item.getAttribute("aria-checked") === "true") ?? items[0])?.focus();
    }
}, true);
document.addEventListener("keydown", (event) => {
    const menu = event.target instanceof Element ? event.target.closest("[data-bh-menu]") : null;
    if (!menu) {
        return;
    }

    const items = menuItems(menu);
    const at = items.indexOf(document.activeElement);
    const next = {
        ArrowDown: items[(at + 1) % items.length],
        ArrowUp: items[(at - 1 + items.length) % items.length],
        Home: items[0],
        End: items[items.length - 1],
    }[event.key];
    if (next) {
        event.preventDefault();
        next.focus();
    }
});

window.bhChanges = {
    resetScroll: () => document.querySelector(".changes-diff")?.scrollTo(0, 0),
};

// Dragging down the line numbers of a diff block picks the lines for one comment. The drag ends as two clicks the
// diff view already understands: a click on the first line, then a Shift-click on the last. A click or tap on one line
// number comments on that line, so touch screens, which have no hover to show the button, can start a comment too.
(() => {
    let start = null;
    let end = null;
    let onButton = false;

    const lineAt = (target) => target instanceof Element ? target.closest(".gdiff-line") : null;
    const rowsOf = (line) => [...line.parentElement.querySelectorAll(`.gdiff-line[data-hunk="${line.dataset.hunk}"]`)];
    const mark = () => {
        const rows = rowsOf(start);
        const [from, to] = [rows.indexOf(start), rows.indexOf(end)].sort((a, b) => a - b);
        rows.forEach((row, i) => row.toggleAttribute("data-dragging", i >= from && i <= to));
    };
    const clear = () => {
        document.querySelectorAll(".gdiff-line[data-dragging]").forEach((row) => row.removeAttribute("data-dragging"));
        start = end = null;
    };

    document.addEventListener("mousedown", (event) => {
        const line = lineAt(event.target);
        if (event.button !== 0 || event.shiftKey || !line || !event.target.closest(".gdiff-num, .gdiff-gutter")) {
            return;
        }

        // Keep the browser from selecting text while the pointer moves.
        event.preventDefault();
        start = end = line;
        onButton = !!event.target.closest(".gdiff-add");
    });

    document.addEventListener("mouseover", (event) => {
        const line = start && lineAt(event.target);
        if (line && line !== end && line.dataset.hunk === start.dataset.hunk && line.parentElement === start.parentElement) {
            end = line;
            mark();
        }
    });

    document.addEventListener("mouseup", () => {
        if (start && end && start !== end) {
            start.querySelector(".gdiff-add")?.click();
            end.querySelector(".gdiff-add")?.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, shiftKey: true }));
        } else if (start && !onButton) {
            start.querySelector(".gdiff-add")?.click();
        }

        if (start) {
            clear();
        }
    });

    // Selecting text across lines of one block picks them too. A labeled button floats under the selection; it is
    // separate from the per-line buttons so it is clear that it comments on every selected line.
    let picked = null;
    let pill = null;

    const nodeLine = (node) => lineAt(node instanceof Element ? node : node?.parentElement);
    const numberOf = (row, column) => Number(row.querySelectorAll(".gdiff-num")[column]?.textContent) || null;

    /** Gets the numbers the server-side range label would show: old numbers for removed lines only, else new ones. */
    const span = (rows) => {
        const removedOnly = rows.every((row) => row.dataset.kind === "removed");
        const numbers = rows
            .filter((row) => removedOnly || row.dataset.kind !== "removed")
            .map((row) => numberOf(row, removedOnly ? 0 : 1))
            .filter((n) => n !== null);
        return numbers.length ? [numbers[0], numbers[numbers.length - 1]] : null;
    };

    const unpick = () => {
        document.querySelectorAll(".gdiff-line[data-picked]").forEach((row) => row.removeAttribute("data-picked"));
        pill?.remove();
        pill = picked = null;
    };

    const place = () => {
        if (!pill || !picked) {
            return;
        }

        const row = picked[picked.length - 1].getBoundingClientRect();
        const code = picked[picked.length - 1].querySelector(".gdiff-code")?.getBoundingClientRect() ?? row;
        pill.style.left = `${Math.max(code.left, 8)}px`;
        pill.style.top = `${row.bottom + 4}px`;
    };

    document.addEventListener("selectionchange", () => {
        const selection = document.getSelection();
        const a = selection && !selection.isCollapsed && !start ? nodeLine(selection.anchorNode) : null;
        const b = a ? nodeLine(selection.focusNode) : null;
        if (!a || !b || a === b || a.dataset.hunk !== b.dataset.hunk || a.parentElement !== b.parentElement) {
            unpick();
            return;
        }

        const rows = rowsOf(a);
        const [from, to] = [rows.indexOf(a), rows.indexOf(b)].sort((x, y) => x - y);
        const next = rows.slice(from, to + 1);
        const numbers = span(next);
        const diff = a.closest(".gdiff");
        const pattern = diff?.dataset.pickLabel;
        const template = diff?.querySelector(".gdiff-pick-template");
        if (!numbers || !pattern || !template) {
            unpick();
            return;
        }

        if (picked && picked.length === next.length && picked.every((row, i) => row === next[i])) {
            return;
        }

        unpick();
        picked = next;
        picked.forEach((row) => row.setAttribute("data-picked", ""));
        pill = template.firstElementChild.cloneNode(true);
        pill.querySelector("span").textContent = pattern.replace("{first}", numbers[0]).replace("{last}", numbers[1]);
        // Keep the text selection alive while the button is pressed.
        pill.addEventListener("mousedown", (event) => event.preventDefault());
        pill.addEventListener("click", () => {
            const rows = picked;
            document.getSelection()?.removeAllRanges();
            rows[0].querySelector(".gdiff-add")?.click();
            rows[rows.length - 1].querySelector(".gdiff-add")?.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, shiftKey: true }));
        });
        document.body.append(pill);
        place();
    });

    document.addEventListener("scroll", place, true);
    window.addEventListener("resize", place);
})();

// j and k move through the changed files of the open Changes panel, like the arrow keys of a list.
document.addEventListener("keydown", (event) => {
    if ((event.key !== "j" && event.key !== "k") || event.ctrlKey || event.metaKey || event.altKey || !(event.target instanceof Element)) {
        return;
    }

    const typing = event.target.closest("input, textarea, select, [contenteditable]");
    if (typing || !event.target.closest(".side-panel")) {
        return;
    }

    const files = [...document.querySelectorAll(".side-panel .changes-file")];
    const at = files.findIndex((file) => file.getAttribute("aria-current") === "true");
    const next = files[at + (event.key === "j" ? 1 : -1)];
    if (next) {
        event.preventDefault();
        next.click();
        next.scrollIntoView({ block: "nearest" });
        if (event.target.classList.contains("changes-file")) {
            next.focus();
        }
    }
});
