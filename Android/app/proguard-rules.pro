# Add project specific ProGuard rules here.
# You can control the set of applied configuration files using the
# proguardFiles setting in build.gradle.

# Keep SignalR classes
-keep class com.microsoft.signalr.** { *; }
-dontwarn com.microsoft.signalr.**

# Keep Gson model classes
-keep class com.signalremote.agent.models.** { *; }

# Keep OkHttp (used by SignalR)
-dontwarn okhttp3.**
-dontwarn okio.**

# Netty (used internally by SignalR)
-dontwarn io.netty.**
-dontwarn reactor.**
