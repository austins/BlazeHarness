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
