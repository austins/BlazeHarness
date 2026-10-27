// Chat page helpers: keeping the newest message in view, and the message box.
const nearBottomPx = 40;
const nearTopPx = 160;
const copiedResetMs = 1500;
const maxComposerHeightPx = 240;
const longPaste = { characters: 5000, lines: 100 };
// Keep in step with PendingFile.PastedNamePrefix.
const pastedNamePrefix = "pasted-text-";

window.bhChat = {
    // Follow new content while the reader is at the bottom, and stop following once they scroll up.
    watch: (root, scroll) => {
        if (!root || !scroll) {
            return;
        }

        if (root.bhUnwatch) {
            root.bhUnwatch();
        }

        const nearBottom = () => scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight < nearBottomPx;
        const toBottom = () => { scroll.scrollTop = scroll.scrollHeight; };
        let pinned = true;
        // Older messages load when the reader scrolls near the top; the button's count tells a new batch from one already asked for.
        let requested = null;
        let anchor = null;
        const onScroll = () => {
            pinned = nearBottom();
            root.dataset.pinned = String(pinned);
            const earlier = scroll.scrollTop < nearTopPx ? scroll.querySelector("[data-earlier]") : null;
            if (earlier && requested !== earlier.dataset.earlier) {
                requested = earlier.dataset.earlier;
                anchor = { height: scroll.scrollHeight, top: scroll.scrollTop };
                earlier.click();
            }
        };
        scroll.addEventListener("scroll", onScroll, { passive: true });
        const observer = new MutationObserver(() => {
            // Browsers don't anchor at the very top, so keep the reader on the message they were reading.
            if (anchor && scroll.scrollHeight !== anchor.height) {
                scroll.scrollTop = anchor.top + scroll.scrollHeight - anchor.height;
                anchor = null;
            }

            if (pinned) {
                toBottom();
            } else {
                root.dataset.pinned = String(nearBottom());
            }
        });
        observer.observe(scroll, { childList: true, subtree: true, characterData: true });
        root.bhUnwatch = () => {
            observer.disconnect();
            scroll.removeEventListener("scroll", onScroll);
            delete root.bhUnwatch;
        };
        root.bhToBottom = () => { pinned = true; root.dataset.pinned = "true"; toBottom(); };
        toBottom();
    },
    // Finds the text in one message with the CSS Custom Highlight API, so nothing is injected into the page.
    highlight: (scroll, query, entryId) => {
        window.bhChat.clearHighlight();
        const article = scroll && scroll.querySelector(`[data-entry="${CSS.escape(entryId)}"]`);
        if (!article) {
            return;
        }

        article.scrollIntoView({ block: "center" });
        if (!window.CSS || !CSS.highlights || !query) {
            return;
        }

        const needle = query.toLowerCase();
        const ranges = [];
        const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT);
        for (let node = walker.nextNode(); node; node = walker.nextNode()) {
            const text = node.nodeValue.toLowerCase();
            for (let at = text.indexOf(needle); at >= 0; at = text.indexOf(needle, at + needle.length)) {
                const range = new Range();
                range.setStart(node, at);
                range.setEnd(node, at + needle.length);
                ranges.push(range);
            }
        }

        if (ranges.length > 0) {
            const highlight = new Highlight();
            ranges.forEach((range) => highlight.add(range));
            CSS.highlights.set("bh-find", highlight);
        }
    },
    clearHighlight: () => {
        if (window.CSS && CSS.highlights) {
            CSS.highlights.delete("bh-find");
        }
    },
    // Sending a message always brings the reader back to the newest content.
    follow: (root) => {
        if (root && root.bhToBottom) {
            root.bhToBottom();
        }
    },
    toBottom: (button) => {
        const root = button.closest(".chat");
        if (root && root.bhToBottom) {
            root.bhToBottom();
        }
    },
    copyText: async (text) => {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            return false;
        }
    },
    // Copy buttons on code blocks are rendered with the message, so one listener covers them all.
    initCopy: () => {
        document.addEventListener("click", async (e) => {
            const button = e.target instanceof Element ? e.target.closest("[data-copy]") : null;
            const code = button && button.closest(".code");
            if (!code) {
                return;
            }

            try {
                await navigator.clipboard.writeText(code.querySelector("pre").innerText);
                button.textContent = "Copied";
                setTimeout(() => { button.textContent = "Copy"; }, copiedResetMs);
            } catch {
                button.textContent = "Copy failed";
            }
        });
    },
    // Message times are rendered as UTC instants and shown in the browser's own locale and time zone.
    initTimes: () => {
        const clock = new Intl.DateTimeFormat(undefined, { hour: "numeric", minute: "2-digit" });
        const dated = new Intl.DateTimeFormat(undefined, { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
        const full = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeStyle: "medium" });
        const show = (el) => {
            const when = new Date(el.getAttribute("datetime"));
            if (Number.isNaN(when.getTime())) {
                return;
            }

            const text = when.toDateString() === new Date().toDateString() ? clock.format(when) : dated.format(when);
            if (el.textContent !== text) {
                el.textContent = text;
            }

            el.title = full.format(when);
        };
        const scan = (root) => {
            if (root.matches && root.matches("time.msg-time")) {
                show(root);
            }

            if (root.querySelectorAll) {
                root.querySelectorAll("time.msg-time").forEach(show);
            }
        };
        new MutationObserver((records) => {
            for (const record of records) {
                if (record.type === "attributes") {
                    show(record.target);
                } else {
                    record.addedNodes.forEach((node) => scan(node));
                }
            }
        }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["datetime"] });
        scan(document.documentElement);
    },
    resize: (input) => {
        if (input) {
            input.style.height = "auto";
            input.style.height = Math.min(input.scrollHeight, maxComposerHeightPx) + "px";
            input.focus();
        }
    },
    pick: (button) => {
        const picker = button.closest("form")?.querySelector("input[type=file]");
        if (picker) {
            picker.click();
        }
    },

    clearPicker: (picker) => {
        if (picker) {
            picker.value = "";
        }
    },

    // Hand files to the composer's hidden file input, which reports them to the component.
    addFiles: (form, files) => {
        const picker = form.querySelector("input[type=file]");
        if (!picker || files.length === 0) {
            return;
        }

        const transfer = new DataTransfer();
        for (const file of files) {
            transfer.items.add(file);
        }

        picker.files = transfer.files;
        picker.dispatchEvent(new Event("change", { bubbles: true }));
    },

    // Enter sends, Shift+Enter adds a line; the box grows with its text.
    initComposer: (input, form) => {
        if (!input) {
            return;
        }

        let pastes = 0;
        input.addEventListener("paste", (e) => {
            const data = e.clipboardData;
            if (!data) {
                return;
            }

            if (data.files.length > 0) {
                e.preventDefault();
                window.bhChat.addFiles(form, [...data.files]);
                return;
            }

            const text = data.getData("text/plain");
            if (text.length > longPaste.characters || text.split("\n").length > longPaste.lines) {
                e.preventDefault();
                pastes += 1;
                window.bhChat.addFiles(form, [new File([text], `${pastedNamePrefix}${pastes}.txt`, { type: "text/plain" })]);
            }
        });

        let depth = 0;
        const hasFiles = (e) => e.dataTransfer && [...e.dataTransfer.types].includes("Files");
        form.addEventListener("dragenter", (e) => {
            if (hasFiles(e)) {
                depth += 1;
                form.dataset.dragging = "true";
            }
        });
        form.addEventListener("dragover", (e) => {
            if (hasFiles(e)) {
                e.preventDefault();
            }
        });
        form.addEventListener("dragleave", () => {
            depth = Math.max(0, depth - 1);
            if (depth === 0) {
                delete form.dataset.dragging;
            }
        });
        form.addEventListener("drop", (e) => {
            if (hasFiles(e)) {
                e.preventDefault();
                depth = 0;
                delete form.dataset.dragging;
                window.bhChat.addFiles(form, [...e.dataTransfer.files]);
            }
        });

        input.addEventListener("keydown", (e) => {
            if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                const send = input.form && input.form.querySelector("button[type=submit]");
                if (send) {
                    send.click();
                }
            }
        });
        input.addEventListener("input", () => window.bhChat.resize(input));
        input.focus();
    },
};

window.bhChat.initCopy();
window.bhChat.initTimes();
