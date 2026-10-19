// Drives the reconnect dialog in App.razor (same flow as the .NET 10 template's ReconnectModal).
(() => {
    const modal = document.getElementById("components-reconnect-modal");
    if (!modal) {
        return;
    }

    const retryWhenVisible = async () => {
        if (document.visibilityState === "visible") {
            await retry();
        }
    };

    async function retry() {
        document.removeEventListener("visibilitychange", retryWhenVisible);
        try {
            if (await Blazor.reconnect()) {
                return;
            }
            if (typeof Blazor.resumeCircuit === "function" && await Blazor.resumeCircuit()) {
                modal.close();
                return;
            }
            location.reload();
        } catch {
            document.addEventListener("visibilitychange", retryWhenVisible);
        }
    }

    async function resume() {
        try {
            if (!await Blazor.resumeCircuit()) {
                location.reload();
            }
        } catch {
            modal.classList.replace("components-reconnect-paused", "components-reconnect-resume-failed");
        }
    }

    modal.addEventListener("components-reconnect-state-changed", ev => {
        switch (ev.detail.state) {
            case "show":
                if (!modal.open) {
                    modal.showModal();
                }
                break;
            case "hide":
                modal.close();
                break;
            case "failed":
                // A phone that slept: try again as soon as the page is visible.
                document.addEventListener("visibilitychange", retryWhenVisible);
                break;
            case "rejected":
                location.reload();
                break;
        }
    });

    document.getElementById("components-reconnect-button")?.addEventListener("click", retry);
    document.getElementById("components-resume-button")?.addEventListener("click", resume);
})();
