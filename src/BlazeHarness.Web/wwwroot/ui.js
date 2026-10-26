// Small helpers for UI primitives that need the DOM.
window.bhUi = {
    isNarrow: () => window.matchMedia("(max-width: 48rem)").matches,
    focusSelect: (input) => {
        if (input) {
            input.focus();
            input.select();
        }
    },
    showModal: (dialog) => {
        if (dialog && !dialog.open) {
            dialog.showModal();
        }
    },
};
