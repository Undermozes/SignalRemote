import {
    AudioButton,
    ChangeScreenButton,
    PopupMenus,
    ScreenSelectMenu,
    ClipboardTransferButton,
    ClipboardTransferMenu,
    TypeClipboardButton,
    ConnectButton,
    CtrlAltDelButton,
    DisconnectButton,
    FileTransferButton,
    FileTransferInput,
    FitToScreenButton,
    ScreenViewer,
    BlockInputButton,
    InviteButton,
    KeyboardButton,
    TouchKeyboardInput,
    MenuFrame,
    MenuButton,
    ScreenViewerWrapper,
    WindowsSessionSelect,
    FileTransferMenu,
    FileUploadButtton,
    FileDownloadButton,
    ViewOnlyButton,
    FullScreenButton,
    RequesterNameInput,
    SessionIDInput,
    ConnectForm,
    CloseAllPopupMenus,
    ExtrasMenu,
    ExtrasMenuButton,
    WindowsSessionMenuButton,
    WindowsSessionMenu,
    MetricsButton,
    MetricsFrame,
    SetStatusMessage,
    BetaPillPullDown,
    QualityModeButton,
    QualityModeMenu,
    QualityModeLabel,
    QualityAutoButton,
    QualityPerformanceButton,
    QualityHighButton,
    FabCluster,
    MenuBackdrop,
    TouchKeyboardBar,
    TouchKeyboardCloseButton,
    SetMenuOpen,
    Confirm,
} from "./UI.js";
import { Sound } from "./Sound.js";
import { ViewerApp } from "./App.js";
import { Point } from "./Models/Point.js";
import { UploadFiles } from "./FileTransferService.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
import { RemoteControlQualityMode } from "./Enums/RemoteControlQualityMode.js";
import { GetDistanceBetween } from "./Utilities.js";
import { ShowToast } from "./UI.js";

var isDragging: boolean;
var currentPointerDevice: string;
var currentTouchCount: number;
var isPinchZooming: boolean;
var startPinchPoint1: Point;
var startPinchPoint2: Point;
var lastPinchDistance: number;
var longPressStarted: boolean;
var longPressStartOffsetX: number;
var longPressStartOffsetY: number;
var lastPinchCenterX: number;
var lastPinchCenterY: number;
var isScrolling: boolean;
var lastScrollTime: number;
var lastScrollTouchY1: number;
var lastScrollTouchY2: number;
var lastPointerMove = Date.now();
var pointerMoveTimeout = -1;

// Touch gestures that don't depend on the contextmenu event (iOS Safari doesn't fire it):
// long-press = right-click, long-press then move = left-button drag, two-finger tap = right-click.
const LongPressDelayMs = 500;
const TouchMoveTolerance = 10;
const TwoFingerTapMaxMs = 350;
var longPressTimer = -1;
var touchStartClientX: number;
var touchStartClientY: number;
var twoFingerTapCandidate = false;
var twoFingerTapStartTime = 0;
var twoFingerStart1: Point;
var twoFingerStart2: Point;
var touchLongPressFinishedAt = 0;
var lastDragPercentX = 0;
var lastDragPercentY = 0;

// Touch keyboard.  The input keeps one invisible character so Backspace on an "empty" field still
// produces a deleteContentBackward beforeinput event on Android keyboards.
const KeyboardSentinel = "\u200B";
const ModifierOrder = ["Control", "Alt", "Shift", "Meta"];
const activeModifiers = new Set<string>();

// Floating toolbar button dragging.
const FabPositionStorageKey = "viewerFabPosition";
const FabDragThreshold = 8;
var fabDragStart: { x: number, y: number, left: number, top: number } = null;
var fabDragging = false;
var suppressMenuClickUntil = 0;

