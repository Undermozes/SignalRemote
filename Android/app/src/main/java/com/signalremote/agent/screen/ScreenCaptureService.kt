package com.signalremote.agent.screen

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.ImageReader
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.IBinder
import android.util.DisplayMetrics
import android.util.Log
import android.view.WindowManager
import androidx.core.app.NotificationCompat
import com.signalremote.agent.R
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.ByteArrayOutputStream

/**
 * Foreground service that captures the device screen using [MediaProjection] and
 * delivers compressed JPEG frames to registered [FrameCallback] listeners.
 *
 * Lifecycle:
 *   1. Request MEDIA_PROJECTION permission via [MediaProjectionManager] in an Activity.
 *   2. Start this service with the result intent via [startCapture].
 *   3. Register a [FrameCallback] to receive encoded frames.
 *   4. Call [stopCapture] (or stop the service) to release resources.
 */
class ScreenCaptureService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    private var mediaProjection: MediaProjection? = null
    private var virtualDisplay: VirtualDisplay? = null
    private var imageReader: ImageReader? = null
    private var captureJob: Job? = null

    private var screenWidth = 0
    private var screenHeight = 0
    private var screenDpi = 0

    // Listeners receive encoded JPEG frames
    private val callbacks = mutableListOf<FrameCallback>()

    /** Called by [AgentHubConnection] to subscribe to screen frames. */
    fun addFrameCallback(cb: FrameCallback) {
        synchronized(callbacks) { callbacks.add(cb) }
    }

    fun removeFrameCallback(cb: FrameCallback) {
        synchronized(callbacks) { callbacks.remove(cb) }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        resolveScreenMetrics()
        startForeground(NOTIFICATION_ID, buildNotification())
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START -> {
                val resultCode = intent.getIntExtra(EXTRA_RESULT_CODE, 0)
                val data = intent.getParcelableExtra<Intent>(EXTRA_DATA)
                if (data != null) startCapture(resultCode, data)
            }
            ACTION_STOP -> stopCapture()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        stopCapture()
        scope.cancel()
        instance = null
        super.onDestroy()
    }

    // ── Capture control ──────────────────────────────────────────────────────

    private fun startCapture(resultCode: Int, data: Intent) {
        val mpManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        mediaProjection = mpManager.getMediaProjection(resultCode, data)

        imageReader = ImageReader.newInstance(screenWidth, screenHeight, PixelFormat.RGBA_8888, 2)

        virtualDisplay = mediaProjection?.createVirtualDisplay(
            "SignalRemoteCapture",
            screenWidth, screenHeight, screenDpi,
            DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
            imageReader?.surface, null, null
        )

        captureJob = scope.launch {
            while (isActive) {
                encodeAndDispatchFrame()
                delay(FRAME_INTERVAL_MS)
            }
        }
        Log.i(TAG, "Screen capture started at ${screenWidth}x$screenHeight @$screenDpi dpi")
    }

    fun stopCapture() {
        captureJob?.cancel()
        captureJob = null
        virtualDisplay?.release()
        virtualDisplay = null
        imageReader?.close()
        imageReader = null
        mediaProjection?.stop()
        mediaProjection = null
        Log.i(TAG, "Screen capture stopped")
    }

    // ── Frame encoding ───────────────────────────────────────────────────────

    private fun encodeAndDispatchFrame() {
        val image = imageReader?.acquireLatestImage() ?: return
        try {
            val planes = image.planes
            val buffer = planes[0].buffer
            val pixelStride = planes[0].pixelStride
            val rowStride = planes[0].rowStride
            val rowPadding = rowStride - pixelStride * screenWidth

            val bitmap = Bitmap.createBitmap(
                screenWidth + rowPadding / pixelStride,
                screenHeight,
                Bitmap.Config.ARGB_8888
            )
            bitmap.copyPixelsFromBuffer(buffer)

            // Crop to exact screen dimensions (remove row padding artefact)
            val cropped = Bitmap.createBitmap(bitmap, 0, 0, screenWidth, screenHeight)
            bitmap.recycle()

            val out = ByteArrayOutputStream()
            cropped.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, out)
            cropped.recycle()

            val frameBytes = out.toByteArray()
            synchronized(callbacks) {
                callbacks.forEach { it.onFrame(frameBytes) }
            }
        } finally {
            image.close()
        }
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    private fun resolveScreenMetrics() {
        val wm = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        wm.defaultDisplay.getMetrics(metrics)
        screenWidth = metrics.widthPixels
        screenHeight = metrics.heightPixels
        screenDpi = metrics.densityDpi
    }

    // ── Notification ─────────────────────────────────────────────────────────

    private fun buildNotification(): Notification {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, "Screen Capture", NotificationManager.IMPORTANCE_LOW)
        )
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("SignalRemote – Screen capture active")
            .setContentText("Sharing screen with remote operator")
            .setSmallIcon(R.drawable.ic_agent)
            .setOngoing(true)
            .build()
    }

    // ── Companion ────────────────────────────────────────────────────────────

    companion object {
        private const val TAG = "ScreenCaptureService"
        private const val CHANNEL_ID = "screen_capture_channel"
        private const val NOTIFICATION_ID = 2
        private const val FRAME_INTERVAL_MS = 100L  // ~10 fps
        private const val JPEG_QUALITY = 60

        const val ACTION_START = "com.signalremote.agent.START_CAPTURE"
        const val ACTION_STOP = "com.signalremote.agent.STOP_CAPTURE"
        const val EXTRA_RESULT_CODE = "result_code"
        const val EXTRA_DATA = "data"

        /** Set in [onCreate] and cleared in [onDestroy]. Allows in-process services to register callbacks. */
        @Volatile
        var instance: ScreenCaptureService? = null
            private set

        /** Convenience helper to start capture from an Activity. */
        fun startCapture(context: Context, resultCode: Int, data: Intent) {
            val intent = Intent(context, ScreenCaptureService::class.java).apply {
                action = ACTION_START
                putExtra(EXTRA_RESULT_CODE, resultCode)
                putExtra(EXTRA_DATA, data)
            }
            context.startForegroundService(intent)
        }

        fun stopCapture(context: Context) {
            val intent = Intent(context, ScreenCaptureService::class.java).apply {
                action = ACTION_STOP
            }
            context.startService(intent)
        }
    }

    /** Consumer of encoded JPEG screen frames. */
    fun interface FrameCallback {
        fun onFrame(jpegBytes: ByteArray)
    }
}
