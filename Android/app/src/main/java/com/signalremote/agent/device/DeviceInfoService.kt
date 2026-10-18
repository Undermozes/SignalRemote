package com.signalremote.agent.device

import android.annotation.SuppressLint
import android.app.ActivityManager
import android.content.Context
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Environment
import android.os.StatFs
import android.os.SystemClock
import com.signalremote.agent.models.DeviceClientDto
import com.signalremote.agent.models.DriveInfo
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.File
import java.io.InputStreamReader
import java.net.NetworkInterface

/**
 * Gathers device metrics and builds a [DeviceClientDto] for registration
 * with the SignalRemote server.
 */
class DeviceInfoService(private val context: Context) {

    /**
     * Builds a complete [DeviceClientDto] populated with current device metrics.
     *
     * @param deviceId  the persistent unique ID stored in [ConnectionInfoStore]
     * @param orgId     the organisation ID from [ConnectionInfoStore]
     */
    suspend fun createDevice(deviceId: String, orgId: String): DeviceClientDto =
        withContext(Dispatchers.IO) {
            DeviceClientDto(
                agentVersion = AGENT_VERSION,
                cpuUtilization = getCpuUtilization(),
                currentUser = Build.USER,
                deviceName = Build.MODEL,
                drives = getStorageInfo(),
                id = deviceId,
                is64Bit = Build.SUPPORTED_64_BIT_ABIS.isNotEmpty(),
                isOnline = true,
                macAddresses = getMacAddresses(),
                organizationId = orgId,
                osArchitecture = getOsArchitecture(),
                osDescription = "Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})",
                platform = "Android",
                processorCount = Runtime.getRuntime().availableProcessors(),
                totalMemory = getTotalMemoryGb(),
                totalStorage = getTotalStorageGb(),
                usedMemory = getUsedMemoryGb(),
                usedStorage = getUsedStorageGb()
            )
        }

    // ── Memory ──────────────────────────────────────────────────────────────

    private fun getTotalMemoryGb(): Double {
        val am = context.getSystemService(Context.ACTIVITY_SERVICE) as ActivityManager
        val info = ActivityManager.MemoryInfo()
        am.getMemoryInfo(info)
        return info.totalMem.toDouble() / GB
    }

    private fun getUsedMemoryGb(): Double {
        val am = context.getSystemService(Context.ACTIVITY_SERVICE) as ActivityManager
        val info = ActivityManager.MemoryInfo()
        am.getMemoryInfo(info)
        return (info.totalMem - info.availMem).toDouble() / GB
    }

    // ── Storage ─────────────────────────────────────────────────────────────

    private fun getTotalStorageGb(): Double {
        val stat = StatFs(Environment.getDataDirectory().path)
        return stat.blockCountLong * stat.blockSizeLong.toDouble() / GB
    }

    private fun getUsedStorageGb(): Double {
        val stat = StatFs(Environment.getDataDirectory().path)
        val total = stat.blockCountLong * stat.blockSizeLong.toDouble()
        val free = stat.availableBlocksLong * stat.blockSizeLong.toDouble()
        return (total - free) / GB
    }

    private fun getStorageInfo(): List<DriveInfo> {
        val stat = StatFs(Environment.getDataDirectory().path)
        val total = stat.blockCountLong * stat.blockSizeLong.toDouble() / GB
        val free = stat.availableBlocksLong * stat.blockSizeLong.toDouble() / GB
        return listOf(
            DriveInfo(
                driveFormat = "FUSE",
                driveType = "Fixed",
                freeSpace = free,
                name = "/data",
                rootDirectory = "/data",
                totalSize = total,
                volumeLabel = "Internal Storage"
            )
        )
    }

    // ── CPU ──────────────────────────────────────────────────────────────────

    private fun getCpuUtilization(): Double {
        return try {
            val process = Runtime.getRuntime().exec("top -n 1 -d 1")
            val reader = BufferedReader(InputStreamReader(process.inputStream))
            var cpuLine = reader.readLine()
            // Skip header lines to find the CPU line
            repeat(3) { cpuLine = reader.readLine() }
            process.destroy()

            // Parse "CPU: xx% user" style output
            cpuLine
                ?.split(" ")
                ?.firstOrNull { it.endsWith("%") }
                ?.trimEnd('%')
                ?.toDoubleOrNull()
                ?.div(100.0)
                ?: 0.0
        } catch (_: Exception) {
            0.0
        }
    }

    // ── Architecture ─────────────────────────────────────────────────────────

    private fun getOsArchitecture(): Int {
        val abis = Build.SUPPORTED_ABIS.firstOrNull() ?: ""
        return when {
            abis.contains("arm64") -> ARM64
            abis.contains("arm") -> ARM
            abis.contains("x86_64") -> X64
            abis.contains("x86") -> X86
            else -> ARM64
        }
    }

    // ── Network ──────────────────────────────────────────────────────────────

    @SuppressLint("HardwareIds")
    private fun getMacAddresses(): List<String> {
        return try {
            NetworkInterface.getNetworkInterfaces()
                ?.toList()
                ?.filter { it.hardwareAddress != null }
                ?.map { iface ->
                    iface.hardwareAddress.joinToString(":") { "%02X".format(it) }
                }
                ?.filter { it.isNotBlank() && it != "00:00:00:00:00:00" }
                ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }

    companion object {
        const val AGENT_VERSION = "1.0.0"

        private const val GB = 1_073_741_824.0  // 1 GiB in bytes

        // Architecture constants mirror System.Runtime.InteropServices.Architecture
        private const val X86 = 0
        private const val ARM = 5
        private const val ARM64 = 6
        private const val X64 = 9
    }
}
