package com.signalremote.agent.service

import android.accessibilityservice.AccessibilityService
import android.accessibilityservice.GestureDescription
import android.graphics.Path
import android.os.Build
import android.util.Log
import android.view.accessibility.AccessibilityEvent
import androidx.annotation.RequiresApi

/**
 * Accessibility service that enables remote input injection.
 *
 * When enabled by the user in Android Settings → Accessibility, this service allows
 * the agent to simulate touch gestures received from the remote operator.
 *
 * Users must manually enable this service via:
 *   Settings → Accessibility → SignalRemote Agent → Enable
 */
class InputInjectionService : AccessibilityService() {

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // No-op: we only use this service for input injection, not event monitoring.
    }

    override fun onInterrupt() {
        Log.d(TAG, "InputInjectionService interrupted")
    }

    override fun onServiceConnected() {
        super.onServiceConnected()
        instance = this
        Log.i(TAG, "InputInjectionService connected")
    }

    override fun onUnbind(intent: android.content.Intent?): Boolean {
        instance = null
        return super.onUnbind(intent)
    }

    // ── Input injection API ──────────────────────────────────────────────────

    /**
     * Dispatches a single tap at the given normalised coordinates.
     *
     * @param normX  X position in [0.0, 1.0] relative to screen width
     * @param normY  Y position in [0.0, 1.0] relative to screen height
     */
    @RequiresApi(Build.VERSION_CODES.N)
    fun tap(normX: Float, normY: Float) {
        val metrics = resources.displayMetrics
        val x = normX * metrics.widthPixels
        val y = normY * metrics.heightPixels

        val path = Path().apply { moveTo(x, y) }
        val stroke = GestureDescription.StrokeDescription(path, 0, TAP_DURATION_MS)
        dispatchGesture(GestureDescription.Builder().addStroke(stroke).build(), null, null)
    }

    /**
     * Dispatches a swipe gesture between two normalised positions.
     */
    @RequiresApi(Build.VERSION_CODES.N)
    fun swipe(fromNormX: Float, fromNormY: Float, toNormX: Float, toNormY: Float) {
        val metrics = resources.displayMetrics
        val fromX = fromNormX * metrics.widthPixels
        val fromY = fromNormY * metrics.heightPixels
        val toX = toNormX * metrics.widthPixels
        val toY = toNormY * metrics.heightPixels

        val path = Path().apply {
            moveTo(fromX, fromY)
            lineTo(toX, toY)
        }
        val stroke = GestureDescription.StrokeDescription(path, 0, SWIPE_DURATION_MS)
        dispatchGesture(GestureDescription.Builder().addStroke(stroke).build(), null, null)
    }

    companion object {
        private const val TAG = "InputInjectionService"
        private const val TAP_DURATION_MS = 100L
        private const val SWIPE_DURATION_MS = 300L

        /** Singleton reference set when the service binds. May be null. */
        @Volatile
        var instance: InputInjectionService? = null
            private set

        /** Returns true if the accessibility service is currently connected. */
        val isConnected: Boolean get() = instance != null
    }
}