export function ApplyInputHandlers() {
    AudioButton.addEventListener("click", async (ev) => {
        AudioButton.classList.toggle("toggled");
        var toggleOn = AudioButton.classList.contains("toggled");
        if (toggleOn) {
            Sound.Init();
        }
        await ViewerApp.MessageSender.SendToggleAudio(toggleOn);
    });
    ChangeScreenButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ScreenSelectMenu.id);
        togglePopupBelow(ScreenSelectMenu, ChangeScreenButton, "left");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    ClipboardTransferButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ClipboardTransferMenu.id);
        togglePopupBelow(ClipboardTransferMenu, ClipboardTransferButton, "left");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    ViewOnlyButton.addEventListener("click", () => {
        ViewOnlyButton.classList.toggle("toggled");
        ViewerApp.ViewOnlyMode = ViewOnlyButton.classList.contains("toggled");
    });
    TypeClipboardButton.addEventListener("click", (ev) => {
        if (
            !location.protocol.includes("https") &&
            !location.origin.includes("localhost")
        ) {
            alert(
                "Clipboard API only works in a secure context (i.e. HTTPS or localhost)."
            );
            return;
        }

        if (!navigator.clipboard?.readText) {
            alert("Clipboard access isn't supported on this browser.");
            return;
        }

        if (ViewerApp.ViewOnlyMode) {
            alert("View-only mode is enabled.");
            return;
        }

        navigator.clipboard.readText().then(
            async (text) => {
                await ViewerApp.MessageSender.SendTextTransfer(text, true);
                ShowToast("Clipboard sent!");
            },
            (reason) => {
                alert("Unable to read clipboard.  Please check your permissions.");
                console.log("Unable to read clipboard.  Reason: " + reason);
            }
        );
    });
    // The connect button is a submit button; never let the form reload the page.
    ConnectForm.addEventListener("submit", (ev) => {
        ev.preventDefault();
    });
    ConnectButton.addEventListener("click", () => {
        if (!ConnectForm.checkValidity()) {
            return;
        }
        ViewerApp.ConnectToClient();
    });
    CtrlAltDelButton.addEventListener("click", async () => {
        if (ViewerApp.ViewOnlyMode) {
            alert("View-only mode is enabled.");
            return;
        }

        CloseAllPopupMenus(null);
        await ViewerApp.MessageSender.SendCtrlAltDel();
    });
    DisconnectButton.addEventListener("click", async (ev) => {
        ev.stopPropagation();
        CloseAllPopupMenus(null);
        // On touch a mis-tap here would drop the session, so always ask first.
        if (!await Confirm("Disconnect from this computer?", "Disconnect")) {
            return;
        }
        SetMenuOpen(false);
        ConnectButton.removeAttribute("disabled");
        ConnectButton.innerText = "Connect";
        SetStatusMessage("Connection closed.");
        ViewerApp.ViewerHubConnection.Connection.stop();
        if (location.search.includes("fromApi=true")) {
            window.close();
        }
    });

    [SessionIDInput, RequesterNameInput].forEach((x) => {
        x.addEventListener("keypress", (ev: KeyboardEvent) => {
            if (!SessionIDInput.value || !RequesterNameInput.value) {
                return;
            }

            if (ev.key.toLowerCase() == "enter") {
                ViewerApp.ConnectToClient();
            }
        });

        x.addEventListener("input", () => {
            if (!SessionIDInput.value || !RequesterNameInput.value) {
                ConnectButton.setAttribute("disabled", "disabled");
            } else {
                ConnectButton.removeAttribute("disabled");
            }
        });
    });
    ExtrasMenuButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(ExtrasMenu.id);
        togglePopupBelow(ExtrasMenu, ExtrasMenuButton, "right");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    FileTransferButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        togglePopupBelow(FileTransferMenu, FileTransferButton, "right");
        const buttonZindex = Number.parseInt(
            getComputedStyle(FileTransferButton.parentElement).zIndex
        );
        FileTransferMenu.style.zIndex = `${buttonZindex + 1}`;

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    FileUploadButtton.addEventListener("click", (ev) => {
        FileTransferInput.click();
    });
    FileDownloadButton.addEventListener("click", async (ev) => {
        if (ViewerApp.ViewOnlyMode) {
            alert("View-only mode is enabled.");
            return;
        }

        await ViewerApp.MessageSender.SendOpenFileTransferWindow();
    });
    FileTransferInput.addEventListener("change", (ev) => {
        UploadFiles(FileTransferInput.files);
    });
    FitToScreenButton.addEventListener("click", (ev) => {
        FitToScreenButton.classList.toggle("toggled");
        if (FitToScreenButton.classList.contains("toggled")) {
            ScreenViewer.classList.add("fit");
        } else {
            ScreenViewer.classList.remove("fit");
        }
    });
    // iPhone Safari has no element fullscreen.
    if (!document.fullscreenEnabled) {
        FullScreenButton.classList.add("d-none");
    }
    FullScreenButton.addEventListener("click", () => {
        if (!document.fullscreenElement) {
            document.body.requestFullscreen();
        } else {
            document.exitFullscreen();
        }
    });

    document.addEventListener("fullscreenchange", () => {
        if (document.fullscreenElement) {
            FullScreenButton.classList.add("toggled");
            FullScreenButton.title = "Exit fullscreen mode.";
            const enterIcon = FullScreenButton.querySelector("i") as HTMLElement;
            if (enterIcon) {
                enterIcon.className = "fas fa-compress";
            }
            SetMenuOpen(false);
        } else {
            FullScreenButton.classList.remove("toggled");
            FullScreenButton.title = "Enter fullscreen mode.";
            const exitIcon = FullScreenButton.querySelector("i") as HTMLElement;
            if (exitIcon) {
                exitIcon.className = "fas fa-window-maximize";
            }
        }
    });
    BlockInputButton.addEventListener("click", async (ev) => {
        if (ViewerApp.ViewOnlyMode) {
            alert("View-only mode is enabled.");
            return;
        }
        BlockInputButton.classList.toggle("toggled");
        if (BlockInputButton.classList.contains("toggled")) {
            await ViewerApp.MessageSender.SendToggleBlockInput(true);
        } else {
            await ViewerApp.MessageSender.SendToggleBlockInput(false);
        }
    });
    InviteButton.addEventListener("click", (ev) => {
        var url = "";
        if (ViewerApp.Mode == RemoteControlMode.Attended) {
            url = `${location.origin}${location.pathname}?sessionId=${ViewerApp.SessionId}`;
        } else {
            url = `${location.origin}${location.pathname}?mode=Unattended&sessionId=${ViewerApp.SessionId}&accessKey=${ViewerApp.AccessKey}`;
        }
        ViewerApp.ClipboardWatcher.SetClipboardText(url);
        ShowToast("Link copied to clipboard.");
    });
    KeyboardButton.addEventListener("click", (ev) => {
        CloseAllPopupMenus(null);
        setTouchKeyboard(!KeyboardButton.classList.contains("toggled"));
    });
    TouchKeyboardCloseButton.addEventListener("click", () => {
        setTouchKeyboard(false);
    });
    MenuButton.addEventListener("click", (ev) => {
        if (Date.now() < suppressMenuClickUntil) {
            // The button was dragged, not tapped.
            return;
        }
        ev.stopPropagation();
        SetMenuOpen(!MenuFrame.classList.contains("open"));
    });
    document.querySelectorAll(".menu-close-button").forEach(x => {
        x.addEventListener("click", (ev) => {
            ev.stopPropagation();
            SetMenuOpen(false);
        });
    });
    MenuBackdrop?.addEventListener("click", () => {
        SetMenuOpen(false);
    });
    applyFabDragHandlers();
    applyTouchKeyboardBarHandlers();
    applyViewportTracking();

    MetricsButton.addEventListener("click", () => {
        MetricsFrame.classList.toggle("d-none");
    });

    QualityModeButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(QualityModeMenu.id);
        togglePopupBelow(QualityModeMenu, QualityModeButton, "right");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });

    const setQualityMode = async (mode: RemoteControlQualityMode, label: string) => {
        [QualityAutoButton, QualityPerformanceButton, QualityHighButton].forEach(btn => {
            btn.classList.remove("toggled");
        });

        switch (mode) {
            case RemoteControlQualityMode.Auto:
                QualityAutoButton.classList.add("toggled");
                break;
            case RemoteControlQualityMode.Performance:
                QualityPerformanceButton.classList.add("toggled");
                break;
            case RemoteControlQualityMode.Quality:
                QualityHighButton.classList.add("toggled");
                break;
        }

        QualityModeLabel.innerText = label;
        await ViewerApp.MessageSender.SendQualityMode(mode);
        ShowToast(`Quality mode set to: ${label}`);
    };

    QualityAutoButton.addEventListener("click", async () => {
        await setQualityMode(RemoteControlQualityMode.Auto, "Auto");
    });

    QualityPerformanceButton.addEventListener("click", async () => {
        await setQualityMode(RemoteControlQualityMode.Performance, "Performance");
    });

    QualityHighButton.addEventListener("click", async () => {
        await setQualityMode(RemoteControlQualityMode.Quality, "Quality");
    });

    ScreenViewer.addEventListener("pointerup", async ev => {
        if (ev.pointerType == "touch") {
            // Finish a touch long-press (right-click) or drag here, as before; touchend does
            // it if this didn't run (finishTouchLongPress only acts once).
            if (currentTouchCount <= 1) {
                cancelLongPressTimer();
                await finishTouchLongPress(ev.clientX, ev.clientY);
            }
            return;
        }

        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        const percentX = ev.offsetX / ScreenViewer.clientWidth;
        const percentY = ev.offsetY / ScreenViewer.clientHeight;

        if (longPressStarted && !isDragging) {
            await ViewerApp.MessageSender.SendMouseDown(2, percentX, percentY);
            await ViewerApp.MessageSender.SendMouseUp(2, percentX, percentY);
        }

        if (longPressStarted && isDragging) {
            // If we're dragging, we need to send a mouse up event.
            await ViewerApp.MessageSender.SendMouseUp(0, percentX, percentY);
        }

        resetTouchState();
    });

    ScreenViewer.addEventListener("pointercancel", ev => {
        if (ev.pointerType == "touch") {
            // The browser took over the gesture (pan/zoom).  Don't leave a button stuck down.
            cancelLongPressTimer();
            if (isDragging && !ViewerApp.ViewOnlyMode) {
                ViewerApp.MessageSender.SendMouseUp(0, lastDragPercentX, lastDragPercentY);
            }
        }
        resetTouchState();
    });

    ScreenViewer.addEventListener("pointerout", ev => {
        // For touch these fire right after pointerup, before touchend needs the state.
        if (ev.pointerType == "touch") {
            return;
        }
        resetTouchState();
    });

    ScreenViewer.addEventListener("pointerleave", ev => {
        if (ev.pointerType == "touch") {
            return;
        }
        resetTouchState();
    });

    ScreenViewer.addEventListener("touchmove", async function (e: TouchEvent) {
        currentTouchCount = e.touches.length;

        if (longPressTimer > -1 && e.touches.length == 1) {
            const moved = GetDistanceBetween(
                touchStartClientX,
                touchStartClientY,
                e.touches[0].clientX,
                e.touches[0].clientY
            );
            if (moved > TouchMoveTolerance) {
                cancelLongPressTimer();
            }
        }

        if (twoFingerTapCandidate) {
            if (e.touches.length != 2 ||
                GetDistanceBetween(twoFingerStart1.X, twoFingerStart1.Y, e.touches[0].clientX, e.touches[0].clientY) > TouchMoveTolerance ||
                GetDistanceBetween(twoFingerStart2.X, twoFingerStart2.Y, e.touches[1].clientX, e.touches[1].clientY) > TouchMoveTolerance) {
                twoFingerTapCandidate = false;
            }
        }

        if ((longPressStarted || isDragging) && e.cancelable) {
            e.preventDefault();
        }

        if (e.touches.length == 1 && longPressStarted && !isDragging) {
            e.stopPropagation();

            if (ViewerApp.ViewOnlyMode) {
                return;
            }

            const rect = ScreenViewer.getBoundingClientRect();
            const offsetX = e.touches[0].pageX - rect.left;
            const offsetY = e.touches[0].pageY - rect.top;

            const moveDistance = GetDistanceBetween(
                longPressStartOffsetX,
                longPressStartOffsetY,
                offsetX,
                offsetY
            );

            if (moveDistance > 5) {
                isDragging = true;
                // Move the mouse to the initial touch point to start the drag operation.
                const percentX = longPressStartOffsetX / ScreenViewer.clientWidth;
                const percentY = longPressStartOffsetY / ScreenViewer.clientHeight;
                lastDragPercentX = percentX;
                lastDragPercentY = percentY;
                await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
                await ViewerApp.MessageSender.SendMouseDown(0, percentX, percentY);
            }
            return;
        }

        if (isDragging) {
            e.stopPropagation();

            if (ViewerApp.ViewOnlyMode) {
                return;
            }

            var screenViewerLeft = ScreenViewer.getBoundingClientRect().left;
            var screenViewerTop = ScreenViewer.getBoundingClientRect().top;
            var pagePercentX =
                (e.touches[0].pageX - screenViewerLeft) / ScreenViewer.clientWidth;
            var pagePercentY =
                (e.touches[0].pageY - screenViewerTop) / ScreenViewer.clientHeight;
            lastDragPercentX = pagePercentX;
            lastDragPercentY = pagePercentY;
            ViewerApp.MessageSender.SendMouseMove(pagePercentX, pagePercentY);

            return;
        }

        if (e.touches.length == 2) {
            let touchMove1 = lastScrollTouchY1 - e.touches[0].pageY;
            let touchMove2 = lastScrollTouchY2 - e.touches[1].pageY;

            if (!isPinchZooming && (isScrolling || touchMove1 * touchMove2 > 0)) {
                // Both touch points are moving in the same direction.  We're doing a scroll.

                if (!isScrolling) {
                    // If this is the start of scrolling, move the mouse to our touch point so
                    // the scroll wheel action will target the intended element on screen.
                    var screenViewerLeft = ScreenViewer.getBoundingClientRect().left;
                    var screenViewerTop = ScreenViewer.getBoundingClientRect().top;
                    var pagePercentX =
                        (e.touches[0].pageX - screenViewerLeft) / ScreenViewer.clientWidth;
                    var pagePercentY =
                        (e.touches[0].pageY - screenViewerTop) / ScreenViewer.clientHeight;
                    await ViewerApp.MessageSender.SendMouseMove(
                        pagePercentX,
                        pagePercentY
                    );
                }

                isScrolling = true;
                if (Date.now() - lastScrollTime < 100) {
                    return;
                }
                lastScrollTime = Date.now();
                let yMove = Math.max(-1, Math.min(touchMove1, 1));
                await ViewerApp.MessageSender.SendMouseWheel(0, yMove);
                lastScrollTouchY1 = e.touches[0].pageY;
                return;
            }

            var pinchPoint1 = {
                X: e.touches[0].pageX,
                Y: e.touches[0].pageY,
                IsEmpty: false,
            };
            var pinchPoint2 = {
                X: e.touches[1].pageX,
                Y: e.touches[1].pageY,
                IsEmpty: false,
            };
            var pinchDistance = GetDistanceBetween(
                pinchPoint1.X,
                pinchPoint1.Y,
                pinchPoint2.X,
                pinchPoint2.Y
            );

            var pinchCenterX = (pinchPoint1.X + pinchPoint2.X) / 2;
            var pinchCenterY = (pinchPoint1.Y + pinchPoint2.Y) / 2;

            ScreenViewerWrapper.scrollBy(
                lastPinchCenterX - pinchCenterX,
                lastPinchCenterY - pinchCenterY
            );

            lastPinchCenterX = pinchCenterX;
            lastPinchCenterY = pinchCenterY;

            if (Math.abs(pinchDistance - lastPinchDistance) > 5) {
                isPinchZooming = true;
                if (FitToScreenButton.classList.contains("toggled")) {
                    FitToScreenButton.click();
                }

                var currentWidth = ScreenViewer.clientWidth;
                var currentHeight = ScreenViewer.clientHeight;

                var clientAdjustedScrollLeftPercent =
                    (ScreenViewerWrapper.scrollLeft +
                        ScreenViewerWrapper.clientWidth * 0.5) /
                    ScreenViewerWrapper.scrollWidth;
                var clientAdjustedScrollTopPercent =
                    (ScreenViewerWrapper.scrollTop +
                        ScreenViewerWrapper.clientHeight * 0.5) /
                    ScreenViewerWrapper.scrollHeight;

                var currentWidthPercent = Number(ScreenViewer.style.width.slice(0, -1));
                var newWidthPercent = Math.max(
                    100,
                    currentWidthPercent +
                    (pinchDistance - lastPinchDistance) * (currentWidthPercent / 100)
                );
                newWidthPercent = Math.min(5000, newWidthPercent);
                ScreenViewer.style.width = String(newWidthPercent) + "%";

                var heightChange = ScreenViewer.clientHeight - currentHeight;
                var widthChange = ScreenViewer.clientWidth - currentWidth;

                var pinchAdjustX = pinchCenterX / window.innerWidth - 0.5;
                var pinchAdjustY = pinchCenterY / window.innerHeight - 0.5;

                var scrollByX =
                    widthChange *
                    (clientAdjustedScrollLeftPercent +
                        (pinchAdjustX * ScreenViewerWrapper.clientWidth) /
                        ScreenViewerWrapper.scrollWidth);
                var scrollByY =
                    heightChange *
                    (clientAdjustedScrollTopPercent +
                        (pinchAdjustY * ScreenViewerWrapper.clientHeight) /
                        ScreenViewerWrapper.scrollHeight);

                ScreenViewerWrapper.scrollBy(scrollByX, scrollByY);

                lastPinchDistance = pinchDistance;
            }
            return;
        }
    });

    ScreenViewer.addEventListener("pointerdown", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });

    ScreenViewer.addEventListener("pointerenter", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });

    ScreenViewer.addEventListener("pointermove", function (e: PointerEvent) {
        currentPointerDevice = e.pointerType;
    });


    ScreenViewer.addEventListener("mousemove", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        if (pointerMoveTimeout > -1) {
            clearTimeout(pointerMoveTimeout);
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        const now = Date.now();

        // Throttle mouse movements so we're not sending too many messages rapidly.
        // The timeout ensures that the last movement still gets sent.
        if (now - lastPointerMove < 50) {
            pointerMoveTimeout = setTimeout(async () => {
                await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
            }, 60);
            return;
        }

        lastPointerMove = now;
        await ViewerApp.MessageSender.SendMouseMove(percentX, percentY);
    });

    ScreenViewer.addEventListener("mousedown", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        await ViewerApp.MessageSender.SendMouseDown(e.button, percentX, percentY);
    });

    ScreenViewer.addEventListener("mouseup", async function (e: MouseEvent) {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        var percentX = e.offsetX / ScreenViewer.clientWidth;
        var percentY = e.offsetY / ScreenViewer.clientHeight;
        await ViewerApp.MessageSender.SendMouseUp(e.button, percentX, percentY);
    });

    ScreenViewer.addEventListener("touchstart", function (e: TouchEvent) {
        currentTouchCount = e.touches.length;
        cancelLongPressTimer();

        if (currentTouchCount == 1) {
            isDragging = false;
            twoFingerTapCandidate = false;
            touchStartClientX = e.touches[0].clientX;
            touchStartClientY = e.touches[0].clientY;
            if (!ViewerApp.ViewOnlyMode) {
                longPressTimer = window.setTimeout(() => {
                    longPressTimer = -1;
                    if (currentTouchCount == 1 && !isPinchZooming && !isScrolling) {
                        const rect = ScreenViewer.getBoundingClientRect();
                        startLongPress(touchStartClientX - rect.left, touchStartClientY - rect.top);
                    }
                }, LongPressDelayMs);
            }
        }

        if (currentTouchCount == 2) {
            twoFingerTapCandidate = !longPressStarted && !isDragging;
            twoFingerTapStartTime = Date.now();
            twoFingerStart1 = { X: e.touches[0].clientX, Y: e.touches[0].clientY, IsEmpty: false };
            twoFingerStart2 = { X: e.touches[1].clientX, Y: e.touches[1].clientY, IsEmpty: false };
        }
        else if (currentTouchCount > 2) {
            twoFingerTapCandidate = false;
        }

        if (currentTouchCount == 2) {
            lastScrollTouchY1 = e.touches[0].pageY;
            lastScrollTouchY2 = e.touches[1].pageY;
            startPinchPoint1 = {
                X: e.touches[0].pageX,
                Y: e.touches[0].pageY,
                IsEmpty: false,
            };
            startPinchPoint2 = {
                X: e.touches[1].pageX,
                Y: e.touches[1].pageY,
                IsEmpty: false,
            };
            lastPinchDistance = GetDistanceBetween(
                startPinchPoint1.X,
                startPinchPoint1.Y,
                startPinchPoint2.X,
                startPinchPoint2.Y
            );
            lastPinchCenterX = (startPinchPoint1.X + startPinchPoint2.X) / 2;
            lastPinchCenterY = (startPinchPoint1.Y + startPinchPoint2.Y) / 2;
        }
        var focusedInput = document.querySelector(
            "input:focus"
        ) as HTMLInputElement;

        if (focusedInput) {
            focusedInput.blur();
        }
    });


    ScreenViewer.addEventListener("touchend", async function (e: TouchEvent) {
        currentTouchCount = e.touches.length;
        // Lifted before the long-press delay: a normal tap, sent by the compatibility mouse events.
        cancelLongPressTimer();

        const isTwoFingerTap = twoFingerTapCandidate &&
            currentTouchCount == 0 &&
            !isPinchZooming &&
            Date.now() - twoFingerTapStartTime <= TwoFingerTapMaxMs;
        if (currentTouchCount == 0) {
            twoFingerTapCandidate = false;
        }

        // We handle these gestures ourselves; stop the browser's synthesized mouse click.
        const handledByPointerUp = Date.now() - touchLongPressFinishedAt < 500;
        if ((isTwoFingerTap || longPressStarted || isDragging || handledByPointerUp) && e.cancelable) {
            e.preventDefault();
        }

        if (isTwoFingerTap && !ViewerApp.ViewOnlyMode) {
            const rect = ScreenViewer.getBoundingClientRect();
            const tapPercentX = (twoFingerStart1.X - rect.left) / ScreenViewer.clientWidth;
            const tapPercentY = (twoFingerStart1.Y - rect.top) / ScreenViewer.clientHeight;
            resetTouchState();
            await ViewerApp.MessageSender.SendMouseMove(tapPercentX, tapPercentY);
            await ViewerApp.MessageSender.SendMouseDown(2, tapPercentX, tapPercentY);
            await ViewerApp.MessageSender.SendMouseUp(2, tapPercentX, tapPercentY);
            return;
        }

        if (currentTouchCount == 0) {
            isPinchZooming = false;
            isScrolling = false;
            lastScrollTouchY1 = null;
            lastScrollTouchY2 = null;
            startPinchPoint1 = null;
            startPinchPoint2 = null;
        }

        await finishTouchLongPress(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
    });

    ScreenViewer.addEventListener("touchcancel", () => {
        cancelLongPressTimer();
        twoFingerTapCandidate = false;
        if (isDragging && !ViewerApp.ViewOnlyMode) {
            ViewerApp.MessageSender.SendMouseUp(0, lastDragPercentX, lastDragPercentY);
        }
        resetTouchState();
    });

    ScreenViewer.addEventListener("contextmenu", async (ev) => {
        ev.preventDefault();

        if (ViewerApp.ViewOnlyMode) {
            return;
        }

        if (currentPointerDevice == "touch") {
            // Android fires this on long-press; iOS doesn't, so the touchstart timer
            // does the same thing.  Whichever comes first starts the long-press.
            const rect = ScreenViewer.getBoundingClientRect();
            await startLongPress(ev.clientX - rect.left, ev.clientY - rect.top);
        }
    });

    ScreenViewer.addEventListener("wheel", async function (e: WheelEvent) {
        e.preventDefault();
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        await ViewerApp.MessageSender.SendMouseWheel(e.deltaX, e.deltaY);
    });

    TouchKeyboardInput.addEventListener("keydown", async (ev) => {
        // Android keyboards often report "Unidentified" (keyCode 229) here; beforeinput and
        // input handle those.
        if (ev.isComposing || ev.key === "Unidentified" || ev.keyCode === 229) {
            return;
        }

        if (ev.key.length == 1 && (ev.ctrlKey || ev.altKey || ev.metaKey)) {
            // Hardware keyboard shortcut, e.g. Ctrl+C on a tablet keyboard.
            ev.preventDefault();
            if (ev.ctrlKey) activeModifiers.add("Control");
            if (ev.altKey) activeModifiers.add("Alt");
            if (ev.shiftKey) activeModifiers.add("Shift");
            if (ev.metaKey) activeModifiers.add("Meta");
            await sendKeyWithModifiers(ev.key.toLowerCase());
            return;
        }

        if (ev.key.length > 1 && TouchKeyboardSpecialKeys.has(ev.key)) {
            ev.preventDefault();
            await sendKeyWithModifiers(ev.key);
        }
    });

    TouchKeyboardInput.addEventListener("beforeinput", async (ev: InputEvent) => {
        let key: string;
        switch (ev.inputType) {
            case "deleteContentBackward":
            case "deleteWordBackward":
            case "deleteSoftLineBackward":
            case "deleteHardLineBackward":
                key = "Backspace";
                break;
            case "deleteContentForward":
            case "deleteWordForward":
                key = "Delete";
                break;
            case "insertLineBreak":
            case "insertParagraph":
                key = "Enter";
                break;
            default:
                return;
        }

        // When it can't be cancelled, the input handler restores the field.
        if (ev.cancelable) {
            ev.preventDefault();
        }
        await sendKeyWithModifiers(key);
    });

    TouchKeyboardInput.addEventListener("input", async (ev) => {
        const text = TouchKeyboardInput.value.split(KeyboardSentinel).join("");
        resetTouchKeyboardInput();

        if (ViewerApp.ViewOnlyMode || !text) {
            return;
        }

        await sendTouchKeyboardText(text);
    });

    TouchKeyboardInput.addEventListener("blur", (ev: FocusEvent) => {
        if (!KeyboardButton.classList.contains("toggled")) {
            return;
        }

        const next = ev.relatedTarget as HTMLElement;
        if (next && isTextEntryTarget(next)) {
            // Focus moved to another field (e.g. the Windows session list); let it have it.
            return;
        }

        if (next) {
            TouchKeyboardInput.focus();
            return;
        }

        // No related target: focus may have moved into the assistant iframe.  Check once it
        // has settled instead of stealing it back right away.
        window.setTimeout(() => {
            if (!KeyboardButton.classList.contains("toggled")) {
                return;
            }
            const active = document.activeElement as HTMLElement;
            if (active && active !== document.body && isTextEntryTarget(active)) {
                return;
            }
            TouchKeyboardInput.focus();
        }, 0);
    });

    WindowsSessionMenuButton.addEventListener("click", (ev) => {
        ev.stopPropagation();

        CloseAllPopupMenus(WindowsSessionMenu.id);
        togglePopupBelow(WindowsSessionMenu, WindowsSessionMenuButton, "right");

        window.addEventListener(
            "click",
            () => {
                CloseAllPopupMenus(null);
            },
            { once: true }
        );
    });
    WindowsSessionSelect.addEventListener("click", (ev) => {
        ev.stopPropagation();
    });
    WindowsSessionSelect.addEventListener("focus", async () => {
        await ViewerApp.MessageSender.GetWindowsSessions();
    });
    WindowsSessionSelect.addEventListener("change", async () => {
        SetStatusMessage("Switching sessions");
        ShowToast("Switching sessions");
        await ViewerApp.MessageSender.ChangeWindowsSession(
            Number(WindowsSessionSelect.selectedOptions[0].value)
        );
    });

    window.addEventListener("keydown", async function (e) {
        if (e.key === "F11") {
            e.preventDefault();
            FullScreenButton.click();
            return;
        }
        if (
            document.querySelector("input:focus") ||
            document.querySelector("textarea:focus") ||
            document.querySelector("dialog[open]")
        ) {
            return;
        }
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        if (!e.ctrlKey || !e.shiftKey || e.key.toLowerCase() != "i") {
            e.preventDefault();
        }
        await ViewerApp.MessageSender.SendKeyDown(e.key);
    });
    window.addEventListener("keyup", async function (e) {
        if (
            document.querySelector("input:focus") ||
            document.querySelector("textarea:focus")
        ) {
            return;
        }
        e.preventDefault();
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        await ViewerApp.MessageSender.SendKeyUp(e.key);
    });

    window.addEventListener("blur", async () => {
        if (ViewerApp.ViewOnlyMode) {
            return;
        }
        await ViewerApp.MessageSender.SendSetKeyStatesUp();
    });

    window.addEventListener("touchstart", () => {
        KeyboardButton.classList.remove("d-none");
    });
    if (window.matchMedia && window.matchMedia("(pointer: coarse)").matches) {
        KeyboardButton.classList.remove("d-none");
    }

    window.ondragover = function (e) {
        e.preventDefault();
        e.dataTransfer.dropEffect = "copy";
    };
    window.ondrop = function (e) {
        e.preventDefault();
        if (e.dataTransfer.files.length < 1) {
            return;
        }
        UploadFiles(e.dataTransfer.files);
    };
}

