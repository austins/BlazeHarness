// Applies the saved theme before first paint so the page does not flash the wrong colors.
(function () {
    const key = "blazeharness.theme";
    const apply = (mode) => {
        if (mode === "light" || mode === "dark") {
            document.documentElement.dataset.theme = mode;
        } else {
            delete document.documentElement.dataset.theme;
        }
    };

    apply(localStorage.getItem(key));

    // Text size steps, as a multiplier of the default size. The interface size scales the root, so everything in rem follows.
    const steps = { small: 0.875, default: 1, large: 1.15, xlarge: 1.3 };
    const sizeKeys = { ui: "blazeharness.ui-size", chat: "blazeharness.chat-size" };
    const sizeProps = { ui: "--ui-scale", chat: "--chat-scale" };
    const applySize = (kind, step) => {
        document.documentElement.style.setProperty(sizeProps[kind], String(steps[step] ?? 1));
    };

    for (const kind of Object.keys(sizeKeys)) {
        applySize(kind, localStorage.getItem(sizeKeys[kind]));
    }

    window.bhSize = {
        get: (kind) => {
            const step = localStorage.getItem(sizeKeys[kind]);
            return step in steps ? step : "default";
        },
        set: (kind, step) => {
            if (!(kind in sizeKeys) || !(step in steps)) {
                return;
            }

            if (step === "default") {
                localStorage.removeItem(sizeKeys[kind]);
            } else {
                localStorage.setItem(sizeKeys[kind], step);
            }
            applySize(kind, step);
        },
    };

    window.bhTheme = {
        get: () => localStorage.getItem(key) || "system",
        isDark: () => {
            const mode = localStorage.getItem(key);
            return mode ? mode === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
        },
        set: (mode) => {
            if (mode === "light" || mode === "dark") {
                localStorage.setItem(key, mode);
            } else {
                localStorage.removeItem(key);
            }
            apply(mode);
        },
    };
})();
