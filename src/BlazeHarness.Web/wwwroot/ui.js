// Small helpers for UI primitives that need the DOM.
window.bhUi = {
    isNarrow: () => window.matchMedia("(max-width: 48rem)").matches,
    showModal: (dialog) => {
        if (dialog && !dialog.open) {
            dialog.showModal();
        }
    },
};
