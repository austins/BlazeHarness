// Browser panel: passes the user's clicks, keys and scrolling on to the page the assistant has open.
// Keep in step with PuppeteerBrowserEngine's list of keys.
const forwardedKeys = new Set([
    "Enter", "Backspace", "Delete", "Escape", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown",
]);

window.bhBrowser = {
    bind: (view, image, target) => {
        if (!view || !image || !target || view.bhUnbind) {
            return;
        }

        // Fractions of the picture, which shows the whole viewport.
        const fractions = event => {
            const box = image.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) {
                return null;
            }

            return [
                Math.min(Math.max((event.clientX - box.left) / box.width, 0), 1),
                Math.min(Math.max((event.clientY - box.top) / box.height, 0), 1),
            ];
        };
        const onClick = event => {
            const at = fractions(event);
            if (at) {
                view.focus();
                target.invokeMethodAsync("Click", at[0], at[1]);
            }
        };
        const onKeyDown = event => {
            if (event.ctrlKey || event.metaKey || event.altKey) {
                return;
            }

            if (event.key.length === 1 || forwardedKeys.has(event.key)) {
                event.preventDefault();
                target.invokeMethodAsync("Key", event.key);
            }
        };
        const onWheel = event => {
            const at = fractions(event);
            if (at) {
                event.preventDefault();
                const pixels = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaY;
                target.invokeMethodAsync("Scroll", at[0], at[1], pixels);
            }
        };

        view.addEventListener("click", onClick);
        view.addEventListener("keydown", onKeyDown);
        view.addEventListener("wheel", onWheel, { passive: false });
        view.bhUnbind = () => {
            view.removeEventListener("click", onClick);
            view.removeEventListener("keydown", onKeyDown);
            view.removeEventListener("wheel", onWheel);
            view.bhUnbind = null;
        };
    },

    unbind: view => {
        if (view && view.bhUnbind) {
            view.bhUnbind();
        }
    },
};
