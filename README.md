# Discord Proximity Voice

Proof of concept for Discord-hosted proximity voice in Vintage Story.

The server creates a Discord provisional access token for each player. The client uses that token to connect to the Discord Social SDK lobby voice call, while the mod keeps proximity state in Vintage Story and sends volume updates to the native bridge.

This is not ready for public servers yet. It is here so the flow can be tested and worked on.

## Current flow

- Server owner configures a Discord application id and bot token in `ModConfig/DiscordProximityVoice.Server.json`.
- The server creates one provisional Discord token per Vintage Story player.
- The client connects to Discord lobby voice with that provisional token.
- Vintage Story sends proximity snapshots to each client.
- The client updates Discord participant volumes from the in-game distances.
- Players can open setup with `/voiceconfig` or `/vc`.
- Push to talk is registered in the Vintage Story controls menu as `Discord voice push to talk`.

## Native files

The Windows POC uses two runtime DLLs in `native/`:

- `DiscordProximityVoice.Native.dll`
- `discord_partner_sdk.dll`

To rebuild the native bridge, download the Discord Social SDK and run:

```powershell
.\native\Setup-DiscordSdk.ps1 -SdkRoot "path\to\discord_social_sdk"
```

The full extracted SDK is ignored by git.

## Build

Build the managed mod from this folder:

```powershell
dotnet build DiscordProximityVoice.csproj
```

Output goes to `bin/Mod/DiscordProximityVoice/`.
