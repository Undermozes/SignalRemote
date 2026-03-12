# SignalRemote Agent – Android

Native Android remote-desktop agent for the [SignalRemote](../README.md) platform.

## Overview

This app runs as a **persistent foreground service** on an Android device and connects to a
SignalRemote server via SignalR. Once connected, the device can be:

* **Remotely viewed** – screen is captured via the MediaProjection API and streamed to the operator's browser.
* **Remotely controlled** – touch/gesture input is injected via an Accessibility Service.
* **Chatted with** – the operator can send text messages directly to the device.
* **Managed** – heartbeat keeps device status up-to-date in the server dashboard.

## Requirements

| Requirement        | Detail                          |
|--------------------|---------------------------------|
| Android version    | 8.0 (API 26) or higher          |
| Permissions        | Notification, MediaProjection, Accessibility Service |
| SignalRemote server | v2024+ (SignalR hub at `/hubs/service`) |

## Project Structure

```
Android/
├── app/src/main/java/com/signalremote/agent/
│   ├── AgentApplication.kt           – Application entry point
│   ├── MainActivity.kt               – Connection configuration UI
│   ├── device/
│   │   └── DeviceInfoService.kt      – CPU / RAM / storage metrics
│   ├── models/
│   │   ├── ConnectionInfo.kt         – Server connection config
│   │   └── DeviceClientDto.kt        – Device registration DTO
│   ├── screen/
│   │   └── ScreenCaptureService.kt   – MediaProjection screen capture
│   ├── service/
│   │   ├── AgentForegroundService.kt – Persistent background service
│   │   └── InputInjectionService.kt  – Accessibility-based touch injection
│   ├── signalr/
│   │   └── AgentHubConnection.kt     – SignalR client (all IAgentHubClient methods)
│   └── storage/
│       └── ConnectionInfoStore.kt    – Encrypted SharedPreferences persistence
└── app/src/test/                     – JVM unit tests
```

## Building

### Prerequisites

* Android Studio Ladybug (2024.2.1) or later, **or** JDK 17 + Android SDK (API 35)
* Gradle 8.9+ (the wrapper script is included)

### Steps

```bash
cd Android

# Debug APK
./gradlew assembleDebug

# Release APK (requires signing config)
./gradlew assembleRelease

# Run JVM unit tests
./gradlew test

# Install on connected device
./gradlew installDebug
```

The debug APK will be at:
```
Android/app/build/outputs/apk/debug/app-debug.apk
```

## First-Time Setup on Device

1. Install the APK.
2. Open the **SignalRemote Agent** app.
3. Enter the **Server URL** (e.g. `https://your-server.example.com`).
4. Enter your **Organisation ID** (from the SignalRemote web UI → Account → Organisation ID).
5. Tap **Start Agent** – the app will start a foreground service and connect.
6. *(Optional for input injection)* Tap **Enable Input Injection** → grant Accessibility permission.

The device now appears in the SignalRemote server dashboard under **Devices**.

## Remote Control Flow

```
Browser (operator)
  │  clicks "Remote Control" on device card
  ▼
SignalRemote Server
  │  sends RemoteControl(sessionId, …) via SignalR
  ▼
AgentForegroundService  (broadcasts ACTION_REQUEST_SCREEN_CAPTURE)
  ▼
MainActivity  (shows dialog "Allow screen share?")
  │  user taps Allow → MediaProjection permission granted
  ▼
ScreenCaptureService  (captures JPEG frames at ~10 fps)
  ▼
AgentHubConnection.sendScreenFrame(sessionId, jpegBytes)
  ▼
SignalRemote Server  (relays frames to browser)
  ▼
Browser  (renders frames in remote control viewer)
```

## Security

* **Server verification token** – the agent exchanges a secret token with the server on first
  connection and verifies it on every subsequent reconnect, preventing man-in-the-middle attacks.
* **Encrypted storage** – the token, device ID, and org ID are stored in
  `EncryptedSharedPreferences` (AES-256-GCM).
* **Permission gating** – screen capture requires explicit user approval via the Android
  MediaProjection permission dialog each session.

## Permissions Explained

| Permission | Why |
|---|---|
| `INTERNET` | Connect to the SignalRemote server |
| `FOREGROUND_SERVICE` | Keep the agent alive in the background |
| `FOREGROUND_SERVICE_MEDIA_PROJECTION` | Required for screen capture foreground service (API 34+) |
| `POST_NOTIFICATIONS` | Show the ongoing agent-status notification (API 33+) |
| `ACCESS_NETWORK_STATE` / `ACCESS_WIFI_STATE` | Gather MAC addresses for device identification |
| `READ_PHONE_STATE` | Read device model name |

## Limitations

* **Script execution** – `RunScript` (PowerShell scripts) is logged but not executed (no PowerShell on Android).
* **Wake-on-LAN** – `WakeDevice` is not applicable on Android.
* **ChangeWindowsSession** / **InvokeCtrlAltDel** – Windows-only; silently ignored.
* **PowerShell completions** – returns an empty response.
* **File downloads** – saved to the app's external files directory (`/sdcard/Android/data/com.signalremote.agent/files/`).
