// Small helpers for UI primitives that need the DOM.
window.bhUi = {
    isNarrow: () => window.matchMedia("(max-width: 48rem)").matches,
    focusSelect: (input) => {
        if (input) {
            input.focus();
            input.select();
        }
    },
    skipToChat: () => {
        const target = document.querySelector(".composer-input") ?? document.querySelector("main");
        if (target) {
            target.focus();
        }
    },
    showModal: (dialog) => {
        if (dialog && !dialog.open) {
            dialog.showModal();
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
        target = document.querySelector("[data-shortcut=search]");
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
        dialog.setAttribute("aria-label", "Enlarged image");
        const big = document.createElement("img");
        const close = document.createElement("button");
        close.type = "button";
        close.className = "btn btn-sm lightbox-close";
        close.textContent = "Close";
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
