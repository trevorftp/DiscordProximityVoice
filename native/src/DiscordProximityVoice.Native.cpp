#define DISCORDPP_IMPLEMENTATION
#include "DiscordProximityVoice.Native.h"

#include "discordpp.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

namespace {
std::recursive_mutex stateMutex;
std::shared_ptr<discordpp::Client> client;
std::optional<discordpp::Call> call;
std::unordered_map<std::string, uint64_t> userIdByVoiceToken;
dpv_log_callback logCallback = nullptr;
uint64_t applicationId = 0;
uint64_t lobbyId = 0;
uint64_t currentUserId = 0;
std::string currentUserIdText;
std::string scratchText;
std::string voiceToken;
std::string provisionalAccessToken;
std::string pendingLobbySecret;
std::string pendingInputDeviceId;
std::vector<std::pair<std::string, std::string>> inputDevices;
bool initialized = false;
bool ready = false;
bool lobbyJoinInFlight = false;
bool inputDeviceRefreshInFlight = false;
bool lobbyJoined = false;
bool callRequested = false;
bool callStarted = false;
bool pttActive = false;
float inputLevel = 0.0f;
discordpp::AudioModeType audioMode = discordpp::AudioModeType::MODE_PTT;

template <typename T>
std::string ErrorText(const T& result)
{
    std::ostringstream stream;
    stream << result.Error();
    return stream.str();
}

void Log(const std::string& message)
{
    if (logCallback != nullptr)
    {
        logCallback(message.c_str());
    }
}

void RefreshCurrentUserLocked()
{
    if (!client) return;

    auto user = client->GetCurrentUserV2();
    if (!user || !(*user)) return;

    currentUserId = user->Id();
    currentUserIdText = std::to_string(currentUserId);
}

void RefreshLobbyMembersLocked()
{
    if (!client || !lobbyJoined || lobbyId == 0) return;

    auto lobby = client->GetLobbyHandle(lobbyId);
    if (!lobby || !(*lobby)) return;

    userIdByVoiceToken.clear();
    for (const auto& member : lobby->LobbyMembers())
    {
        auto metadata = member.Metadata();
        auto token = metadata.find("dpv_token");
        if (token != metadata.end() && !token->second.empty())
        {
            userIdByVoiceToken[token->second] = member.Id();
        }
    }
}

void RefreshInputDevicesLocked()
{
    if (!client || !ready || inputDeviceRefreshInFlight) return;

    inputDeviceRefreshInFlight = true;
    client->GetInputDevices([](std::vector<discordpp::AudioDevice> devices) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        inputDeviceRefreshInFlight = false;
        inputDevices.clear();

        for (const auto& device : devices)
        {
            std::string id = device.Id();
            std::string name = device.Name();
            if (id.empty() || name.empty()) continue;
            if (device.IsDefault()) name += " (default)";
            inputDevices.emplace_back(id, name);
        }
    });
}

void ApplyAudioSettingsLocked()
{
    if (client && ready && !pendingInputDeviceId.empty())
    {
        std::string deviceId = pendingInputDeviceId;
        client->SetInputDevice(deviceId, [](discordpp::ClientResult result) {
            if (!result.Successful()) Log("input device change failed: " + ErrorText(result));
        });
    }

    if (!callStarted || !call) return;

    call->SetAudioMode(audioMode);
    call->SetPTTReleaseDelay(20);
    call->SetPTTActive(audioMode == discordpp::AudioModeType::MODE_PTT ? pttActive : true);
}

void UpdateInputLevelLocked(int16_t* data, uint64_t samplesPerChannel, uint64_t channels)
{
    uint64_t sampleCount = samplesPerChannel * channels;
    if (data == nullptr || sampleCount == 0)
    {
        inputLevel *= 0.85f;
        return;
    }

    double sum = 0.0;
    for (uint64_t i = 0; i < sampleCount; i++)
    {
        double sample = static_cast<double>(data[i]) / 32768.0;
        sum += sample * sample;
    }

    float rms = static_cast<float>(std::sqrt(sum / static_cast<double>(sampleCount)));
    inputLevel = std::max(rms, inputLevel * 0.82f);
}

