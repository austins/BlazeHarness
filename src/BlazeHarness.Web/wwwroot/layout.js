// Drag-to-resize for the left sidebar and the right side panel. Widths live in CSS variables and are kept in localStorage.
(() => {
    const kinds = {
        sidebar: { variable: "--sidebar-width", storage: "bh.sidebarWidth", target: ".sidebar", grows: 1, min: 13, max: 32 },
        panel: { variable: "--panel-width", storage: "bh.panelWidth", target: ".side-panel", grows: -1, min: 20, max: 80 },
    };
    const root = document.documentElement;
    const rem = () => parseFloat(getComputedStyle(root).fontSize) || 16;
    let drag = null;

    const limits = (kind) => {
        const spec = kinds[kind];
        let max = spec.max * rem();
        if (kind === "panel") {
            // The chat keeps at least 20rem.
            const layout = document.querySelector(".chat-layout");
            max = Math.min(max, (layout ? layout.clientWidth : window.innerWidth) - 20 * rem());
        }

        const min = spec.min * rem();
        return [min, Math.max(min, max)];
    };

    const describe = (handle, kind, width) => {
        const [min, max] = limits(kind);
        handle.setAttribute("aria-valuemin", String(Math.round(min)));
        handle.setAttribute("aria-valuemax", String(Math.round(max)));
        handle.setAttribute("aria-valuenow", String(Math.round(width)));
    };

    const apply = (kind, width, handle, save) => {
        const [min, max] = limits(kind);
        const clamped = Math.min(max, Math.max(min, width));
        root.style.setProperty(kinds[kind].variable, `${clamped}px`);
        if (handle) {
            describe(handle, kind, clamped);
        }

        if (save) {
            try {
                localStorage.setItem(kinds[kind].storage, String(Math.round(clamped)));
            } catch {
                // Storage can be blocked; the width then only lasts until the page reloads.
            }
        }

        return clamped;
    };

    const currentWidth = (kind) => document.querySelector(kinds[kind].target)?.getBoundingClientRect().width ?? 0;

    const direction = (handle, kind) => (getComputedStyle(handle).direction === "rtl" ? -1 : 1) * kinds[kind].grows;

    // Restore saved widths before the first paint. The page clamps them again on the first drag.
    for (const [kind, spec] of Object.entries(kinds)) {
        try {
            const saved = Number(localStorage.getItem(spec.storage));
            if (Number.isFinite(saved) && saved > 0) {
                root.style.setProperty(spec.variable, `${Math.min(Math.max(saved, spec.min * 16), spec.max * 16)}px`);
            }
        } catch {
            // Storage can be blocked.
        }
    }

    const endDrag = () => {
        if (!drag) {
            return;
        }

        const { kind, handle } = drag;
        drag = null;
        root.classList.remove("resizing");
        const width = currentWidth(kind);
        if (width > 0) {
            apply(kind, width, handle, true);
        }
    };

    document.addEventListener("pointerdown", (event) => {
        const handle = event.target instanceof Element ? event.target.closest("[data-resizer]") : null;
        if (!handle || event.button !== 0) {
            return;
        }

        const kind = handle.dataset.resizer;
        const width = currentWidth(kind);
        if (!kinds[kind] || width === 0) {
            return;
        }

        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
        drag = { kind, handle, startX: event.clientX, startWidth: width, sign: direction(handle, kind) };
        root.classList.add("resizing");
        describe(handle, kind, width);
    });

    document.addEventListener("pointermove", (event) => {
        if (drag) {
            apply(drag.kind, drag.startWidth + (event.clientX - drag.startX) * drag.sign, drag.handle, false);
        }
    });

    document.addEventListener("pointerup", endDrag);
    document.addEventListener("pointercancel", endDrag);
    document.addEventListener("lostpointercapture", endDrag);

    document.addEventListener("keydown", (event) => {
        const handle = event.target instanceof Element ? event.target.closest("[data-resizer]") : null;
        const kind = handle?.dataset.resizer;
        if (!handle || !kinds[kind]) {
            return;
        }

        const step = event.shiftKey ? 64 : 16;
        const sign = direction(handle, kind);
        let width = null;
        if (event.key === "ArrowRight") {
            width = currentWidth(kind) + step * sign;
        } else if (event.key === "ArrowLeft") {
            width = currentWidth(kind) - step * sign;
        } else if (event.key === "Home") {
            width = limits(kind)[0];
        } else if (event.key === "End") {
            width = limits(kind)[1];
        }

        if (width !== null) {
            event.preventDefault();
            apply(kind, width, handle, true);
        }
    });

    // Double-click puts the width back to its default.
    document.addEventListener("dblclick", (event) => {
        const handle = event.target instanceof Element ? event.target.closest("[data-resizer]") : null;
        const kind = handle?.dataset.resizer;
        if (!handle || !kinds[kind]) {
            return;
        }

        root.style.removeProperty(kinds[kind].variable);
        try {
            localStorage.removeItem(kinds[kind].storage);
        } catch {
            // Storage can be blocked.
        }

        describe(handle, kind, currentWidth(kind));
    });
})();
