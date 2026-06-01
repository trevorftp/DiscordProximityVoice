using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace DiscordProximityVoice;

internal sealed class VoiceServer : IDisposable
{
    private const float MinVolumeDelta = 0.05f;

    private readonly ICoreServerAPI api = null;
    private readonly Dictionary<string, VoiceServerSession> sessions = new Dictionary<string, VoiceServerSession>();

    private IServerNetworkChannel channel = null;
    private long tickListenerId = 0;
    private DiscordVoiceServerConfig config = null;

    public VoiceServer(ICoreServerAPI api)
    {
        this.api = api;
    }

    public void Start()
    {
        LoadConfig();

        channel = api.Network.GetChannel(DiscordProximityVoiceModSystem.ChannelName)
            .SetMessageHandler<VoiceHelloPacket>(OnVoiceHello)
            .SetMessageHandler<VoiceStatePacket>(OnVoiceState);

        api.Event.PlayerJoin += OnPlayerJoin;
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        tickListenerId = api.Event.RegisterGameTickListener(OnTick, config.UpdateIntervalMs);

        api.ChatCommands.Create("dpvoice")
            .WithDescription("Discord proximity voice status")
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(OnCommand);

        api.Logger.Notification("[DiscordProximityVoice] server side started, appId={0}, voiceId={1}", config.DiscordApplicationId, config.ServerVoiceId);
    }

    private void LoadConfig()
    {
        const string configName = "DiscordProximityVoice.Server.json";
        config = api.LoadModConfig<DiscordVoiceServerConfig>(configName) ?? new DiscordVoiceServerConfig();
        config.FillDefaults(api.World.Seed);
        api.StoreModConfig(config, configName);
    }

    private TextCommandResult OnCommand(TextCommandCallingArgs args)
    {
        int linked = 0;
        foreach (VoiceServerSession session in sessions.Values)
        {
            if (session.BackendAvailable) linked++;
        }

        string appState = config.DiscordApplicationId == 0 ? "missing app id" : "app id set";
        string authState = string.IsNullOrWhiteSpace(config.DiscordBotToken) ? "missing bot token" : "bot token set";
        return TextCommandResult.Success("DiscordProximityVoice: " + appState + ", " + authState + ", sessions=" + sessions.Count + ", backends=" + linked + ", radius=" + config.FarRadius);
    }

    private void OnPlayerJoin(IServerPlayer player)
    {
        SendSession(player);
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        SendSession(player);
    }

    private void OnVoiceHello(IServerPlayer fromPlayer, VoiceHelloPacket packet)
    {
        if (packet.ProtocolVersion != DiscordProximityVoiceModSystem.ProtocolVersion)
        {
            api.Logger.Warning("[DiscordProximityVoice] protocol mismatch from {0}: {1}", fromPlayer.PlayerName, packet.ProtocolVersion);
            return;
        }

        VoiceServerSession session = GetOrCreateSession(fromPlayer);
        session.DiscordUserId = packet.DiscordUserId ?? "";
        session.BackendAvailable = packet.BackendAvailable;
        session.BackendStatus = packet.BackendStatus ?? "";
    }

    private void OnVoiceState(IServerPlayer fromPlayer, VoiceStatePacket packet)
    {
        VoiceServerSession session = GetOrCreateSession(fromPlayer);
        session.Linked = packet.Linked;
        session.JoinedLobby = packet.JoinedLobby;
        session.InCall = packet.InCall;
        session.Muted = packet.Muted;
        session.Deafened = packet.Deafened;
        session.BackendStatus = packet.BackendStatus ?? "";
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        sessions.Remove(player.PlayerUID);
    }

    private void OnTick(float dt)
    {
        IPlayer[] players = api.World.AllOnlinePlayers;
        if (players == null || players.Length == 0) return;

        foreach (IPlayer player in players)
        {
            if (player is not IServerPlayer serverPlayer || serverPlayer.Entity?.Pos == null) continue;

            if (!sessions.TryGetValue(serverPlayer.PlayerUID, out VoiceServerSession listenerSession) || !listenerSession.BackendAvailable) continue;

            VoiceProximityPacket packet = BuildProximityPacket(serverPlayer, players);
            if (ShouldSendProximity(listenerSession, packet))
            {
                channel.SendPacket(packet, serverPlayer);
                listenerSession.LastPeerVolumes.Clear();
                foreach (VoicePeerPacket peer in packet.Peers ?? Array.Empty<VoicePeerPacket>())
                {
                    listenerSession.LastPeerVolumes[peer.VoiceToken] = peer.Volume;
                }
            }
        }
    }

    private bool ShouldSendProximity(VoiceServerSession session, VoiceProximityPacket packet)
    {
        VoicePeerPacket[] peers = packet.Peers ?? Array.Empty<VoicePeerPacket>();

        if (peers.Length != session.LastPeerVolumes.Count) return true;
        if (peers.Length == 0) return false;

        foreach (VoicePeerPacket peer in peers)
        {
            if (!session.LastPeerVolumes.TryGetValue(peer.VoiceToken, out float lastVolume)) return true;
            if (Math.Abs(lastVolume - peer.Volume) >= MinVolumeDelta) return true;
        }

        return false;
    }

