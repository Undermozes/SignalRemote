package com.signalremote.agent.models

/**
 * Mirrors DeviceClientDto from Remotely.Shared.Dtos.
 * Sent to the server hub when the device comes online and on each heartbeat.
 */
data class DeviceClientDto(
    val agentVersion: String = "1.0.0",
    val cpuUtilization: Double = 0.0,
    val currentUser: String = "",
    val deviceName: String = "",
    val drives: List<DriveInfo> = emptyList(),
    val id: String = "",
    val is64Bit: Boolean = true,
    val isOnline: Boolean = true,
    val macAddresses: List<String> = emptyList(),
    val organizationId: String = "",
    /** Architecture integer: 0 = X86, 5 = Arm, 6 = Arm64, 9 = X64 */
    val osArchitecture: Int = 5,
    val osDescription: String = "Android",
    val platform: String = "Android",
    val processorCount: Int = 1,
    val publicIp: String = "",
    val totalMemory: Double = 0.0,
    val totalStorage: Double = 0.0,
    val usedMemory: Double = 0.0,
    val usedStorage: Double = 0.0
)

/**
 * Mirrors Remotely.Shared.Models.Drive.
 */
data class DriveInfo(
    val driveFormat: String = "",
    val driveType: String = "Fixed",
    val freeSpace: Double = 0.0,
    val name: String = "",
    val rootDirectory: String = "",
    val totalSize: Double = 0.0,
    val volumeLabel: String = ""
)
