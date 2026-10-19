window.addClassName = (element, className) => {
    element.classList.add(className);
}

window.getSelectionStart = (element) => {
    return element.selectionStart;
}

window.invokeClick = elementId => {
    document.getElementById(elementId).click();
}

window.invokeConfirm = async (message) => {
    return confirm(message);
}

window.invokeAlert = async (message) => {
    alert(message);
}

window.invokePrompt = async (message) => {
    return prompt(message);
}

window.preventTabOut = (element) => {
    element.addEventListener("keydown", ev => {
        if (ev.key.toLowerCase() == "tab") {
            ev.preventDefault();
        }
    })
}
window.scrollToEnd = (element) => {
    if (!element) {
        return;
    }

    element.scrollTop = element.scrollHeight;
}
window.scrollToElement = (element) => {
    window.setTimeout(() => {
        window.scrollTo({ top: element.offsetTop, behavior: "smooth"});
    }, 200);

}

/**
 * @param {string} text
 * @returns {Promise<boolean>}
 */
window.setClipboardText = async (text) => {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    }
    catch (ex) {
        console.error(ex);
        return false;
    }
}

window.setStyleProperty = (element, propertyName, value) => {
    element.style[propertyName] = value;
}

window.startDraggingY = (element, clientY) => {
    if (!element) {
        return;
    }

    var startTop = Number(clientY);

    function pointerMove(ev) {
        if (Math.abs(ev.clientY - startTop) > 10) {
            if (ev.clientY < 0 || ev.clientY > window.innerHeight - element.clientHeight) {
                return;
            }

            element.style.top = `${ev.clientY}px`;
        }
    }

    function pointerUpOrLeave(ev) {
        window.removeEventListener("pointermove", pointerMove);
        window.removeEventListener("pointerup", pointerUpOrLeave);
        window.removeEventListener("pointerleave", pointerUpOrLeave);
    }

    pointerUpOrLeave();

    window.addEventListener("pointermove", pointerMove);
    window.addEventListener("pointerup", pointerUpOrLeave);
    window.addEventListener("pointerleave", pointerUpOrLeave);
}

window.autoHeight = () => {
    document.querySelectorAll(".auto-height").forEach(x => {
        var desiredHeight = window.innerHeight - x.getBoundingClientRect().top;
        x.style.height = `${desiredHeight}px`;
        x.style.opacity = 1;
    })
}

window.openWindow = (url, target) => {
    window.open(url, target);
}

// Mobile Safari only allows window.open directly in a tap.  Remote control needs a
// server round trip (up to 20 s) before the viewer URL is known, so the tap opens a
// placeholder window right away (inline onclick, before Blazor handles the event) and
// the server later points it at the viewer, or closes it if the session failed.
window.preopenPendingWindow = () => {
    try {
        const pending = window.open("", "_blank");
        if (pending) {
            pending.document.title = "Remote Control";
            pending.document.body.innerHTML =
                '<p style="font: 16px system-ui, sans-serif; margin: 2rem; color: #6b7280">Starting remote control…</p>';
        }
        window.__pendingWindow = pending;
    } catch {
        window.__pendingWindow = null;
    }
}

window.openPendingWindow = (url) => {
    const pending = window.__pendingWindow;
    window.__pendingWindow = null;
    if (pending && !pending.closed) {
        pending.location.href = url;
        return;
    }
    window.open(url, "_blank");
}

window.closePendingWindow = () => {
    const pending = window.__pendingWindow;
    window.__pendingWindow = null;
    if (pending && !pending.closed) {
        pending.close();
    }
}

window.addEventListener("load", () => {
    window.addEventListener("resize", () => {
        autoHeight();
    });
    autoHeight();
})

window.downloadFileFromText = (fileName, content) => {
    var blob = new Blob([content], { type: "application/json" });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

window.addBeforeUnloadHandler = () => {
    window.addEventListener("beforeunload", (ev) => {
        if (location.href.indexOf("localhost") == -1) {
            // All three methods have been implemented in different browsers at some point.
            // Might as well cover all bases.
            ev.preventDefault();
            var msg = "This will disconnect your session.  Are you sure you want to exit?";
            ev.returnValue = msg;
            return msg;
        }
    })
}