function resetTouchState() {
    longPressStarted = false;
    isDragging = false;
    isPinchZooming = false;
    isScrolling = false;
}

function cancelLongPressTimer() {
    if (longPressTimer > -1) {
        window.clearTimeout(longPressTimer);
        longPressTimer = -1;
    }
}

/**
 * Ends a touch long-press: right-click when the finger didn't move, mouse up after a drag.
 * Called from pointerup and touchend; only the first call does anything.
 */
async function finishTouchLongPress(clientX: number, clientY: number) {
    if (!longPressStarted && !isDragging) {
        return;
    }

    const wasDragging = isDragging;
    longPressStarted = false;
    isDragging = false;
    touchLongPressFinishedAt = Date.now();

    if (ViewerApp.ViewOnlyMode) {
        return;
    }

    const rect = ScreenViewer.getBoundingClientRect();
    const percentX = (clientX - rect.left) / ScreenViewer.clientWidth;
    const percentY = (clientY - rect.top) / ScreenViewer.clientHeight;

    if (wasDragging) {
        await ViewerApp.MessageSender.SendMouseUp(0, percentX, percentY);
    }
    else {
        await ViewerApp.MessageSender.SendMouseDown(2, percentX, percentY);
        await ViewerApp.MessageSender.SendMouseUp(2, percentX, percentY);
    }
}

