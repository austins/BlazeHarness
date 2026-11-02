// Small helpers for UI primitives that need the DOM.
const openers = [];

window.bhUi = {
    // Keep in sync with the @media (max-width: 48rem) breakpoint in app.css.
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
        if (!menu) {
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
