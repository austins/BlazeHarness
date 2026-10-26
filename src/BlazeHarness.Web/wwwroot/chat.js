// Chat page helpers: keeping the newest message in view, and the message box.
window.bhChat = {
    // Follow new content while the reader is at the bottom, and stop following once they scroll up.
    watch: (root, scroll) => {
        if (!root || !scroll) {
            return;
        }

        const nearBottom = () => scroll.scrollHeight - scroll.scrollTop - scroll.clientHeight < 40;
        const toBottom = () => { scroll.scrollTop = scroll.scrollHeight; };
        let pinned = true;
        scroll.addEventListener("scroll", () => {
            pinned = nearBottom();
            root.dataset.pinned = String(pinned);
        }, { passive: true });
        new MutationObserver(() => {
            if (pinned) {
                toBottom();
            } else {
                root.dataset.pinned = String(nearBottom());
            }
        }).observe(scroll, { childList: true, subtree: true, characterData: true });
        root.bhToBottom = () => { pinned = true; root.dataset.pinned = "true"; toBottom(); };
        toBottom();
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
                setTimeout(() => { button.textContent = "Copy"; }, 1500);
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
            input.style.height = Math.min(input.scrollHeight, 240) + "px";
            input.focus();
        }
    },
    // Enter sends, Shift+Enter adds a line; the box grows with its text.
    initComposer: (input) => {
        if (!input) {
            return;
        }

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