/** Starts a touch long-press at a point on the canvas: right-click on release, drag on move. */
async function startLongPress(offsetX: number, offsetY: number) {
    cancelLongPressTimer();
    if (longPressStarted || ViewerApp.ViewOnlyMode) {
        return;
    }

    longPressStarted = true;
    longPressStartOffsetX = offsetX;
    longPressStartOffsetY = offsetY;

    try {
        // Haptic hint on Android; iOS doesn't support vibrate.
        navigator.vibrate?.(15);
    }
    catch { }

    // We're either starting a right-click or left-button drag.
    // Either way, move the cursor to the touch point.
    await ViewerApp.MessageSender.SendMouseMove(
        offsetX / ScreenViewer.clientWidth,
        offsetY / ScreenViewer.clientHeight
    );
}

/** Opens (or closes) a popup menu under its button.  On phones CSS turns it into a bottom sheet. */
function togglePopupBelow(menu: HTMLElement, anchor: HTMLElement, align: "left" | "right") {
    const rect = anchor.getBoundingClientRect();
    const margin = 8;

    if (align == "left") {
        menu.style.right = "";
        menu.style.left = `${rect.left.toFixed(0)}px`;
    } else {
        menu.style.left = "";
        menu.style.right = `${(document.body.clientWidth - rect.right).toFixed(0)}px`;
    }
    menu.style.top = `${rect.bottom.toFixed(0)}px`;
    menu.classList.toggle("open");

    if (!menu.classList.contains("open") || window.innerWidth < 768) {
        return;
    }

    // Keep it on screen.
    const menuRect = menu.getBoundingClientRect();
    if (menuRect.right > window.innerWidth - margin || menuRect.left < margin) {
        const left = Math.max(margin, Math.min(menuRect.left, window.innerWidth - margin - menuRect.width));
        menu.style.right = "";
        menu.style.left = `${left.toFixed(0)}px`;
    }
}

