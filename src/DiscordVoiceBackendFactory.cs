using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;

namespace DiscordProximityVoice;

internal static class DiscordVoiceBackendFactory
{
    private static readonly object resolverLock = new object();
    private static bool resolverRegistered = false;
    private static string resolvedBridgePath = "";
    private static string resolvedDiscordPath = "";

    public static IDiscordVoiceBackend Create(ICoreClientAPI api, DiscordVoiceClientConfig config)
    {
        string modFolder = Path.GetDirectoryName(typeof(DiscordVoiceBackendFactory).Assembly.Location) ?? AppContext.BaseDirectory;
        string nativeFolder = Path.Combine(modFolder, "native");
        string bridgePath = Path.Combine(nativeFolder, "DiscordProximityVoice.Native.dll");
        string discordPath = Path.Combine(nativeFolder, "discord_partner_sdk.dll");

        if (File.Exists(bridgePath) && File.Exists(discordPath))
        {
            RegisterResolver(bridgePath, discordPath);
            return new NativeBridgeVoiceBackend(api, config, bridgePath, discordPath);
        }

        return new MissingDiscordVoiceBackend(bridgePath, discordPath);
    }

    private static void RegisterResolver(string bridgePath, string discordPath)
    {
        lock (resolverLock)
        {
            if (resolverRegistered) return;

            resolvedBridgePath = bridgePath;
            resolvedDiscordPath = discordPath;
            NativeLibrary.SetDllImportResolver(typeof(DiscordVoiceBackendFactory).Assembly, ResolveNativeLibrary);
            resolverRegistered = true;
        }
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (IsLibrary(libraryName, "DiscordProximityVoice.Native")) return NativeLibrary.Load(resolvedBridgePath);
        if (IsLibrary(libraryName, "discord_partner_sdk")) return NativeLibrary.Load(resolvedDiscordPath);
        return IntPtr.Zero;
    }

    private static bool IsLibrary(string libraryName, string expectedName)
    {
        return string.Equals(libraryName, expectedName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(libraryName, expectedName + ".dll", StringComparison.OrdinalIgnoreCase);
    }
}
