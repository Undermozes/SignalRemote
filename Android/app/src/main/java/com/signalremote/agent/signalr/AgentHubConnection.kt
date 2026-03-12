package com.signalremote.agent.signalr

import android.content.Context
import android.util.Log
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.signalremote.agent.device.DeviceInfoService
import com.signalremote.agent.models.ConnectionInfo
import com.signalremote.agent.storage.ConnectionInfoStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import java.util.UUID
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.min
import kotlin.math.pow

/**
 * Manages the persistent SignalR connection to the SignalRemote server.
 *
 * This class implements the server-side `IAgentHubClient` interface by registering
 * handlers for every method the server can invoke on the agent. It mirrors the
 * behaviour of the .NET [AgentHubConnection] in the Agent project.
 *
 * Lifecycle:
 *   call [connect] once – the coroutine loops forever with exponential back-off.
 *   call [disconnect] to cleanly tear down the connection.
 */
class AgentHubConnection(
    private val context: Context,
    private val store: ConnectionInfoStore,
    private val deviceInfoService: DeviceInfoService
) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private var hub: HubConnection? = null
    private var heartbeatJob: Job? = null
    private var isServerVerified = false

    /** True when the SignalR connection is in the Connected state. */
    val isConnected: Boolean
        get() = hub?.connectionState == HubConnectionState.CONNECTED

    // ── Public API ───────────────────────────────────────────────────────────

    /**
     * Starts the connection loop. This suspends indefinitely; cancel the scope
     * (or call [disconnect]) to stop it.
     */
    suspend fun connect() {
        var attempt = 0
        while (scope.isActive) {
            attempt++
            val delaySeconds = min(60.0, attempt.toDouble().pow(2)).toLong()
            if (attempt > 1) {
                Log.i(TAG, "Reconnecting in ${delaySeconds}s (attempt $attempt)")
                delay(delaySeconds * 1_000)
            }

            val info = store.load()

            if (info.host.isBlank()) {
                Log.e(TAG, "Host not configured. Waiting…")
                delay(RETRY_IDLE_MS)
                continue
            }
            if (info.organizationId.isBlank()) {
                Log.e(TAG, "OrganizationID not configured. Waiting…")
                delay(RETRY_IDLE_MS)
                continue
            }

            try {
                connectOnce(info)
                attempt = 0   // reset back-off on success
            } catch (e: Exception) {
                Log.e(TAG, "Connection error: ${e.message}")
            }
        }
    }

    /** Cleanly disconnects from the server and cancels all background work. */
    fun disconnect() {
        heartbeatJob?.cancel()
        try {
            hub?.stop()?.blockingAwait()
        } catch (_: Exception) {}
        hub = null
        scope.cancel()
        Log.i(TAG, "Disconnected")
    }

    /** Sends an up-to-date heartbeat to the server immediately. */
    suspend fun sendHeartbeat() {
        val info = store.load()
        if (info.organizationId.isBlank()) return
        val device = deviceInfoService.createDevice(info.deviceId, info.organizationId)
        hubSend("DeviceHeartbeat", device)
    }

    // ── Connection setup ─────────────────────────────────────────────────────

    private suspend fun connectOnce(info: ConnectionInfo) {
        hub?.stop()?.blockingAwait()

        val hubUrl = "${info.normalizedHost}/hubs/service"
        Log.i(TAG, "Connecting to $hubUrl")

        hub = HubConnectionBuilder.create(hubUrl).build()
        registerHandlers()

        hub!!.start().blockingAwait()
        Log.i(TAG, "Connected to server")

        val device = deviceInfoService.createDevice(info.deviceId, info.organizationId)
        val registered = hubInvoke(Boolean::class.javaObjectType, "DeviceCameOnline", device)

        if (!registered) {
            Log.e(TAG, "Server rejected registration (check OrganizationID)")
            hub?.stop()?.blockingAwait()
            return
        }

        if (!verifyServer(info)) {
            hub?.stop()?.blockingAwait()
            return
        }

        startHeartbeat()

        hubSend("CheckForPendingScriptRuns")
        hubSend("CheckForPendingRemoteControlSessions")

        // Keep this coroutine alive until the connection drops
        while (hub?.connectionState == HubConnectionState.CONNECTED && scope.isActive) {
            delay(CHECK_INTERVAL_MS)
        }
    }

    // ── Server verification ──────────────────────────────────────────────────

    private suspend fun verifyServer(info: ConnectionInfo): Boolean {
        return try {
            if (info.serverVerificationToken == null) {
                // First connection: generate and register our token
                val token = UUID.randomUUID().toString()
                hubSend("SetServerVerificationToken", token)
                store.updateVerificationToken(token)
                Log.i(TAG, "Server verification token set")
                true
            } else {
                val serverToken = hubInvoke(String::class.java, "GetServerVerificationToken")
                if (serverToken == info.serverVerificationToken) {
                    Log.i(TAG, "Server verified successfully")
                    isServerVerified = true
                    true
                } else {
                    Log.e(TAG, "Server verification FAILED – token mismatch. Possible MITM.")
                    isServerVerified = false
                    false
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error during server verification: ${e.message}")
            false
        }
    }

    // ── Heartbeat ────────────────────────────────────────────────────────────

    private fun startHeartbeat() {
        heartbeatJob?.cancel()
        heartbeatJob = scope.launch {
            while (isActive) {
                delay(HEARTBEAT_INTERVAL_MS)
                try {
                    sendHeartbeat()
                } catch (e: Exception) {
                    Log.w(TAG, "Heartbeat error: ${e.message}")
                }
            }
        }
    }

    // ── IAgentHubClient handlers ─────────────────────────────────────────────
    //
    // Each method below corresponds to a method in the C# IAgentHubClient
    // interface. The server invokes these on the agent via SignalR RPC.

    private fun registerHandlers() {
        val h = hub ?: return

        // Remote control
        h.on(
            "RemoteControl",
            { sessionId: String, accessKey: String, userConnectionId: String,
              requesterName: String, orgName: String, orgId: String ->
                handleRemoteControl(sessionId, accessKey, userConnectionId, requesterName, orgName, orgId)
            },
            String::class.java, String::class.java, String::class.java,
            String::class.java, String::class.java, String::class.java
        )

        h.on(
            "RestartScreenCaster",
            { viewerIds: ArrayList<*>, sessionId: String, accessKey: String,
              userConnectionId: String, requesterName: String, orgName: String, orgId: String ->
                @Suppress("UNCHECKED_CAST")
                handleRestartScreenCaster(
                    (viewerIds as? ArrayList<String>)?.toTypedArray() ?: emptyArray(),
                    sessionId, accessKey, userConnectionId, requesterName, orgName, orgId
                )
            },
            ArrayList::class.java, String::class.java, String::class.java,
            String::class.java, String::class.java, String::class.java, String::class.java
        )

        // Chat
        h.on(
            "SendChatMessage",
            { senderName: String, message: String, orgName: String, orgId: String,
              disconnected: Boolean, senderConnectionId: String ->
                handleChatMessage(senderName, message, orgName, orgId, disconnected, senderConnectionId)
            },
            String::class.java, String::class.java, String::class.java,
            String::class.java, Boolean::class.javaObjectType, String::class.java
        )

        // Command execution
        h.on(
            "ExecuteCommand",
            { shell: String, command: String, authToken: String,
              senderUsername: String, senderConnectionId: String ->
                handleExecuteCommand(shell, command, authToken, senderUsername, senderConnectionId)
            },
            String::class.java, String::class.java, String::class.java,
            String::class.java, String::class.java
        )

        h.on(
            "ExecuteCommandFromApi",
            { shell: String, authToken: String, requestId: String,
              command: String, senderUsername: String ->
                handleExecuteCommandFromApi(shell, authToken, requestId, command, senderUsername)
            },
            String::class.java, String::class.java, String::class.java,
            String::class.java, String::class.java
        )

        // Agent management
        h.on("ReinstallAgent", { handleReinstallAgent() })
        h.on("UninstallAgent", { handleUninstallAgent() })

        // Heartbeat trigger
        h.on("TriggerHeartbeat", { scope.launch { sendHeartbeat() } })

        // Logs
        h.on("DeleteLogs", { handleDeleteLogs() })
        h.on("GetLogs", { senderConnectionId: String ->
            handleGetLogs(senderConnectionId)
        }, String::class.java)

        // Script execution
        h.on(
            "RunScript",
            { savedScriptId: String, scriptRunId: Int, initiator: String,
              scriptInputType: String, authToken: String ->
                handleRunScript(savedScriptId, scriptRunId, initiator, scriptInputType, authToken)
            },
            String::class.java, Int::class.javaObjectType, String::class.java,
            String::class.java, String::class.java
        )

        // File transfer
        h.on(
            "TransferFileFromBrowserToAgent",
            { transferId: String, fileIds: ArrayList<*>, requesterId: String, expiringToken: String ->
                @Suppress("UNCHECKED_CAST")
                handleTransferFile(
                    transferId,
                    (fileIds as? ArrayList<String>)?.toTypedArray() ?: emptyArray(),
                    requesterId,
                    expiringToken
                )
            },
            String::class.java, ArrayList::class.java, String::class.java, String::class.java
        )

        // PowerShell completions – not applicable on Android; send empty response
        h.on(
            "GetPowerShellCompletions",
            { inputText: String, currentIndex: Int, intent: String, forward: Boolean,
              senderConnectionId: String ->
                handleGetPowerShellCompletions(inputText, currentIndex, intent, forward, senderConnectionId)
            },
            String::class.java, Int::class.javaObjectType, String::class.java,
            Boolean::class.javaObjectType, String::class.java
        )

        // Windows-only – log and ignore
        h.on(
            "ChangeWindowsSession",
            { viewerConnectionId: String, sessionId: String, accessKey: String,
              userConnectionId: String, requesterName: String, orgName: String,
              orgId: String, targetSessionId: Int ->
                Log.d(TAG, "ChangeWindowsSession ignored on Android (viewer=$viewerConnectionId)")
            },
            String::class.java, String::class.java, String::class.java, String::class.java,
            String::class.java, String::class.java, String::class.java, Int::class.javaObjectType
        )
        h.on("InvokeCtrlAltDel", {
            Log.d(TAG, "InvokeCtrlAltDel ignored on Android")
        })

        // Wake-on-LAN – not applicable on Android
        h.on("WakeDevice", { macAddress: String ->
            Log.d(TAG, "WakeDevice ignored on Android (mac=$macAddress)")
        }, String::class.java)
    }

    // ── Handler implementations ──────────────────────────────────────────────

    private fun handleRemoteControl(
        sessionId: String, accessKey: String, userConnectionId: String,
        requesterName: String, orgName: String, orgId: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "RemoteControl before server verified"); return }
        Log.i(TAG, "RemoteControl requested by $requesterName (session=$sessionId)")
        // Notify the UI so it can request MediaProjection permission
        onRemoteControlRequested?.invoke(sessionId, accessKey, userConnectionId, requesterName, orgName, orgId)
    }

    private fun handleRestartScreenCaster(
        viewerIds: Array<String>, sessionId: String, accessKey: String,
        userConnectionId: String, requesterName: String, orgName: String, orgId: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "RestartScreenCaster before server verified"); return }
        Log.i(TAG, "RestartScreenCaster for session=$sessionId, viewers=${viewerIds.size}")
        onRestartScreenCaster?.invoke(viewerIds, sessionId, accessKey, userConnectionId, requesterName, orgName, orgId)
    }

    private fun handleChatMessage(
        senderName: String, message: String, orgName: String, orgId: String,
        disconnected: Boolean, senderConnectionId: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "Chat before server verified"); return }
        Log.i(TAG, "Chat from $senderName: $message (disconnected=$disconnected)")
        onChatMessage?.invoke(senderName, message, orgName, orgId, disconnected, senderConnectionId)
    }

    private fun handleExecuteCommand(
        shell: String, command: String, authToken: String,
        senderUsername: String, senderConnectionId: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "ExecuteCommand before server verified"); return }
        Log.i(TAG, "ExecuteCommand: shell=$shell, sender=$senderUsername")
        scope.launch {
            val output = executeShellCommand(command)
            Log.d(TAG, "Command output: $output")
        }
    }

    private fun handleExecuteCommandFromApi(
        shell: String, authToken: String, requestId: String,
        command: String, senderUsername: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "ExecuteCommandFromApi before server verified"); return }
        Log.i(TAG, "ExecuteCommandFromApi: shell=$shell, requestId=$requestId")
        scope.launch {
            val output = executeShellCommand(command)
            Log.d(TAG, "Command output: $output")
        }
    }

    private fun handleRunScript(
        savedScriptId: String, scriptRunId: Int, initiator: String,
        scriptInputType: String, authToken: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "RunScript before server verified"); return }
        Log.i(TAG, "RunScript: scriptId=$savedScriptId, initiator=$initiator")
        // Script execution on Android is limited; log and skip
    }

    private fun handleTransferFile(
        transferId: String, fileIds: Array<String>,
        requesterId: String, expiringToken: String
    ) {
        if (!isServerVerified) { Log.w(TAG, "TransferFile before server verified"); return }
        Log.i(TAG, "TransferFileFromBrowserToAgent: transferId=$transferId, files=${fileIds.size}")
        scope.launch {
            val info = store.load()
            fileIds.forEach { fileId ->
                try {
                    downloadFile(info.normalizedHost, fileId, expiringToken)
                } catch (e: Exception) {
                    Log.e(TAG, "Error downloading file $fileId: ${e.message}")
                }
            }
        }
    }

    private fun handleGetLogs(senderConnectionId: String) {
        scope.launch {
            val logChunk = "Android agent logs not available via this channel."
            hubSend("SendLogs", logChunk, senderConnectionId)
        }
    }

    private fun handleDeleteLogs() {
        Log.i(TAG, "DeleteLogs requested (no-op on Android)")
    }

    private fun handleGetPowerShellCompletions(
        inputText: String, currentIndex: Int, intent: String,
        forward: Boolean, senderConnectionId: String
    ) {
        Log.d(TAG, "GetPowerShellCompletions ignored on Android")
        scope.launch {
            // Return an empty completion result
            hubSend("ReturnPowerShellCompletions", "{}", intent, senderConnectionId)
        }
    }

    private fun handleReinstallAgent() {
        Log.i(TAG, "ReinstallAgent requested – not supported on Android")
    }

    private fun handleUninstallAgent() {
        Log.i(TAG, "UninstallAgent requested – not supported on Android")
    }

    // ── Screen frame sending ─────────────────────────────────────────────────

    /**
     * Called by [ScreenCaptureService] with each encoded JPEG frame.
     * Sends the frame bytes to the server's DesktopHub under the session ID.
     */
    fun sendScreenFrame(sessionId: String, jpegBytes: ByteArray) {
        if (!isServerVerified) return
        scope.launch {
            try {
                hub?.send("SendDesktopStream", sessionId, jpegBytes)?.blockingAwait()
            } catch (e: Exception) {
                Log.w(TAG, "Error sending frame: ${e.message}")
            }
        }
    }

    // ── Chat sending ─────────────────────────────────────────────────────────

    /** Sends a chat reply to the browser that initiated the chat. */
    suspend fun sendChatReply(text: String, browserConnectionId: String) {
        hubSend("Chat", text, false, browserConnectionId)
    }

    // ── Shell command execution ──────────────────────────────────────────────

    private suspend fun executeShellCommand(command: String): String =
        withContext(Dispatchers.IO) {
            try {
                val process = Runtime.getRuntime().exec(arrayOf("sh", "-c", command))
                process.waitFor()
                process.inputStream.bufferedReader().readText() +
                        process.errorStream.bufferedReader().readText()
            } catch (e: Exception) {
                "Error executing command: ${e.message}"
            }
        }

    // ── File download ────────────────────────────────────────────────────────

    private suspend fun downloadFile(host: String, fileId: String, token: String) =
        withContext(Dispatchers.IO) {
            val url = java.net.URL("$host/API/FileSharing/$fileId")
            val conn = url.openConnection() as java.net.HttpURLConnection
            try {
                conn.setRequestProperty("X-Expiring-Token", token)
                conn.connect()
                val fileName = conn.getHeaderField("Content-Disposition")
                    ?.substringAfter("filename=")
                    ?.trim('"')
                    ?: "file_$fileId"

                val dest = context.getExternalFilesDir(null)?.let {
                    java.io.File(it, fileName)
                } ?: return@withContext

                conn.inputStream.use { input ->
                    dest.outputStream().use { output ->
                        input.copyTo(output)
                    }
                }
                Log.i(TAG, "Downloaded file to ${dest.absolutePath}")
            } finally {
                conn.disconnect()
            }
        }

    // ── SignalR helpers ──────────────────────────────────────────────────────

    private suspend fun hubSend(method: String, vararg args: Any?) =
        withContext(Dispatchers.IO) {
            try {
                hub?.send(method, *args)?.blockingAwait()
            } catch (e: Exception) {
                Log.w(TAG, "hubSend($method) error: ${e.message}")
            }
        }

    @Suppress("UNCHECKED_CAST")
    private suspend fun <T> hubInvoke(returnType: Class<T>, method: String, vararg args: Any?): T =
        withContext(Dispatchers.IO) {
            suspendCancellableCoroutine { cont ->
                val single = hub?.invoke(returnType, method, *args)
                if (single == null) {
                    cont.resumeWithException(IllegalStateException("Hub not connected"))
                    return@suspendCancellableCoroutine
                }
                single.subscribe(
                    { result -> cont.resume(result) },
                    { error -> cont.resumeWithException(error) }
                )
            }
        }

    // ── Callbacks (set by AgentForegroundService) ────────────────────────────

    var onRemoteControlRequested: ((String, String, String, String, String, String) -> Unit)? = null
    var onRestartScreenCaster: ((Array<String>, String, String, String, String, String, String) -> Unit)? = null
    var onChatMessage: ((String, String, String, String, Boolean, String) -> Unit)? = null

    // ── Constants ────────────────────────────────────────────────────────────

    companion object {
        private const val TAG = "AgentHubConnection"
        private const val HEARTBEAT_INTERVAL_MS = 5L * 60 * 1_000   // 5 minutes
        private const val RETRY_IDLE_MS = 10_000L
        private const val CHECK_INTERVAL_MS = 5_000L
    }
}