// ---------- Touch keyboard and special-keys bar ----------

const TouchKeyboardSpecialKeys = new Set<string>([
    "Enter", "Backspace", "Delete", "Escape", "Tab",
    "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown",
    "Home", "End", "PageUp", "PageDown", "Insert",
    "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
]);

function isTextEntryTarget(element: HTMLElement): boolean {
    return element.tagName == "IFRAME" ||
        element.matches("input, textarea, select, [contenteditable]") ||
        !!element.closest("dialog");
}

function resetTouchKeyboardInput() {
    TouchKeyboardInput.value = KeyboardSentinel;
    try {
        TouchKeyboardInput.setSelectionRange(KeyboardSentinel.length, KeyboardSentinel.length);
    }
    catch { }
}

function setTouchKeyboard(on: boolean) {
    KeyboardButton.classList.toggle("toggled", on);
    document.body.classList.toggle("touch-keyboard-on", on);
    TouchKeyboardBar.hidden = !on;
    updateKeyBarHeight();

    if (on) {
        resetTouchKeyboardInput();
        TouchKeyboardInput.focus();
    }
    else {
        clearModifiers();
        TouchKeyboardInput.blur();
    }
}

function updateKeyBarHeight() {
    const height = TouchKeyboardBar.hidden ? 0 : TouchKeyboardBar.offsetHeight;
    document.documentElement.style.setProperty("--keybar-h", `${height}px`);
}

