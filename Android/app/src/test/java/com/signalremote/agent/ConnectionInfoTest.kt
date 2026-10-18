package com.signalremote.agent

import com.signalremote.agent.models.ConnectionInfo
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Unit tests for [ConnectionInfo] model.
 */
class ConnectionInfoTest {

    @Test
    fun `default ConnectionInfo generates a non-blank deviceId`() {
        val info = ConnectionInfo()
        assertTrue(info.deviceId.isNotBlank())
    }

    @Test
    fun `normalizedHost strips trailing slash`() {
        val info = ConnectionInfo(host = "https://example.com/")
        assertEquals("https://example.com", info.normalizedHost)
    }

    @Test
    fun `normalizedHost strips multiple trailing slashes`() {
        val info = ConnectionInfo(host = "https://example.com///")
        assertEquals("https://example.com", info.normalizedHost)
    }

    @Test
    fun `normalizedHost trims surrounding whitespace`() {
        val info = ConnectionInfo(host = "  https://example.com  ")
        assertEquals("https://example.com", info.normalizedHost)
    }

    @Test
    fun `normalizedHost of blank host is blank`() {
        val info = ConnectionInfo(host = "")
        assertEquals("", info.normalizedHost)
    }

    @Test
    fun `copy preserves deviceId when not changed`() {
        val info = ConnectionInfo(deviceId = "my-device-id", host = "https://old.example.com")
        val updated = info.copy(host = "https://new.example.com")
        assertEquals("my-device-id", updated.deviceId)
        assertEquals("https://new.example.com", updated.host)
    }

    @Test
    fun `serverVerificationToken defaults to null`() {
        val info = ConnectionInfo()
        assertEquals(null, info.serverVerificationToken)
    }

    @Test
    fun `two ConnectionInfo instances have different deviceIds by default`() {
        val a = ConnectionInfo()
        val b = ConnectionInfo()
        // UUIDs should be unique
        assertTrue(a.deviceId != b.deviceId)
    }
}
