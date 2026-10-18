// Helpers for the Claude assistant page.  Screenshots are only available when it is
// embedded in the remote control viewer.
// The viewer page (the parent window) owns the screen canvas, so captures are
// requested from it with postMessage.  See wwwroot/viewer-assistant.js.

let lastCapture = null;

export function isEmbeddedInViewer() {
    return window.parent !== window;
}

export function captureViewerScreen(timeoutMs) {
    lastCapture = null;

    return new Promise(resolve => {
        if (window.parent === window) {
            resolve({ ok: false, width: 0, height: 0, error: "The assistant isn't open inside a remote control session." });
            return;
        }

        const id = `${Date.now()}-${Math.random()}`;

        const timer = setTimeout(() => {
            window.removeEventListener("message", onMessage);
            resolve({ ok: false, width: 0, height: 0, error: "The viewer didn't respond." });
        }, timeoutMs);

        function onMessage(ev) {
            if (ev.origin !== location.origin || ev.source !== window.parent) {
                return;
            }
            const data = ev.data;
            if (!data || data.type !== "signalremote:screenshot" || data.id !== id) {
                return;
            }

            clearTimeout(timer);
            window.removeEventListener("message", onMessage);

            if (data.error || !data.jpegBase64) {
                resolve({ ok: false, width: 0, height: 0, error: data.error || "No screenshot available." });
                return;
            }

            lastCapture = data.jpegBase64;
            resolve({ ok: true, width: data.width, height: data.height, error: null });
        }

        window.addEventListener("message", onMessage);
        window.parent.postMessage({ type: "signalremote:screenshot-request", id }, location.origin);
    });
}

// Returned as bytes so .NET can read it as a stream.  A capture is larger than the
// SignalR message size limit for regular interop results.
export function takeCapturedScreen() {
    const capture = lastCapture ?? "";
    lastCapture = null;
    return new TextEncoder().encode(capture);
}

export function scrollToBottom(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}