function clearModifiers() {
    activeModifiers.clear();
    updateModifierButtons();
}

function updateModifierButtons() {
    TouchKeyboardBar.querySelectorAll<HTMLButtonElement>("button[data-modifier]").forEach(btn => {
        const active = activeModifiers.has(btn.dataset.modifier);
        btn.classList.toggle("toggled", active);
        btn.setAttribute("aria-pressed", String(active));
    });
}

async function toggleModifier(modifier: string) {
    if (!activeModifiers.has(modifier)) {
        activeModifiers.add(modifier);
        updateModifierButtons();
        return;
    }

    if (modifier == "Meta") {
        // Tapping Win twice presses it by itself (opens the Start menu).
        clearModifiers();
        if (!ViewerApp.ViewOnlyMode) {
            await ViewerApp.MessageSender.SendKeyPress("Meta");
        }
        return;
    }

    activeModifiers.delete(modifier);
    updateModifierButtons();
}

/** Sends one key, holding the sticky modifiers, then releases the modifiers. */
async function sendKeyWithModifiers(key: string) {
    const modifiers = ModifierOrder.filter(x => activeModifiers.has(x));
    clearModifiers();

    if (ViewerApp.ViewOnlyMode) {
        return;
    }

    if (modifiers.length == 0) {
        await ViewerApp.MessageSender.SendKeyPress(key);
        return;
    }

    for (const modifier of modifiers) {
        await ViewerApp.MessageSender.SendKeyDown(modifier);
    }
    await ViewerApp.MessageSender.SendKeyDown(key);
    await ViewerApp.MessageSender.SendKeyUp(key);
    for (const modifier of modifiers.slice().reverse()) {
        await ViewerApp.MessageSender.SendKeyUp(modifier);
    }
}

