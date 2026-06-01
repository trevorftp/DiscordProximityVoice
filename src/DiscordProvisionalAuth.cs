using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiscordProximityVoice;

internal static class DiscordProvisionalAuth
{
    private const string TokenEndpoint = "https://discord.com/api/v10/partner-sdk/token/bot";
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task<ProvisionalTokenResult> FetchToken(string playerUid, string playerName, string botToken)
    {
        object payload = new
        {
            external_user_id = playerUid,
            preferred_global_name = SanitizeDisplayName(playerName)
        };

        string body = JsonSerializer.Serialize(payload);
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        request.Headers.TryAddWithoutValidation("Authorization", "Bot " + botToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ProvisionalTokenResult.Fail("Discord provisional auth failed: HTTP " + (int)response.StatusCode);
            }

            using JsonDocument doc = JsonDocument.Parse(responseBody);
            JsonElement root = doc.RootElement;
            string accessToken = root.TryGetProperty("access_token", out JsonElement tokenElement) ? tokenElement.GetString() : "";
            int expiresIn = root.TryGetProperty("expires_in", out JsonElement expiresElement) ? expiresElement.GetInt32() : 3600;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ProvisionalTokenResult.Fail("Discord provisional auth failed: missing access_token");
            }

            return ProvisionalTokenResult.Ok(accessToken, expiresIn);
        }
        catch (Exception ex)
        {
            return ProvisionalTokenResult.Fail("Discord provisional auth failed: " + ex.GetType().Name);
        }
    }

    private static string SanitizeDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Vintage Story Player";

        StringBuilder builder = new StringBuilder();
        foreach (char ch in name.Trim())
        {
            if (!char.IsControl(ch)) builder.Append(ch);
            if (builder.Length >= 32) break;
        }

        return builder.Length == 0 ? "Vintage Story Player" : builder.ToString();
    }
}
