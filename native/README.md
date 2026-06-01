# Native bridge

This folder holds the Windows native bridge for the Discord Social SDK POC.

Runtime files used by the managed mod:

- `DiscordProximityVoice.Native.dll`
- `discord_partner_sdk.dll`

The bridge receives a provisional Discord access token from the managed client, connects to the SDK, joins the lobby voice call, publishes the player voice token as lobby member metadata, and applies per-peer volume updates from the Vintage Story client.

To rebuild it, download the Discord Social SDK and run:

```powershell
.\native\Setup-DiscordSdk.ps1 -SdkRoot "path\to\discord_social_sdk"
```

The extracted SDK and CMake build folder are local files and are not checked in.
