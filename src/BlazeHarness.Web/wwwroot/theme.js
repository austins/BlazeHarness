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
