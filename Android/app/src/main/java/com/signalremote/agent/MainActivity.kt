package com.signalremote.agent

import android.Manifest
import android.app.AlertDialog
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.Editable
import android.text.TextWatcher
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.signalremote.agent.databinding.ActivityMainBinding
import com.signalremote.agent.service.AgentForegroundService
import com.signalremote.agent.storage.ConnectionInfoStore
import kotlinx.coroutines.launch

/**
 * Main activity – provides a simple UI for:
 *   - Configuring the server URL and organisation ID
 *   - Starting / stopping the background agent service
 *   - Prompting for MediaProjection permission when a remote-control session starts
 *   - Displaying incoming chat messages
 */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var store: ConnectionInfoStore

    // Pending remote-control session details (filled on broadcast receipt)
    private var pendingSessionId: String? = null
    private var pendingAccessKey: String? = null
    private var pendingUserConnectionId: String? = null
    private var pendingRequesterName: String? = null
    private var pendingOrgName: String? = null
    private var pendingOrgId: String? = null

    // MediaProjection permission launcher
    private val projectionLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK && result.data != null) {
            val sessionId = pendingSessionId ?: return@registerForActivityResult
            // Tell AgentForegroundService to start capturing and wire frames to the hub
            AgentForegroundService.activateScreenCapture(this, sessionId, result.resultCode, result.data!!)
            Toast.makeText(this, "Screen sharing started", Toast.LENGTH_SHORT).show()
        } else {
            Toast.makeText(this, "Screen capture permission denied", Toast.LENGTH_SHORT).show()
        }
    }

    // POST_NOTIFICATIONS permission launcher (Android 13+)
    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (!granted) {
            Toast.makeText(this, "Notification permission denied – ongoing status may not show", Toast.LENGTH_LONG).show()
        }
        startAgent()
    }

    // Receives broadcasts from AgentForegroundService
    private val agentBroadcastReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            when (intent.action) {
                AgentForegroundService.ACTION_REQUEST_SCREEN_CAPTURE -> {
                    pendingSessionId = intent.getStringExtra(AgentForegroundService.EXTRA_SESSION_ID)
                    pendingAccessKey = intent.getStringExtra(AgentForegroundService.EXTRA_ACCESS_KEY)
                    pendingUserConnectionId = intent.getStringExtra(AgentForegroundService.EXTRA_USER_CONNECTION_ID)
                    pendingRequesterName = intent.getStringExtra(AgentForegroundService.EXTRA_REQUESTER_NAME)
                    pendingOrgName = intent.getStringExtra(AgentForegroundService.EXTRA_ORG_NAME)
                    pendingOrgId = intent.getStringExtra(AgentForegroundService.EXTRA_ORG_ID)
                    promptForScreenCapturePermission()
                }
                AgentForegroundService.ACTION_CHAT_RECEIVED -> {
                    val sender = intent.getStringExtra(AgentForegroundService.EXTRA_CHAT_SENDER) ?: ""
                    val message = intent.getStringExtra(AgentForegroundService.EXTRA_CHAT_MESSAGE) ?: ""
                    val disconnected = intent.getBooleanExtra(AgentForegroundService.EXTRA_CHAT_DISCONNECTED, false)
                    val connectionId = intent.getStringExtra(AgentForegroundService.EXTRA_CHAT_CONNECTION_ID) ?: ""
                    showChatDialog(sender, message, disconnected, connectionId)
                }
            }
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        store = ConnectionInfoStore(applicationContext)

        populateFields()
        setupListeners()
    }

    override fun onResume() {
        super.onResume()
        val filter = IntentFilter().apply {
            addAction(AgentForegroundService.ACTION_REQUEST_SCREEN_CAPTURE)
            addAction(AgentForegroundService.ACTION_CHAT_RECEIVED)
        }
        ContextCompat.registerReceiver(
            this, agentBroadcastReceiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED
        )
        updateAccessibilityStatus()
    }

    override fun onPause() {
        super.onPause()
        unregisterReceiver(agentBroadcastReceiver)
    }

    // ── UI setup ──────────────────────────────────────────────────────────────

    private fun populateFields() {
        val info = store.load()
        binding.editTextHost.setText(info.host)
        binding.editTextOrgId.setText(info.organizationId)
        binding.textDeviceId.text = "Device ID: ${info.deviceId}"
    }

    private fun setupListeners() {
        // Save settings on text change
        val watcher = object : TextWatcher {
            override fun afterTextChanged(s: Editable?) = saveSettings()
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
        }
        binding.editTextHost.addTextChangedListener(watcher)
        binding.editTextOrgId.addTextChangedListener(watcher)

        binding.buttonStartAgent.setOnClickListener { onStartAgentClicked() }
        binding.buttonStopAgent.setOnClickListener { onStopAgentClicked() }
        binding.buttonAccessibility.setOnClickListener { openAccessibilitySettings() }
    }

    private fun updateAccessibilityStatus() {
        val connected = com.signalremote.agent.service.InputInjectionService.isConnected
        binding.textAccessibilityStatus.text = if (connected) {
            "Input injection: Enabled ✓"
        } else {
            "Input injection: Disabled (tap to enable)"
        }
        binding.buttonAccessibility.visibility = if (connected) View.GONE else View.VISIBLE
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private fun onStartAgentClicked() {
        if (!validateSettings()) return
        saveSettings()
        requestNotificationPermissionAndStart()
    }

    private fun onStopAgentClicked() {
        AgentForegroundService.stop(this)
        ScreenCaptureService.stopCapture(this)
        Toast.makeText(this, "Agent stopped", Toast.LENGTH_SHORT).show()
    }

    private fun requestNotificationPermissionAndStart() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED
        ) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        } else {
            startAgent()
        }
    }

    private fun startAgent() {
        AgentForegroundService.start(this)
        Toast.makeText(this, "Agent started", Toast.LENGTH_SHORT).show()
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private fun validateSettings(): Boolean {
        val host = binding.editTextHost.text.toString().trim()
        val orgId = binding.editTextOrgId.text.toString().trim()

        if (host.isBlank()) {
            binding.editTextHost.error = "Server URL is required"
            return false
        }
        if (orgId.isBlank()) {
            binding.editTextOrgId.error = "Organisation ID is required"
            return false
        }
        return true
    }

    private fun saveSettings() {
        val current = store.load()
        store.save(
            current.copy(
                host = binding.editTextHost.text.toString().trim(),
                organizationId = binding.editTextOrgId.text.toString().trim()
            )
        )
    }

    // ── Screen capture permission ─────────────────────────────────────────────

    private fun promptForScreenCapturePermission() {
        AlertDialog.Builder(this)
            .setTitle("Remote Control Request")
            .setMessage("${pendingRequesterName ?: "A user"} is requesting to view your screen. Allow?")
            .setPositiveButton("Allow") { _, _ ->
                val mpManager = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
                projectionLauncher.launch(mpManager.createScreenCaptureIntent())
            }
            .setNegativeButton("Deny", null)
            .show()
    }

    // ── Chat dialog ───────────────────────────────────────────────────────────

    private fun showChatDialog(
        sender: String, message: String, disconnected: Boolean, connectionId: String
    ) {
        if (disconnected) {
            Toast.makeText(this, "$sender disconnected", Toast.LENGTH_SHORT).show()
            return
        }
        AlertDialog.Builder(this)
            .setTitle("Message from $sender")
            .setMessage(message)
            .setPositiveButton("Reply") { _, _ ->
                showReplyDialog(connectionId)
            }
            .setNegativeButton("Dismiss", null)
            .show()
    }

    private fun showReplyDialog(connectionId: String) {
        val input = android.widget.EditText(this)
        AlertDialog.Builder(this)
            .setTitle("Send Reply")
            .setView(input)
            .setPositiveButton("Send") { _, _ ->
                val replyText = input.text.toString().trim()
                if (replyText.isNotEmpty()) {
                    val hub = (application as AgentApplication).hubConnection
                    if (hub != null) {
                        lifecycleScope.launch {
                            hub.sendChatReply(replyText, connectionId)
                        }
                        Toast.makeText(this, "Reply sent", Toast.LENGTH_SHORT).show()
                    } else {
                        Toast.makeText(this, "Agent not connected – reply not sent", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton("Cancel", null)
            .show()
    }

    // ── Accessibility ─────────────────────────────────────────────────────────

    private fun openAccessibilitySettings() {
        startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
    }
}
