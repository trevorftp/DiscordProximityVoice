using System;
using ProtoBuf;

namespace DiscordProximityVoice;

[ProtoContract]
public sealed class VoiceSessionPacket
{
    [ProtoMember(1)] public int ProtocolVersion = 0;
    [ProtoMember(2)] public bool Enabled = false;
    [ProtoMember(3)] public string ServerVoiceId = "";
    [ProtoMember(4)] public string LobbySecret = "";
    [ProtoMember(5)] public string PlayerVoiceToken = "";
    [ProtoMember(6)] public float NearRadius = 0f;
    [ProtoMember(7)] public float FarRadius = 0f;
    [ProtoMember(8)] public int UpdateIntervalMs = 0;
    [ProtoMember(9)] public ulong DiscordApplicationId = 0;
    [ProtoMember(10)] public bool RequireSetup = false;
    [ProtoMember(11)] public string DiscordAccessToken = "";
    [ProtoMember(12)] public long DiscordAccessTokenExpiresUnixMs = 0;
    [ProtoMember(13)] public string AuthStatus = "";
}

[ProtoContract]
public sealed class VoiceHelloPacket
{
    [ProtoMember(1)] public int ProtocolVersion = 0;
    [ProtoMember(2)] public string DiscordUserId = "";
    [ProtoMember(3)] public bool BackendAvailable = false;
    [ProtoMember(4)] public string BackendStatus = "";
}

[ProtoContract]
public sealed class VoiceStatePacket
{
    [ProtoMember(1)] public bool Linked = false;
    [ProtoMember(2)] public bool JoinedLobby = false;
    [ProtoMember(3)] public bool InCall = false;
    [ProtoMember(4)] public bool Muted = false;
    [ProtoMember(5)] public bool Deafened = false;
    [ProtoMember(6)] public string BackendStatus = "";
}

[ProtoContract]
public sealed class VoiceProximityPacket
{
    [ProtoMember(1)] public VoicePeerPacket[] Peers = Array.Empty<VoicePeerPacket>();
    [ProtoMember(2)] public long ServerMs = 0;
}

[ProtoContract]
public sealed class VoicePeerPacket
{
    [ProtoMember(1)] public string PlayerUid = "";
    [ProtoMember(2)] public string PlayerName = "";
    [ProtoMember(3)] public string VoiceToken = "";
    [ProtoMember(4)] public double X = 0;
    [ProtoMember(5)] public double Y = 0;
    [ProtoMember(6)] public double Z = 0;
    [ProtoMember(7)] public int Dimension = 0;
    [ProtoMember(8)] public float Distance = 0f;
    [ProtoMember(9)] public float Volume = 0f;
}
