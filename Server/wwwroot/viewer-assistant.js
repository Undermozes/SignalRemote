// Claude assistant side panel for the remote control viewer.  The panel is an iframe of
// /assistant/{deviceId}/embed.  It asks this page for screenshots of the remote screen
// with postMessage; see wwwroot/assistant-embed.js.

(() => {
    const maxLongEdge = 1568;
    const jpegQuality = 0.75;

    const button = document.getElementById("assistantButton");
    const panel = document.getElementById("assistantPanel");
    const frame = document.getElementById("assistantFrame");
    const closeButton = document.getElementById("assistantCloseButton");

    if (!button || !panel || !frame) {
        return;
    }

    function setOpen(open) {
        // The touch keyboard grabs focus back whenever it loses it, which would
        // make typing in the assistant impossible.  Turn it off first.
        const keyboardButton = document.getElementById("keyboardButton");
        if (open && keyboardButton?.classList.contains("toggled")) {
            keyboardButton.click();
        }

        if (open && !frame.src) {
            frame.src = frame.dataset.src;
        }
        panel.hidden = !open;
        document.body.classList.toggle("assistant-open", open);
        button.classList.toggle("toggled", open);
        // Lets the viewer recalculate fit-to-screen sizing.
        window.dispatchEvent(new Event("resize"));
    }

    button.addEventListener("click", () => setOpen(panel.hidden));
    closeButton?.addEventListener("click", () => setOpen(false));

    function capture() {
        const canvas = document.getElementById("screenViewer");
        if (!canvas || !canvas.width || !canvas.height) {
            return { error: "The remote screen isn't showing yet." };
        }

        const scale = Math.min(1, maxLongEdge / Math.max(canvas.width, canvas.height));
        const width = Math.round(canvas.width * scale);
        const height = Math.round(canvas.height * scale);

        const scaled = document.createElement("canvas");
        scaled.width = width;
        scaled.height = height;
        const context = scaled.getContext("2d");
        context.drawImage(canvas, 0, 0, width, height);

        const dataUrl = scaled.toDataURL("image/jpeg", jpegQuality);
        return { jpegBase64: dataUrl.substring(dataUrl.indexOf(",") + 1), width, height };
    }

    window.addEventListener("message", ev => {
        if (ev.origin !== location.origin || ev.source !== frame.contentWindow) {
            return;
        }
        const data = ev.data;
        if (!data || data.type !== "signalremote:screenshot-request") {
            return;
        }

        let result;
        try {
            result = capture();
        } catch (err) {
            result = { error: `Capture failed: ${err}` };
        }

        frame.contentWindow.postMessage({ type: "signalremote:screenshot", id: data.id, ...result }, location.origin);
    });
})();
