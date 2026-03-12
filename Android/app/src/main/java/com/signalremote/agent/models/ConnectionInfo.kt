package com.signalremote.agent.models

import java.util.UUID

/**
 * Mirrors ConnectionInfo from Remotely.Shared.Models.
 * Stores server connection settings persistently.
 */
data class ConnectionInfo(
    val deviceId: String = UUID.randomUUID().toString(),
    val host: String = "",
    val organizationId: String = "",
    val serverVerificationToken: String? = null
) {
    /** Returns the host URL trimmed of trailing slashes. */
    val normalizedHost: String
        get() = host.trim().trimEnd('/')
}
