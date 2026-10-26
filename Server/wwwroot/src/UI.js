import { ViewerApp } from "./App.js";
import { ConvertUInt8ArrayToBase64 } from "./Utilities.js";
import { WindowsSessionType } from "./Enums/WindowsSessionType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
export var AudioButton = document.getElementById("audioButton");
export var MenuButton = document.getElementById("menuButton");
export var MenuFrame = document.getElementById("menuFrame");
export var SessionIDInput = document.getElementById("sessionIDInput");
export var ConnectButton = document.getElementById("connectButton");
export var RequesterNameInput = document.getElementById("nameInput");
export var StatusMessage = document.getElementById("statusMessage");
export var ScreenViewer = document.getElementById("screenViewer");
export var ScreenViewerWrapper = document.getElementById("screenViewerWrapper");
export var Screen2DContext = ScreenViewer ? ScreenViewer.getContext("2d") : null;
export var PopupMenus = document.querySelectorAll(".popup-menu");
export var ConnectBox = document.getElementById("connectBox");
export var ConnectHeader = document.getElementById("connectHeader");
export var ConnectForm = document.getElementById("connectForm");
export var ScreenSelectMenu = document.getElementById("screenSelectMenu");
export var ChangeScreenButton = document.getElementById("changeScreenButton");
export var FitToScreenButton = document.getElementById("fitToScreenButton");
export var BlockInputButton = document.getElementById("blockInputButton");
export var DisconnectButton = document.getElementById("disconnectButton");
export var FileTransferInput = document.getElementById("fileTransferInput");
export var FileTransferProgress = document.getElementById("fileTransferProgress");
export var FileTransferNameSpan = document.getElementById("fileTransferNameSpan");
export var KeyboardButton = document.getElementById("keyboardButton");
export var InviteButton = document.getElementById("inviteButton");
export var FileTransferButton = document.getElementById("fileTransferButton");
export var FileTransferMenu = document.getElementById("fileTransferMenu");
export var FileUploadButtton = document.getElementById("fileUploadButton");
export var FileDownloadButton = document.getElementById("fileDownloadButton");
export var CtrlAltDelButton = document.getElementById("ctrlAltDelButton");
export var TouchKeyboardInput = document.getElementById("touchKeyboardInput");
export var ClipboardTransferMenu = document.getElementById("clipboardTransferMenu");
export var ClipboardTransferButton = document.getElementById("clipboardTransferButton");
export var TypeClipboardButton = document.getElementById("typeClipboardButton");
export var ConnectionP2PIcon = document.getElementById("connectionP2PIcon");
export var ConnectionRelayedIcon = document.getElementById("connectionRelayedIcon");
export var WindowsSessionSelect = document.getElementById("windowsSessionSelect");
export var ViewOnlyButton = document.getElementById("viewOnlyButton");
export var FullScreenButton = document.getElementById("fullScreenButton");
export var ToastsWrapper = document.getElementById("toastsWrapper");
export var MbpsDiv = document.getElementById("mbpsDiv");
export var FpsDiv = document.getElementById("fpsDiv");
export var LatencyDiv = document.getElementById("latencyDiv");
export var GpuDiv = document.getElementById("gpuAcceleratedDiv");
export var WorkAreaGrid = document.getElementById("workAreaGrid");
export var BackgroundLayers = document.getElementById("backgroundLayers");
export var ExtrasMenu = document.getElementById("extrasMenu");
export var ExtrasMenuButton = document.getElementById("extrasMenuButton");
export var WindowsSessionMenu = document.getElementById("windowsSessionMenu");
export var WindowsSessionMenuButton = document.getElementById("windowsSessionMenuButton");
export var MetricsButton = document.getElementById("metricsButton");
export var MetricsFrame = document.getElementById("metricsFrame");
export var BetaPillPullDown = document.getElementById("betaPillPullDown");
export var QualityModeButton = document.getElementById("qualityModeButton");
export var QualityModeMenu = document.getElementById("qualityModeMenu");
export var QualityModeLabel = document.getElementById("qualityModeLabel");
export var QualityAutoButton = document.getElementById("qualityAutoButton");
export var QualityPerformanceButton = document.getElementById("qualityPerformanceButton");
export var QualityHighButton = document.getElementById("qualityHighButton");
export var FabCluster = document.getElementById("fabCluster");
export var MenuBackdrop = document.getElementById("menuBackdrop");
export var TouchKeyboardBar = document.getElementById("touchKeyboardBar");
export var TouchKeyboardCloseButton = document.getElementById("touchKeyboardCloseButton");
const _thumbnailMap = new Map();
export function UpdateThumbnail(displayName, imageBytes) {
    var base64 = ConvertUInt8ArrayToBase64(imageBytes);
    _thumbnailMap.set(displayName, `data:image/jpeg;base64,${base64}`);
    var img = document.querySelector(`[data-display="${CSS.escape(displayName)}"] img`);
    if (img) {
        ShowThumbnail(img, _thumbnailMap.get(displayName));
    }
}
// Constant markup (Lucide "monitor"), never user data.
const MonitorIconSvg = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect width="20" height="14" x="2" y="3" rx="2"/><line x1="8" x2="16" y1="21" y2="21"/><line x1="12" x2="12" y1="17" y2="21"/></svg>';
function ShowThumbnail(img, src) {
    img.onload = () => {
        var _a, _b;
        img.hidden = false;
        (_b = (_a = img.parentElement) === null || _a === void 0 ? void 0 : _a.querySelector("svg")) === null || _b === void 0 ? void 0 : _b.setAttribute("hidden", "");
    };
    img.src = src;
}
export function CloseAllPopupMenus(exceptMenuId) {
    PopupMenus.forEach(x => {
        if (x.id != exceptMenuId) {
            x.classList.remove("open");
        }
    });
}
/** Opens or closes the session toolbar (bottom sheet on phones, top bar from md up). */
export function SetMenuOpen(open) {
    MenuFrame.classList.toggle("open", open);
    MenuButton.classList.toggle("open", open);
    MenuButton.setAttribute("aria-expanded", String(open));
    document.body.classList.toggle("menu-open", open);
    if (MenuBackdrop) {
        MenuBackdrop.hidden = !open;
    }
    BetaPillPullDown === null || BetaPillPullDown === void 0 ? void 0 : BetaPillPullDown.classList.toggle("d-none", open);
    if (!open) {
        CloseAllPopupMenus(null);
    }
}
export function Prompt(promptMessage) {
    return new Promise((resolve, reject) => {
        var modalDiv = document.createElement("div");
        modalDiv.className = "modal-prompt fixed top-1/2 left-1/2 z-[60] w-[calc(100%-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-xl border border-border bg-surface-raised p-4 text-fg shadow-xl";
        var messageDiv = document.createElement("div");
        messageDiv.textContent = promptMessage;
        var responseInput = document.createElement("input");
        responseInput.className = "sr-input mt-3";
        var buttonsDiv = document.createElement("div");
        buttonsDiv.className = "buttons-footer mt-4 flex justify-end gap-2";
        var cancelButton = document.createElement("button");
        cancelButton.className = "sr-btn sr-btn-secondary";
        cancelButton.innerHTML = "Cancel";
        var okButton = document.createElement("button");
        okButton.className = "sr-btn sr-btn-primary";
        okButton.innerHTML = "OK";
        buttonsDiv.appendChild(okButton);
        buttonsDiv.appendChild(cancelButton);
        modalDiv.appendChild(messageDiv);
        modalDiv.appendChild(responseInput);
        modalDiv.appendChild(buttonsDiv);
        document.body.appendChild(modalDiv);
        okButton.onclick = () => {
            modalDiv.remove();
            resolve(responseInput.value);
        };
        cancelButton.onclick = () => {
            modalDiv.remove();
            resolve(null);
        };
    });
}
/**
 * Asks the user to confirm an action in a modal dialog.  Resolves true when confirmed.
 * Escape, Cancel and tapping the backdrop resolve false.
 */
