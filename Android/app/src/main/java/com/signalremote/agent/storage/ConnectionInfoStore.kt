package com.signalremote.agent.storage

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.google.gson.Gson
import com.signalremote.agent.models.ConnectionInfo

/**
 * Persists [ConnectionInfo] in encrypted SharedPreferences so that the device ID,
 * server URL, organisation ID, and verification token survive app restarts.
 */
class ConnectionInfoStore(context: Context) {

    private val gson = Gson()

    private val prefs by lazy {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            context,
            PREFS_FILE_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    /** Returns the currently saved [ConnectionInfo], or a fresh default if none exists. */
    fun load(): ConnectionInfo {
        val json = prefs.getString(KEY_CONNECTION_INFO, null)
            ?: return ConnectionInfo()
        return try {
            gson.fromJson(json, ConnectionInfo::class.java)
        } catch (_: Exception) {
            ConnectionInfo()
        }
    }

    /** Persists the given [ConnectionInfo]. */
    fun save(info: ConnectionInfo) {
        prefs.edit()
            .putString(KEY_CONNECTION_INFO, gson.toJson(info))
            .apply()
    }

    /** Updates only the server verification token, keeping all other fields intact. */
    fun updateVerificationToken(token: String?) {
        val current = load()
        save(current.copy(serverVerificationToken = token))
    }

    companion object {
        private const val PREFS_FILE_NAME = "signal_remote_agent_prefs"
        private const val KEY_CONNECTION_INFO = "connection_info"
    }
}