/** Text typed on the on-screen keyboard.  With Ctrl/Alt/Win active, the first character becomes a shortcut. */
async function sendTouchKeyboardText(text: string) {
    const chars = Array.from(text);

    if (activeModifiers.has("Control") || activeModifiers.has("Alt") || activeModifiers.has("Meta")) {
        await sendKeyWithModifiers(chars[0].toLowerCase());
        if (chars.length > 1) {
            await ViewerApp.MessageSender.SendTextTransfer(chars.slice(1).join(""), true);
        }
        return;
    }

    if (activeModifiers.has("Shift")) {
        clearModifiers();
        text = text.toUpperCase();
    }

    await ViewerApp.MessageSender.SendTextTransfer(text, true);
}

function applyTouchKeyboardBarHandlers() {
    resetTouchKeyboardInput();

    TouchKeyboardBar.querySelectorAll<HTMLButtonElement>("button").forEach(btn => {
        // Keep focus (and the on-screen keyboard) on the hidden input.
        btn.addEventListener("pointerdown", ev => ev.preventDefault());
        btn.addEventListener("mousedown", ev => ev.preventDefault());

        const key = btn.dataset.key;
        const modifier = btn.dataset.modifier;
        if (!key && !modifier) {
            return;
        }

        btn.addEventListener("click", async (ev) => {
            ev.stopPropagation();
            if (KeyboardButton.classList.contains("toggled") && document.activeElement !== TouchKeyboardInput) {
                TouchKeyboardInput.focus();
            }

            if (modifier) {
                await toggleModifier(modifier);
                return;
            }

            await sendKeyWithModifiers(key);
        });
    });
}