    private VoiceProximityPacket BuildProximityPacket(IServerPlayer listener, Vintagestory.API.Common.IPlayer[] players)
    {
        EntityPos listenerPos = listener.Entity.Pos;
        List<VoicePeerPacket> peers = new List<VoicePeerPacket>();

        foreach (IPlayer player in players)
        {
            if (player.PlayerUID == listener.PlayerUID) continue;
            if (player is not IServerPlayer speaker || speaker.Entity?.Pos == null) continue;

            EntityPos speakerPos = speaker.Entity.Pos;
            float distance = VoiceProximityMath.Distance(listenerPos.X, listenerPos.Y, listenerPos.Z, speakerPos.X, speakerPos.Y, speakerPos.Z);
            bool sameDimension = listenerPos.Dimension == speakerPos.Dimension;
            float volume = sameDimension ? VoiceProximityMath.VolumeForDistance(distance, config.NearRadius, config.FarRadius) : 0f;

            if (volume <= 0f) continue;

            VoiceServerSession session = GetOrCreateSession(speaker);
            peers.Add(new VoicePeerPacket
            {
                PlayerUid = speaker.PlayerUID,
                PlayerName = speaker.PlayerName,
                VoiceToken = session.VoiceToken,
                X = speakerPos.X,
                Y = speakerPos.Y,
                Z = speakerPos.Z,
                Dimension = speakerPos.Dimension,
                Distance = distance,
                Volume = volume
            });
        }

        return new VoiceProximityPacket
        {
            Peers = peers.ToArray(),
            ServerMs = api.World.ElapsedMilliseconds
        };
    }

    private void SendSession(IServerPlayer player)
    {
        VoiceServerSession session = GetOrCreateSession(player);
        VoiceSessionPacket packet = BuildSessionPacket(session);
        channel.SendPacket(packet, player);

        if (ShouldRequestProvisionalToken(session))
        {
            RequestProvisionalToken(player, session);
        }
    }

    private VoiceSessionPacket BuildSessionPacket(VoiceServerSession session)
    {
        return new VoiceSessionPacket
        {
            ProtocolVersion = DiscordProximityVoiceModSystem.ProtocolVersion,
            Enabled = config.Enabled,
            ServerVoiceId = config.ServerVoiceId,
            LobbySecret = config.LobbySecret,
            PlayerVoiceToken = session.VoiceToken,
            NearRadius = config.NearRadius,
            FarRadius = config.FarRadius,
            UpdateIntervalMs = config.UpdateIntervalMs,
            DiscordApplicationId = config.DiscordApplicationId,
            RequireSetup = config.RequireClientSetup,
            DiscordAccessToken = session.ProvisionalAccessToken,
            DiscordAccessTokenExpiresUnixMs = UnixMillisecondsOrZero(session.ProvisionalAccessTokenExpiresUtc),
            AuthStatus = ProvisionalAuthStatus(session)
        };
    }

    private static long UnixMillisecondsOrZero(DateTime dateTime)
    {
        if (dateTime <= DateTime.UnixEpoch) return 0;
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    private string ProvisionalAuthStatus(VoiceServerSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.ProvisionalAccessToken)) return "provisional token ready";
        if (session.TokenRequestInFlight) return "requesting provisional token";
        if (!string.IsNullOrWhiteSpace(session.TokenError)) return session.TokenError;
        if (config.DiscordApplicationId == 0) return "server has no Discord application id configured";
        if (string.IsNullOrWhiteSpace(config.DiscordBotToken)) return "server has no Discord bot token configured";
        return "provisional token not requested";
    }

    private bool ShouldRequestProvisionalToken(VoiceServerSession session)
    {
        if (config.DiscordApplicationId == 0) return false;
        if (string.IsNullOrWhiteSpace(config.DiscordBotToken)) return false;
        if (session.TokenRequestInFlight) return false;
        if (!string.IsNullOrWhiteSpace(session.ProvisionalAccessToken) && session.ProvisionalAccessTokenExpiresUtc > DateTime.UtcNow.AddMinutes(10)) return false;

        return true;
    }

    private void RequestProvisionalToken(IServerPlayer player, VoiceServerSession session)
    {
        session.TokenRequestInFlight = true;
        session.TokenError = "";

        string playerUid = player.PlayerUID;
        string playerName = player.PlayerName;
        string botToken = config.DiscordBotToken;

        _ = Task.Run(async () =>
        {
            ProvisionalTokenResult result = await DiscordProvisionalAuth.FetchToken(playerUid, playerName, botToken);

            api.Event.EnqueueMainThreadTask(() =>
            {
                if (!sessions.TryGetValue(playerUid, out VoiceServerSession activeSession)) return;

                activeSession.TokenRequestInFlight = false;
                if (result.Success)
                {
                    activeSession.ProvisionalAccessToken = result.AccessToken;
                    activeSession.ProvisionalAccessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, result.ExpiresIn));
                    activeSession.TokenError = "";
                    SendSession(player);
                }
                else
                {
                    activeSession.TokenError = result.Error;
                    api.Logger.Warning("[DiscordProximityVoice] provisional token request failed for {0}: {1}", playerName, result.Error);
                    channel.SendPacket(BuildSessionPacket(activeSession), player);
                }
            }, "dpvoice-provisional-token");
        });
    }

    private VoiceServerSession GetOrCreateSession(IServerPlayer player)
    {
        if (!sessions.TryGetValue(player.PlayerUID, out VoiceServerSession session))
        {
            session = new VoiceServerSession
            {
                PlayerUid = player.PlayerUID,
                VoiceToken = Guid.NewGuid().ToString("N")
            };
            sessions[player.PlayerUID] = session;
        }

        return session;
    }

    public void Dispose()
    {
        if (tickListenerId != 0)
        {
            api.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        api.Event.PlayerJoin -= OnPlayerJoin;
        api.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
        api.Event.PlayerDisconnect -= OnPlayerDisconnect;
    }

}
