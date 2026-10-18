package com.signalremote.agent

import android.app.Application
import android.util.Log
import com.signalremote.agent.signalr.AgentHubConnection
import com.signalremote.agent.storage.ConnectionInfoStore

/**
 * Application class – initialises global state that must be available
 * before any Activity or Service starts.
 */
class AgentApplication : Application() {

    /** Application-wide connection info store (single shared instance). */
    lateinit var connectionInfoStore: ConnectionInfoStore
        private set

    /**
     * The active [AgentHubConnection] set by [com.signalremote.agent.service.AgentForegroundService]
     * when it starts, and cleared when it stops.
     */
    @Volatile
    var hubConnection: AgentHubConnection? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        connectionInfoStore = ConnectionInfoStore(applicationContext)
        Log.i(TAG, "SignalRemote Agent application started")
    }

    companion object {
        private const val TAG = "AgentApplication"

        @Volatile
        lateinit var instance: AgentApplication
            private set
    }
}
