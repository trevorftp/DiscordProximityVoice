using System;
using System.Collections.Generic;

namespace DiscordProximityVoice;

internal sealed class VoiceServerSession
{
    public string PlayerUid = "";
    public string VoiceToken = "";
    public string ProvisionalAccessToken = "";
    public DateTime ProvisionalAccessTokenExpiresUtc = DateTime.MinValue;
    public bool TokenRequestInFlight = false;
    public string TokenError = "";
    public string DiscordUserId = "";
    public bool BackendAvailable = false;
    public bool Linked = false;
    public bool JoinedLobby = false;
    public bool InCall = false;
    public bool Muted = false;
    public bool Deafened = false;
    public string BackendStatus = "";
    public readonly Dictionary<string, float> LastPeerVolumes = new Dictionary<string, float>();
}

internal sealed class ProvisionalTokenResult
{
    public bool Success = false;
    public string AccessToken = "";
    public int ExpiresIn = 0;
    public string Error = "";

    private ProvisionalTokenResult(bool success, string accessToken, int expiresIn, string error)
    {
        Success = success;
        AccessToken = accessToken;
        ExpiresIn = expiresIn;
        Error = error;
    }

    public static ProvisionalTokenResult Ok(string accessToken, int expiresIn)
    {
        return new ProvisionalTokenResult(true, accessToken, expiresIn, "");
    }

    public static ProvisionalTokenResult Fail(string error)
    {
        return new ProvisionalTokenResult(false, "", 0, error);
    }
}