void StartCallLocked()
{
    if (!client || !lobbyJoined || callStarted || lobbyId == 0) return;

    auto startedCall = client->StartCallWithAudioCallbacks(
        lobbyId,
        [](uint64_t, int16_t*, uint64_t, int32_t, uint64_t, bool& outShouldMute) {
            outShouldMute = false;
        },
        [](int16_t* data, uint64_t samplesPerChannel, int32_t, uint64_t channels) {
            std::lock_guard<std::recursive_mutex> lock(stateMutex);
            UpdateInputLevelLocked(data, samplesPerChannel, channels);
        });
    if (startedCall)
    {
        call = startedCall;
    }
    else
    {
        auto existingCall = client->GetCall(lobbyId);
        if (!existingCall) return;
        call = existingCall;
    }

    callStarted = true;
    ApplyAudioSettingsLocked();
    call->SetParticipantChangedCallback([](uint64_t, bool) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        RefreshLobbyMembersLocked();
    });
    call->SetStatusChangedCallback([](discordpp::Call::Status status, discordpp::Call::Error error, int32_t errorDetail) {
        std::ostringstream stream;
        stream << "call status " << discordpp::Call::StatusToString(status);
        if (error != discordpp::Call::Error::None)
        {
            stream << ", error " << discordpp::Call::ErrorToString(error) << " detail " << errorDetail;
        }
        Log(stream.str());
    });
    Log("voice call start requested");
}

void JoinLobbyLocked()
{
    if (!client || !ready || lobbyJoinInFlight || lobbyJoined || pendingLobbySecret.empty()) return;

    lobbyJoinInFlight = true;
    std::unordered_map<std::string, std::string> lobbyMetadata;
    std::unordered_map<std::string, std::string> memberMetadata;
    lobbyMetadata["kind"] = "vintagestory-dpv";
    memberMetadata["dpv_token"] = voiceToken;

    client->CreateOrJoinLobbyWithMetadata(
        pendingLobbySecret,
        lobbyMetadata,
        memberMetadata,
        [](discordpp::ClientResult result, uint64_t joinedLobbyId) {
            std::lock_guard<std::recursive_mutex> lock(stateMutex);
            lobbyJoinInFlight = false;

            if (!result.Successful())
            {
                Log("lobby join failed: " + ErrorText(result));
                return;
            }

            lobbyId = joinedLobbyId;
            lobbyJoined = true;
            Log("lobby joined");
            RefreshLobbyMembersLocked();
            if (callRequested) StartCallLocked();
        }
    );
}

void ConnectWithAccessTokenLocked()
{
    std::shared_ptr<discordpp::Client> activeClient = client;
    std::string accessToken = provisionalAccessToken;
    if (!activeClient || accessToken.empty()) return;

    activeClient->UpdateToken(discordpp::AuthorizationTokenType::Bearer, accessToken, [activeClient](discordpp::ClientResult updateResult) {
        if (!updateResult.Successful())
        {
            Log("token update failed: " + ErrorText(updateResult));
            return;
        }

        activeClient->Connect();
        Log("discord connect requested");
    });
}
}

DPV_EXPORT int32_t __cdecl dpv_init(uint64_t requestedApplicationId, const char* requestedVoiceToken, const char* requestedDiscordAccessToken, dpv_log_callback requestedLogCallback)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (requestedApplicationId == 0) return 1;
    if (requestedDiscordAccessToken == nullptr || requestedDiscordAccessToken[0] == '\0') return 2;

    if (initialized && applicationId == requestedApplicationId) return 0;

    logCallback = requestedLogCallback;
    applicationId = requestedApplicationId;
    voiceToken = requestedVoiceToken == nullptr ? "" : requestedVoiceToken;
    provisionalAccessToken = requestedDiscordAccessToken;
    currentUserId = 0;
    currentUserIdText.clear();
    userIdByVoiceToken.clear();
    inputDevices.clear();
    pendingLobbySecret.clear();
    pendingInputDeviceId.clear();
    lobbyId = 0;
    ready = false;
    lobbyJoinInFlight = false;
    inputDeviceRefreshInFlight = false;
    lobbyJoined = false;
    callRequested = false;
    callStarted = false;
    pttActive = false;
    inputLevel = 0.0f;
    audioMode = discordpp::AudioModeType::MODE_PTT;
    call.reset();

    client = std::make_shared<discordpp::Client>();
    client->SetApplicationId(applicationId);
    client->AddLogCallback([](std::string message, discordpp::LoggingSeverity) { Log(message); }, discordpp::LoggingSeverity::Info);
    client->AddVoiceLogCallback([](std::string message, discordpp::LoggingSeverity) { Log("voice: " + message); }, discordpp::LoggingSeverity::Warning);
    client->SetStatusChangedCallback([](discordpp::Client::Status status, discordpp::Client::Error error, int32_t errorDetail) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        std::ostringstream stream;
        stream << "client status " << discordpp::Client::StatusToString(status);
        if (error != discordpp::Client::Error::None)
        {
            stream << ", error " << discordpp::Client::ErrorToString(error) << " detail " << errorDetail;
        }
        Log(stream.str());

        ready = status == discordpp::Client::Status::Ready;
        if (ready)
        {
            RefreshCurrentUserLocked();
            RefreshInputDevicesLocked();
            JoinLobbyLocked();
        }
    });
    client->SetLobbyMemberAddedCallback([](uint64_t, uint64_t) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        RefreshLobbyMembersLocked();
    });
    client->SetLobbyMemberUpdatedCallback([](uint64_t, uint64_t) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        RefreshLobbyMembersLocked();
    });
    client->SetLobbyMemberRemovedCallback([](uint64_t, uint64_t) {
        std::lock_guard<std::recursive_mutex> lock(stateMutex);
        RefreshLobbyMembersLocked();
    });

    initialized = true;
    ConnectWithAccessTokenLocked();
    Log("provisional authorization requested");
    return 0;
}

