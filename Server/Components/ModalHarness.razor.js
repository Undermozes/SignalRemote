/**
 * Opens the native <dialog> as a modal.
 * @param {HTMLDialogElement} modal
 */
export function showModal(modal) {
    if (!modal) {
        return;
    }

    if (!modal.dataset.backdropCloseBound) {
        // A click on the dialog element itself (not its content) is a click on the backdrop.
        modal.addEventListener("click", ev => {
            if (ev.target === modal) {
                modal.close();
            }
        });
        modal.dataset.backdropCloseBound = "true";
    }

    if (!modal.open) {
        modal.showModal();
    }
}

/**
 * Closes the native <dialog>.
 * @param {HTMLDialogElement} modal
 */
export function closeModal(modal) {
    if (modal?.open) {
        modal.close();
    }
}
