// Browser panel: draws the pictures of the page the assistant has open, and passes the user's clicks, keys and scrolling on to it.
// Keep in step with PuppeteerBrowserEngine's list of keys.
const forwardedKeys = new Set([
    "Enter", "Backspace", "Delete", "Escape", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown",
]);

// The pictures come over a WebSocket of their own as JPEG messages, not through the Blazor circuit. When pictures arrive
// faster than they decode, only the newest one waits, so the panel never falls behind the page.
const openStream = (canvas, address) => {
    const url = new URL(address, document.baseURI);
    url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
    const context = canvas.getContext("2d");
    let closed = false;
    let busy = false;
    let waiting = null;
    let socket = null;
    let retry = 0;

    const draw = async blob => {
        busy = true;
        try {
            const bitmap = await createImageBitmap(blob);
            if (!closed) {
                if (canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
                    canvas.width = bitmap.width;
                    canvas.height = bitmap.height;
                }

                context.drawImage(bitmap, 0, 0);
            }

            bitmap.close();
        } catch {
            // A picture that can't be decoded is skipped; the next one replaces it.
        } finally {
            busy = false;
            if (waiting && !closed) {
                const next = waiting;
                waiting = null;
                draw(next);
            }
        }
    };
    const connect = () => {
        socket = new WebSocket(url);
        socket.binaryType = "blob";
        socket.onmessage = event => {
            if (busy) {
                waiting = event.data;
            } else {
                draw(event.data);
            }
        };
        socket.onclose = () => {
            if (!closed) {
                retry = setTimeout(connect, 1000);
            }
        };
    };

    connect();
    return () => {
        closed = true;
        clearTimeout(retry);
        socket.close();
    };
};

let active = null;

window.bhBrowser = {
    bind: (view, image, target, streamAddress) => {
        window.bhBrowser.stop();
        if (!view || !image || !target || !streamAddress) {
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
        // The pointer moves far faster than the page can follow, so one move is in flight at a time and the newest one waits behind it.
        let moving = false;
        let latest = null;
        const sendMove = async () => {
            moving = true;
            while (latest) {
                const at = latest;
                latest = null;
                try {
                    await target.invokeMethodAsync("Hover", at[0], at[1]);
                } catch {
                    // The circuit went away; the panel is being closed.
                    latest = null;
                }
            }

            moving = false;
        };
        const onMove = event => {
            // A finger dragging the picture scrolls it; hovering is for a mouse or pen.
            if (event.pointerType === "touch") {
                return;
            }

            latest = fractions(event);
            if (latest && !moving) {
                sendMove();
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
        view.addEventListener("pointermove", onMove);
        const closeStream = openStream(image, streamAddress);
        active = () => {
            view.removeEventListener("click", onClick);
            view.removeEventListener("keydown", onKeyDown);
            view.removeEventListener("wheel", onWheel);
            view.removeEventListener("pointermove", onMove);
            latest = null;
            closeStream();
        };
    },

    stop: () => {
        if (active) {
            active();
            active = null;
        }
    },
};
