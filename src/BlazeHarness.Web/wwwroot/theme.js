// Applies the saved theme before first paint so the page does not flash the wrong colors.
(function () {
    const readStorage = (name) => {
        try { return localStorage.getItem(name); } catch { return null; }
    };
    const writeStorage = (name, value) => {
        try { localStorage.setItem(name, value); } catch { /* Storage may be disabled. */ }
    };
    const removeStorage = (name) => {
        try { localStorage.removeItem(name); } catch { /* Storage may be disabled. */ }
    };

    const key = "blazeharness.theme";
    const apply = (mode) => {
        if (mode === "light" || mode === "dark") {
            document.documentElement.dataset.theme = mode;
        } else {
            delete document.documentElement.dataset.theme;
        }
    };

    apply(readStorage(key));

    // Text size steps, as a multiplier of the default size. The interface size scales the root, so everything in rem follows.
    const steps = { small: 0.875, default: 1, large: 1.15, xlarge: 1.3 };
    const sizeKeys = { ui: "blazeharness.ui-size", chat: "blazeharness.chat-size" };
    const sizeProps = { ui: "--ui-scale", chat: "--chat-scale" };
    const applySize = (kind, step) => {
        document.documentElement.style.setProperty(sizeProps[kind], String(steps[step] ?? 1));
    };

    for (const kind of Object.keys(sizeKeys)) {
        applySize(kind, readStorage(sizeKeys[kind]));
    }

    window.bhSize = {
        get: (kind) => {
            const step = readStorage(sizeKeys[kind]);
            return step in steps ? step : "default";
        },
        set: (kind, step) => {
            if (!(kind in sizeKeys) || !(step in steps)) {
                return;
            }

            if (step === "default") {
                removeStorage(sizeKeys[kind]);
            } else {
                writeStorage(sizeKeys[kind], step);
            }
            applySize(kind, step);
        },
    };

    const chatWidthKey = "blazeharness.chat-width";
    const applyChatWidth = (full) => document.documentElement.toggleAttribute("data-chat-full-width", full);
    applyChatWidth(readStorage(chatWidthKey) === "full");

    window.bhChatWidth = {
        get: () => readStorage(chatWidthKey) === "full",
        set: (full) => {
            if (full) {
                writeStorage(chatWidthKey, "full");
            } else {
                removeStorage(chatWidthKey);
            }
            applyChatWidth(full);
        },
    };

    const diffWrapKey = "blazeharness.diff-wrap";
    const applyDiffWrap = (wrap) => document.documentElement.toggleAttribute("data-diff-wrap", wrap);
    applyDiffWrap(readStorage(diffWrapKey) === "on");

    window.bhDiffWrap = {
        get: () => readStorage(diffWrapKey) === "on",
        set: (wrap) => {
            if (wrap) {
                writeStorage(diffWrapKey, "on");
            } else {
                removeStorage(diffWrapKey);
            }
            applyDiffWrap(wrap);
        },
    };

    window.bhTheme = {
        get: () => readStorage(key) || "system",
        isDark: () => {
            const mode = readStorage(key);
            return mode ? mode === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
        },
        set: (mode) => {
            if (mode === "light" || mode === "dark") {
                writeStorage(key, mode);
            } else {
                removeStorage(key);
            }
            apply(mode);
        },
    };

    const chatSortKey = "blazeharness.chat-sort";
    window.bhChatSort = {
        get: () => readStorage(chatSortKey),
        set: (order) => writeStorage(chatSortKey, order),
    };
})();