DPV_EXPORT void __cdecl dpv_tick()
{
    discordpp::RunCallbacks();

    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (ready)
    {
        RefreshCurrentUserLocked();
        RefreshLobbyMembersLocked();
        RefreshInputDevicesLocked();
        JoinLobbyLocked();
        if (callRequested) StartCallLocked();
        inputLevel *= 0.96f;
    }
}

DPV_EXPORT int32_t __cdecl dpv_connect_lobby(const char* lobbySecret)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (!initialized || !client) return 1;

    pendingLobbySecret = lobbySecret == nullptr ? "" : lobbySecret;
    if (pendingLobbySecret.empty()) return 2;

    JoinLobbyLocked();
    return 0;
}

DPV_EXPORT int32_t __cdecl dpv_start_call()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (!initialized || !client) return 1;

    callRequested = true;
    StartCallLocked();
    return 0;
}

DPV_EXPORT void __cdecl dpv_set_audio_mode(int32_t mode)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    audioMode = mode == 1 ? discordpp::AudioModeType::MODE_VAD : discordpp::AudioModeType::MODE_PTT;
    ApplyAudioSettingsLocked();
}

DPV_EXPORT void __cdecl dpv_set_ptt_active(bool active)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    pttActive = active;
    ApplyAudioSettingsLocked();
}

DPV_EXPORT void __cdecl dpv_set_input_device(const char* deviceId)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    pendingInputDeviceId = deviceId == nullptr ? "" : deviceId;
    ApplyAudioSettingsLocked();
}

DPV_EXPORT void __cdecl dpv_refresh_input_devices()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    RefreshInputDevicesLocked();
}

DPV_EXPORT int32_t __cdecl dpv_get_input_device_count()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    return static_cast<int32_t>(inputDevices.size());
}

DPV_EXPORT const char* __cdecl dpv_get_input_device_id(int32_t index)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (index < 0 || static_cast<size_t>(index) >= inputDevices.size()) return "";

    scratchText = inputDevices[static_cast<size_t>(index)].first;
    return scratchText.c_str();
}

DPV_EXPORT const char* __cdecl dpv_get_input_device_name(int32_t index)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (index < 0 || static_cast<size_t>(index) >= inputDevices.size()) return "";

    scratchText = inputDevices[static_cast<size_t>(index)].second;
    return scratchText.c_str();
}

DPV_EXPORT float __cdecl dpv_get_input_level()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    return std::clamp(inputLevel, 0.0f, 1.0f);
}

DPV_EXPORT int32_t __cdecl dpv_set_peer_volume(const char* requestedVoiceToken, float volume)
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (!callStarted || !call) return 1;

    std::string token = requestedVoiceToken == nullptr ? "" : requestedVoiceToken;
    if (token.empty()) return 2;

    RefreshLobbyMembersLocked();
    auto user = userIdByVoiceToken.find(token);
    if (user == userIdByVoiceToken.end()) return 3;
    if (user->second == currentUserId) return 0;

    float discordVolume = std::clamp(volume * 100.0f, 0.0f, 200.0f);
    call->SetParticipantVolume(user->second, discordVolume);
    return 0;
}

DPV_EXPORT void __cdecl dpv_disconnect()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    if (!client) return;

    if (lobbyId != 0)
    {
        client->EndCall(lobbyId, []() {});
        client->LeaveLobby(lobbyId, [](discordpp::ClientResult) {});
    }

    call.reset();
    userIdByVoiceToken.clear();
    lobbyId = 0;
    lobbyJoined = false;
    lobbyJoinInFlight = false;
    callRequested = false;
    callStarted = false;
    pttActive = false;
    inputLevel = 0.0f;
}

DPV_EXPORT void __cdecl dpv_shutdown()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    dpv_disconnect();

    if (client)
    {
        client->Disconnect();
    }

    client.reset();
    initialized = false;
    ready = false;
    applicationId = 0;
    currentUserId = 0;
    currentUserIdText.clear();
    voiceToken.clear();
    provisionalAccessToken.clear();
    pendingLobbySecret.clear();
    logCallback = nullptr;
}

DPV_EXPORT const char* __cdecl dpv_get_discord_user_id()
{
    std::lock_guard<std::recursive_mutex> lock(stateMutex);
    RefreshCurrentUserLocked();
    return currentUserIdText.c_str();
}
