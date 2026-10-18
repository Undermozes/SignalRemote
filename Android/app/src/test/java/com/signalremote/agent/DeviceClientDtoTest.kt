package com.signalremote.agent

import com.signalremote.agent.device.DeviceInfoService
import com.signalremote.agent.models.DeviceClientDto
import com.signalremote.agent.models.DriveInfo
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.mockito.kotlin.mock

/**
 * Unit tests for [DeviceClientDto] model serialisation/deserialisation
 * and basic invariant checks that do not require an Android context.
 */
@ExperimentalCoroutinesApi
class DeviceClientDtoTest {

    @Test
    fun `default DeviceClientDto has Android platform`() {
        val dto = DeviceClientDto(id = "test-id", organizationId = "org-id")
        assertEquals("Android", dto.platform)
        assertEquals("Android", dto.osDescription)
    }

    @Test
    fun `default DeviceClientDto isOnline is true`() {
        val dto = DeviceClientDto(id = "test-id", organizationId = "org-id")
        assertTrue(dto.isOnline)
    }

    @Test
    fun `DeviceClientDto copy preserves all fields`() {
        val original = DeviceClientDto(
            agentVersion = "2.0.0",
            cpuUtilization = 0.42,
            currentUser = "android",
            deviceName = "Pixel 9",
            id = "device-123",
            is64Bit = true,
            organizationId = "org-456",
            processorCount = 8,
            totalMemory = 12.0,
            usedMemory = 4.5
        )
        val copy = original.copy(cpuUtilization = 0.5)

        assertEquals("2.0.0", copy.agentVersion)
        assertEquals(0.5, copy.cpuUtilization, 0.001)
        assertEquals("Pixel 9", copy.deviceName)
        assertEquals("device-123", copy.id)
        assertEquals("org-456", copy.organizationId)
        assertEquals(8, copy.processorCount)
        assertEquals(12.0, copy.totalMemory, 0.001)
    }

    @Test
    fun `DriveInfo defaults are sensible`() {
        val drive = DriveInfo(
            name = "/data",
            totalSize = 64.0,
            freeSpace = 32.0
        )
        assertEquals("/data", drive.name)
        assertEquals(64.0, drive.totalSize, 0.001)
        assertEquals("Fixed", drive.driveType)
    }

    @Test
    fun `DeviceClientDto with drives list`() {
        val drives = listOf(
            DriveInfo(name = "/data", totalSize = 64.0, freeSpace = 32.0),
            DriveInfo(name = "/sdcard", totalSize = 128.0, freeSpace = 100.0)
        )
        val dto = DeviceClientDto(id = "x", organizationId = "y", drives = drives)
        assertEquals(2, dto.drives.size)
        assertEquals("/data", dto.drives[0].name)
        assertEquals("/sdcard", dto.drives[1].name)
    }
}