// ---------- On-screen keyboard tracking ----------

/**
 * Sets --kb-inset (how much of the page the on-screen keyboard covers) so the key bar,
 * the floating buttons and the assistant sheet stay above it.  iOS doesn't resize the
 * layout viewport for the keyboard, only the visual viewport.
 */
function applyViewportTracking() {
    const update = () => {
        const viewport = window.visualViewport;
        let inset = 0;
        if (viewport && (!viewport.scale || viewport.scale < 1.05)) {
            inset = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop);
        }
        document.documentElement.style.setProperty("--kb-inset", `${Math.round(inset)}px`);
        // Over the keyboard there's no need for the home-indicator safe area.
        document.body.classList.toggle("kb-open", inset > 40);
        updateKeyBarHeight();
        clampFabPosition();
    };

    window.visualViewport?.addEventListener("resize", update);
    window.visualViewport?.addEventListener("scroll", update);
    window.addEventListener("resize", update);
    update();
}

// ---------- Floating toolbar button ----------

function applyFabDragHandlers() {
    try {
        const saved = JSON.parse(localStorage.getItem(FabPositionStorageKey) || "null");
        if (saved && typeof saved.x == "number" && typeof saved.y == "number") {
            setFabPosition(saved.x * window.innerWidth, saved.y * window.innerHeight);
        }
    }
    catch { }

    MenuButton.addEventListener("pointerdown", (ev: PointerEvent) => {
        if (ev.button != 0) {
            return;
        }
        const rect = FabCluster.getBoundingClientRect();
        fabDragStart = { x: ev.clientX, y: ev.clientY, left: rect.left, top: rect.top };
        fabDragging = false;
        try {
            MenuButton.setPointerCapture(ev.pointerId);
        }
        catch { }
    });

    MenuButton.addEventListener("pointermove", (ev: PointerEvent) => {
        if (!fabDragStart) {
            return;
        }
        const dx = ev.clientX - fabDragStart.x;
        const dy = ev.clientY - fabDragStart.y;
        if (!fabDragging && Math.hypot(dx, dy) < FabDragThreshold) {
            return;
        }
        fabDragging = true;
        setFabPosition(fabDragStart.left + dx, fabDragStart.top + dy);
    });

    const endDrag = () => {
        if (fabDragging) {
            suppressMenuClickUntil = Date.now() + 400;
            const rect = FabCluster.getBoundingClientRect();
            try {
                localStorage.setItem(FabPositionStorageKey, JSON.stringify({
                    x: rect.left / window.innerWidth,
                    y: rect.top / window.innerHeight
                }));
            }
            catch { }
        }
        fabDragStart = null;
        fabDragging = false;
    };
    MenuButton.addEventListener("pointerup", endDrag);
    MenuButton.addEventListener("pointercancel", endDrag);
}

function setFabPosition(left: number, top: number) {
    const width = FabCluster.offsetWidth || 48;
    const height = FabCluster.offsetHeight || 48;
    const viewportHeight = window.visualViewport?.height || window.innerHeight;
    const margin = 4;
    left = Math.max(margin, Math.min(left, window.innerWidth - width - margin));
    top = Math.max(margin, Math.min(top, viewportHeight - height - margin));

    FabCluster.classList.add("moved");
    FabCluster.style.left = `${Math.round(left)}px`;
    FabCluster.style.top = `${Math.round(top)}px`;
}

function clampFabPosition() {
    if (!FabCluster.classList.contains("moved")) {
        return;
    }
    setFabPosition(parseFloat(FabCluster.style.left) || 0, parseFloat(FabCluster.style.top) || 0);
}