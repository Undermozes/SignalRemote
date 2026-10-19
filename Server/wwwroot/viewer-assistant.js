// Claude assistant panel for the remote control viewer.  The panel is an iframe of
// /assistant/{deviceId}/embed.  It asks this page for screenshots of the remote screen
// with postMessage; see wwwroot/assistant-embed.js.
//
// Below lg (1024px) the panel is a bottom sheet with three heights: closed, half and full.
// Drag the handle/header to resize, tap the handle or the size button to switch half/full.
// From lg up it's a side panel.  Geometry is in css/remote-control.css.

(() => {
    const maxLongEdge = 1568;
    const jpegQuality = 0.75;
    const sidePanelQuery = window.matchMedia("(min-width: 1024px)");

    const button = document.getElementById("assistantButton");
    const panel = document.getElementById("assistantPanel");
    const frame = document.getElementById("assistantFrame");
    const closeButton = document.getElementById("assistantCloseButton");
    const sizeButton = document.getElementById("assistantSizeButton");
    const handle = document.getElementById("assistantSheetHandle");
    const header = panel?.querySelector(".assistant-panel-header");

    if (!button || !panel || !frame) {
        return;
    }

    const isSheet = () => !sidePanelQuery.matches;

    function viewportHeight() {
        return window.visualViewport?.height || window.innerHeight;
    }

    function sheetHeight(size) {
        const height = viewportHeight();
        return size === "full" ? height : Math.round(height * 0.5);
    }

    // Applies the current size: --assistant-h for the sheet, body classes for the layout, and
    // --assistant-inset so the floating toolbar button stays above the sheet.
    function applySize() {
        const open = !panel.hidden;
        const size = panel.dataset.size === "full" ? "full" : "half";
        const root = document.documentElement.style;

        root.setProperty("--assistant-h", `${sheetHeight(size)}px`);
        root.setProperty("--assistant-inset", open && isSheet() ? `${sheetHeight(size)}px` : "0px");
        document.body.classList.toggle("assistant-half", open && size === "half");
        document.body.classList.toggle("assistant-full", open && size === "full");

        const icon = sizeButton?.querySelector("i");
        if (icon) {
            icon.className = size === "full"
                ? "fas fa-down-left-and-up-right-to-center"
                : "fas fa-up-right-and-down-left-from-center";
        }
        sizeButton?.setAttribute("aria-label", size === "full" ? "Half height" : "Full screen");
    }

    function setSize(size) {
        panel.dataset.size = size;
        panel.style.removeProperty("height");
        applySize();
        // Lets the viewer recalculate fit-to-screen sizing.
        window.dispatchEvent(new Event("resize"));
    }

    function setOpen(open) {
        // The touch keyboard grabs focus back whenever it loses it, which would
        // make typing in the assistant impossible.  Turn it off first.
        const keyboardButton = document.getElementById("keyboardButton");
        if (open && keyboardButton?.classList.contains("toggled")) {
            keyboardButton.click();
        }

        // Close the session toolbar sheet on phones so the assistant isn't hidden behind it.
        if (open && document.body.classList.contains("menu-open")) {
            document.querySelector(".menu-close-button")?.click();
        }

        if (open && !frame.src) {
            frame.src = frame.dataset.src;
        }
        if (open) {
            panel.dataset.size = "half";
        }
        panel.hidden = !open;
        panel.style.removeProperty("height");
        document.body.classList.toggle("assistant-open", open);
        button.classList.toggle("toggled", open);
        applySize();
        // Lets the viewer recalculate fit-to-screen sizing.
        window.dispatchEvent(new Event("resize"));
    }

    button.addEventListener("click", () => setOpen(panel.hidden));
    closeButton?.addEventListener("click", () => setOpen(false));
    sizeButton?.addEventListener("click", () => setSize(panel.dataset.size === "full" ? "half" : "full"));

    // Drag the handle or header to resize; release snaps to closed / half / full.
    let drag = null;

    function onDragStart(ev) {
        if (!isSheet() || ev.button > 0 || ev.target.closest("button")) {
            return;
        }
        drag = {
            pointerId: ev.pointerId,
            startY: ev.clientY,
            startHeight: panel.getBoundingClientRect().height,
            moved: false,
            fromHandle: ev.currentTarget === handle,
            element: ev.currentTarget,
        };
        try {
            ev.currentTarget.setPointerCapture(ev.pointerId);
        } catch { }
    }

    function onDragMove(ev) {
        if (!drag || ev.pointerId !== drag.pointerId) {
            return;
        }
        const dy = drag.startY - ev.clientY;
        if (!drag.moved && Math.abs(dy) < 6) {
            return;
        }
        drag.moved = true;
        const height = Math.max(80, Math.min(viewportHeight(), drag.startHeight + dy));
        panel.classList.add("dragging");
        panel.style.height = `${height}px`;
    }

    function onDragEnd(ev) {
        if (!drag || ev.pointerId !== drag.pointerId) {
            return;
        }
        const current = drag;
        drag = null;
        panel.classList.remove("dragging");

        if (!current.moved) {
            if (current.fromHandle) {
                setSize(panel.dataset.size === "full" ? "half" : "full");
            }
            return;
        }

        const ratio = panel.getBoundingClientRect().height / viewportHeight();
        if (ratio < 0.25) {
            setOpen(false);
        } else if (ratio < 0.75) {
            setSize("half");
        } else {
            setSize("full");
        }
    }

    [handle, header].forEach(element => {
        if (!element) {
            return;
        }
        element.addEventListener("pointerdown", onDragStart);
        element.addEventListener("pointermove", onDragMove);
        element.addEventListener("pointerup", onDragEnd);
        element.addEventListener("pointercancel", onDragEnd);
    });

    // Typing in the assistant on a phone: go full height so the log stays visible above
    // the on-screen keyboard.  The iframe is same-origin, so we can see its focus.
    frame.addEventListener("load", () => {
        try {
            frame.contentDocument?.addEventListener("focusin", ev => {
                const target = ev.target;
                if (isSheet() && !panel.hidden && target?.matches?.("textarea, input:not([type=checkbox]):not([type=radio])")) {
                    setSize("full");
                }
            });
        } catch { }
    });

    // Keep the sheet sized to the visible area when the on-screen keyboard opens or the
    // phone rotates.  Its bottom follows --kb-inset (set by the viewer scripts).
    const onViewportChange = () => {
        if (!panel.hidden && !drag) {
            applySize();
        }
    };
    window.visualViewport?.addEventListener("resize", onViewportChange);
    window.addEventListener("resize", onViewportChange);
    sidePanelQuery.addEventListener?.("change", () => {
        applySize();
        window.dispatchEvent(new Event("resize"));
    });

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
