package com.signalremote.agent.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import com.signalremote.agent.AgentApplication
import com.signalremote.agent.MainActivity
import com.signalremote.agent.R
import com.signalremote.agent.device.DeviceInfoService
import com.signalremote.agent.screen.ScreenCaptureService
import com.signalremote.agent.signalr.AgentHubConnection
import com.signalremote.agent.storage.ConnectionInfoStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/**
 * Long-running foreground service that keeps the SignalR agent connection alive
 * while the app is in the background.
 *
 * Responsibilities:
 *   - Maintain a persistent connection to the SignalRemote server
 *   - Route screen-share requests to [ScreenCaptureService]
 *   - Show a persistent notification so Android does not kill the service
 *   - Restart automatically if killed via START_STICKY
 */
class AgentForegroundService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private lateinit var store: ConnectionInfoStore
    private lateinit var deviceInfoService: DeviceInfoService
    lateinit var hubConnection: AgentHubConnection
        private set

    // Screen capture state
    private var screenshotService: ScreenCaptureService? = null
    private var screenCaptureCallback: ScreenCaptureService.FrameCallback? = null
    private var activeSessionId: String? = null

    override fun onCreate() {
        super.onCreate()

        store = ConnectionInfoStore(applicationContext)
        deviceInfoService = DeviceInfoService(applicationContext)
        hubConnection = AgentHubConnection(applicationContext, store, deviceInfoService)

        registerHubCallbacks()
        (application as AgentApplication).hubConnection = hubConnection
        startForeground(NOTIFICATION_ID, buildNotification())

        scope.launch {
            try {
                hubConnection.connect()
            } catch (e: Exception) {
                Log.e(TAG, "Hub connection loop terminated: ${e.message}")
            }
        }

        Log.i(TAG, "AgentForegroundService started")
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_ACTIVATE_SCREEN_CAPTURE -> {
                val sessionId = intent.getStringExtra(EXTRA_SESSION_ID) ?: return START_STICKY
                val resultCode = intent.getIntExtra(EXTRA_PROJECTION_RESULT_CODE, 0)
                val projectionData = intent.getParcelableExtra<Intent>(EXTRA_PROJECTION_DATA)
                if (projectionData != null) {
                    activateScreenCapture(sessionId, resultCode, projectionData)
                }
            }
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        screenCaptureCallback?.let { screenshotService?.removeFrameCallback(it) }
        screenshotService = null
        screenCaptureCallback = null
        (application as AgentApplication).hubConnection = null
        hubConnection.disconnect()
        scope.cancel()
        super.onDestroy()
        Log.i(TAG, "AgentForegroundService destroyed")
    }

    // ── Screen capture activation ────────────────────────────────────────────

    /**
     * Called (via intent from MainActivity) once the user has granted the
     * MediaProjection permission. Binds the [ScreenCaptureService] frame output
     * directly to [AgentHubConnection.sendScreenFrame] for the given session.
     */
    private fun activateScreenCapture(sessionId: String, resultCode: Int, projectionData: Intent) {
        // Clean up any previous session
        screenCaptureCallback?.let { screenshotService?.removeFrameCallback(it) }

        activeSessionId = sessionId

        // Start (or restart) the capture service
        ScreenCaptureService.startCapture(this, resultCode, projectionData)

        // Wire frames → hub
        screenCaptureCallback = ScreenCaptureService.FrameCallback { jpegBytes ->
            hubConnection.sendScreenFrame(sessionId, jpegBytes)
        }

        // The ScreenCaptureService is a separate service; we keep a local reference
        // once it starts. Since it's in the same process, we can access its singleton.
        // We post-register the callback after a brief delay to allow the service to start.
        scope.launch {
            kotlinx.coroutines.delay(500)
            ScreenCaptureService.instance?.addFrameCallback(screenCaptureCallback!!)
            Log.i(TAG, "Screen capture frame callback registered for session=$sessionId")
        }
    }

    // ── Hub callback wiring ──────────────────────────────────────────────────

    private fun registerHubCallbacks() {
        hubConnection.onRemoteControlRequested = { sessionId, accessKey, userConnectionId,
                                                    requesterName, orgName, orgId ->
            Log.i(TAG, "Remote control request from $requesterName – notifying activity")
            // Broadcast to MainActivity so it can prompt for MediaProjection permission
            val intent = Intent(ACTION_REQUEST_SCREEN_CAPTURE).apply {
                putExtra(EXTRA_SESSION_ID, sessionId)
                putExtra(EXTRA_ACCESS_KEY, accessKey)
                putExtra(EXTRA_USER_CONNECTION_ID, userConnectionId)
                putExtra(EXTRA_REQUESTER_NAME, requesterName)
                putExtra(EXTRA_ORG_NAME, orgName)
                putExtra(EXTRA_ORG_ID, orgId)
            }
            sendBroadcast(intent)
        }

        hubConnection.onRestartScreenCaster = { viewerIds, sessionId, _, userConnectionId,
                                                 requesterName, orgName, orgId ->
            Log.i(TAG, "RestartScreenCaster for session=$sessionId")
            val intent = Intent(ACTION_REQUEST_SCREEN_CAPTURE).apply {
                putExtra(EXTRA_SESSION_ID, sessionId)
                putExtra(EXTRA_USER_CONNECTION_ID, userConnectionId)
                putExtra(EXTRA_REQUESTER_NAME, requesterName)
                putExtra(EXTRA_ORG_NAME, orgName)
                putExtra(EXTRA_ORG_ID, orgId)
            }
            sendBroadcast(intent)
        }

        hubConnection.onChatMessage = { senderName, message, _, _, disconnected, senderConnectionId ->
            Log.i(TAG, "Chat from $senderName: $message")
            val intent = Intent(ACTION_CHAT_RECEIVED).apply {
                putExtra(EXTRA_CHAT_SENDER, senderName)
                putExtra(EXTRA_CHAT_MESSAGE, message)
                putExtra(EXTRA_CHAT_DISCONNECTED, disconnected)
                putExtra(EXTRA_CHAT_CONNECTION_ID, senderConnectionId)
            }
            sendBroadcast(intent)
        }
    }

    // ── Notification ─────────────────────────────────────────────────────────

    private fun buildNotification(): Notification {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "Agent Status", NotificationManager.IMPORTANCE_LOW)
        )
        val pendingIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("SignalRemote Agent")
            .setContentText("Connected and ready for remote control")
            .setSmallIcon(R.drawable.ic_agent)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .build()
    }

    // ── Companion ────────────────────────────────────────────────────────────

    companion object {
        private const val TAG = "AgentForegroundService"
        private const val CHANNEL_ID = "agent_status_channel"
        private const val NOTIFICATION_ID = 1

        // Broadcast actions
        const val ACTION_REQUEST_SCREEN_CAPTURE = "com.signalremote.agent.REQUEST_SCREEN_CAPTURE"
        const val ACTION_CHAT_RECEIVED = "com.signalremote.agent.CHAT_RECEIVED"

        // Intents
        const val ACTION_ACTIVATE_SCREEN_CAPTURE = "com.signalremote.agent.ACTIVATE_SCREEN_CAPTURE"

        // Extras
        const val EXTRA_SESSION_ID = "session_id"
        const val EXTRA_ACCESS_KEY = "access_key"
        const val EXTRA_USER_CONNECTION_ID = "user_connection_id"
        const val EXTRA_REQUESTER_NAME = "requester_name"
        const val EXTRA_ORG_NAME = "org_name"
        const val EXTRA_ORG_ID = "org_id"
        const val EXTRA_CHAT_SENDER = "chat_sender"
        const val EXTRA_CHAT_MESSAGE = "chat_message"
        const val EXTRA_CHAT_DISCONNECTED = "chat_disconnected"
        const val EXTRA_CHAT_CONNECTION_ID = "chat_connection_id"
        const val EXTRA_PROJECTION_RESULT_CODE = "projection_result_code"
        const val EXTRA_PROJECTION_DATA = "projection_data"

        fun start(context: Context) {
            context.startForegroundService(Intent(context, AgentForegroundService::class.java))
        }

        fun stop(context: Context) {
            context.stopService(Intent(context, AgentForegroundService::class.java))
        }

        /** Called from [com.signalremote.agent.MainActivity] after MediaProjection permission is granted. */
        fun activateScreenCapture(context: Context, sessionId: String, resultCode: Int, data: Intent) {
            val intent = Intent(context, AgentForegroundService::class.java).apply {
                action = ACTION_ACTIVATE_SCREEN_CAPTURE
                putExtra(EXTRA_SESSION_ID, sessionId)
                putExtra(EXTRA_PROJECTION_RESULT_CODE, resultCode)
                putExtra(EXTRA_PROJECTION_DATA, data)
            }
            context.startService(intent)
        }
    }
}