export function Confirm(message, confirmLabel, danger = true) {
    return new Promise((resolve) => {
        const dialog = document.createElement("dialog");
        if (typeof dialog.showModal !== "function") {
            resolve(window.confirm(message));
            return;
        }
        dialog.className = "sr-dialog";
        const body = document.createElement("div");
        body.className = "p-5";
        const messageP = document.createElement("p");
        messageP.className = "mb-5 text-base";
        messageP.innerText = message;
        const buttons = document.createElement("div");
        buttons.className = "flex flex-col-reverse gap-2 sm:flex-row sm:justify-end";
        const cancelButton = document.createElement("button");
        cancelButton.type = "button";
        cancelButton.className = "sr-btn sr-btn-secondary w-full sm:w-auto";
        cancelButton.innerText = "Cancel";
        const okButton = document.createElement("button");
        okButton.type = "button";
        okButton.className = `sr-btn ${danger ? "sr-btn-danger" : "sr-btn-primary"} w-full sm:w-auto`;
        okButton.innerText = confirmLabel;
        buttons.appendChild(cancelButton);
        buttons.appendChild(okButton);
        body.appendChild(messageP);
        body.appendChild(buttons);
        dialog.appendChild(body);
        let done = false;
        const finish = (result) => {
            if (done) {
                return;
            }
            done = true;
            if (dialog.open) {
                dialog.close();
            }
            dialog.remove();
            resolve(result);
        };
        okButton.addEventListener("click", () => finish(true));
        cancelButton.addEventListener("click", () => finish(false));
        dialog.addEventListener("cancel", (ev) => {
            ev.preventDefault();
            finish(false);
        });
        dialog.addEventListener("click", (ev) => {
            // A click on the dialog element itself is a click on the backdrop.
            if (ev.target === dialog) {
                finish(false);
            }
        });
        document.body.appendChild(dialog);
        dialog.showModal();
        cancelButton.focus();
    });
}
export function SetScreenSize(width, height) {
    ScreenViewer.width = width;
    ScreenViewer.height = height;
    Screen2DContext.clearRect(0, 0, width, height);
}
export function SetStatusMessage(message) {
    StatusMessage.innerText = message;
}
export function ShowToast(message) {
    var messageDiv = document.createElement("div");
    messageDiv.className = "toast-message max-w-full rounded-lg border border-border bg-surface-raised/95 px-4 py-2 text-sm text-fg shadow-lg select-none";
    messageDiv.textContent = message;
    ToastsWrapper.appendChild(messageDiv);
    window.setTimeout(() => {
        messageDiv.remove();
    }, 5000);
}
export function ToggleConnectUI(shown) {
    if (shown) {
        ConnectButton.innerText = "Connect";
        Screen2DContext.clearRect(0, 0, ScreenViewer.width, ScreenViewer.height);
        ScreenViewerWrapper.setAttribute("hidden", "hidden");
        if (ViewerApp.Mode == RemoteControlMode.Attended) {
            ConnectBox.style.removeProperty("display");
            ConnectHeader.style.removeProperty("display");
        }
        BlockInputButton.classList.remove("toggled");
        AudioButton.classList.remove("toggled");
        WorkAreaGrid.style.display = "none";
        BackgroundLayers.classList.remove("d-none");
        document.body.classList.remove("in-session");
        SetMenuOpen(false);
    }
    else {
        ConnectBox.style.display = "none";
        ConnectHeader.style.display = "none";
        ScreenViewerWrapper.removeAttribute("hidden");
        StatusMessage.innerHTML = "";
        WorkAreaGrid.style.removeProperty("display");
        BackgroundLayers.classList.add("d-none");
        document.body.classList.add("in-session");
    }
    ConnectButton.disabled = !ViewerApp.RequesterName || !ViewerApp.SessionId;
}
export function UpdateCursor(imageBytes, hotSpotX, hotSpotY, cssOverride) {
    if (cssOverride) {
        ScreenViewer.style.cursor = cssOverride;
    }
    else if (imageBytes.byteLength == 0) {
        ScreenViewer.style.cursor = "default";
    }
    else {
        var base64 = ConvertUInt8ArrayToBase64(imageBytes);
        ScreenViewer.style.cursor = `url('data:image/png;base64,${base64}') ${hotSpotX} ${hotSpotY}, default`;
    }
}
export function UpdateDisplays(selectedDisplay, displayNames, displayLayouts) {
    ScreenSelectMenu.innerHTML = "";
    if (displayLayouts && displayLayouts.length > 1) {
        var layoutContainer = RenderMonitorLayout(selectedDisplay, displayLayouts);
        ScreenSelectMenu.appendChild(layoutContainer);
    }
    for (let i = 0; i < displayNames.length; i++) {
        var button = document.createElement("button");
        button.className = "sr-btn sr-btn-ghost h-auto w-full flex-col gap-1 p-2";
        button.setAttribute("data-display", displayNames[i]);
        button.title = displayNames[i];
        // A tile with a monitor icon until the agent sends a thumbnail (older agents never do).
        var thumb = document.createElement("span");
        thumb.className = "monitor-thumbnail";
        thumb.innerHTML = MonitorIconSvg;
        var img = document.createElement("img");
        img.alt = "";
        img.hidden = true;
        img.onerror = () => {
            img.hidden = true;
            img.removeAttribute("src");
        };
        thumb.appendChild(img);
        var cachedSrc = _thumbnailMap.get(displayNames[i]);
        if (cachedSrc) {
            ShowThumbnail(img, cachedSrc);
        }
        var label = document.createElement("span");
        label.innerText = `Monitor ${i + 1}`;
        if (displayNames[i] == selectedDisplay) {
            button.classList.add("toggled");
        }
        button.appendChild(thumb);
        button.appendChild(label);
        ScreenSelectMenu.appendChild(button);
        button.onclick = (ev) => {
            ViewerApp.MessageSender.SendSelectScreen(displayNames[i], `Monitor ${i + 1}`);
            ScreenSelectMenu.classList.toggle("open");
            ScreenSelectMenu.querySelectorAll("button").forEach(btn => {
                btn.classList.remove("toggled");
            });
            ev.currentTarget.classList.add("toggled");
        };
    }
}
export function RenderMonitorLayout(selectedDisplay, layouts) {
    var container = document.createElement("div");
    container.className = "monitor-layout-diagram";
    var minX = Math.min(...layouts.map(l => l.X));
    var minY = Math.min(...layouts.map(l => l.Y));
    var maxX = Math.max(...layouts.map(l => l.X + l.Width));
    var maxY = Math.max(...layouts.map(l => l.Y + l.Height));
    var totalW = maxX - minX || 1;
    var totalH = maxY - minY || 1;
    const svgW = 200;
    const svgH = Math.max(40, Math.round(svgW * totalH / totalW));
    const padding = 4;
    var svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("width", String(svgW + padding * 2));
    svg.setAttribute("height", String(svgH + padding * 2));
    svg.setAttribute("viewBox", `0 0 ${svgW + padding * 2} ${svgH + padding * 2}`);
    svg.style.display = "block";
    for (let li = 0; li < layouts.length; li++) {
        var layout = layouts[li];
        var rx = padding + Math.round((layout.X - minX) / totalW * svgW);
        var ry = padding + Math.round((layout.Y - minY) / totalH * svgH);
        var rw = Math.max(1, Math.round(layout.Width / totalW * svgW) - 2);
        var rh = Math.max(1, Math.round(layout.Height / totalH * svgH) - 2);
        var rect = document.createElementNS("http://www.w3.org/2000/svg", "rect");
        rect.setAttribute("x", String(rx));
        rect.setAttribute("y", String(ry));
        rect.setAttribute("width", String(rw));
        rect.setAttribute("height", String(rh));
        rect.setAttribute("rx", "3");
        rect.classList.add("monitor-layout-rect");
        if (layout.DisplayName === selectedDisplay) {
            rect.classList.add("monitor-layout-rect-selected");
        }
        var displayName = layout.DisplayName;
        var monitorLabel = `Monitor ${li + 1}`;
        rect.addEventListener("click", () => {
            ViewerApp.MessageSender.SendSelectScreen(displayName, monitorLabel);
            ScreenSelectMenu.classList.toggle("open");
        });
        svg.appendChild(rect);
    }
    container.appendChild(svg);
    return container;
}
export function UpdateMetrics(metricsDto) {
    FpsDiv.innerHTML = metricsDto.Fps.toFixed(0);
    MbpsDiv.innerHTML = metricsDto.Mbps.toFixed(2);
    LatencyDiv.innerHTML = `${metricsDto.RoundTripLatency.toFixed(2)}ms`;
    GpuDiv.innerHTML = metricsDto.IsGpuAccelerated ? "Enabled" : "Unavailable";
}
export function UpdateWindowsSessions(windowsSessions) {
    while (WindowsSessionSelect.options.length > 0) {
        WindowsSessionSelect.options.remove(0);
    }
    WindowsSessionSelect.options.add(document.createElement("option"));
    windowsSessions.forEach(x => {
        var sessionType = "";
        if (typeof x.Type == "number") {
            sessionType = x.Type == WindowsSessionType.Console ? "Console" : "RDP";
        }
        else {
            sessionType = x.Type;
        }
        var option = document.createElement("option");
        option.value = String(x.ID);
        option.text = `${sessionType} (ID: ${x.ID} | User: ${x.Username})`;
        option.title = `${sessionType} Session (ID: ${x.ID} | User: ${x.Username})`;
        WindowsSessionSelect.options.add(option);
    });
}
//# sourceMappingURL=UI.js.map