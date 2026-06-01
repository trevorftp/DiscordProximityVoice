#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define DPV_EXPORT extern "C" __declspec(dllexport)
#else
#define DPV_EXPORT extern "C"
#endif

typedef void(__cdecl* dpv_log_callback)(const char* message);

DPV_EXPORT int32_t __cdecl dpv_init(uint64_t application_id, const char* voice_token, const char* discord_access_token, dpv_log_callback log_callback);
DPV_EXPORT void __cdecl dpv_tick();
DPV_EXPORT int32_t __cdecl dpv_connect_lobby(const char* lobby_secret);
DPV_EXPORT int32_t __cdecl dpv_start_call();
DPV_EXPORT void __cdecl dpv_set_audio_mode(int32_t mode);
DPV_EXPORT void __cdecl dpv_set_ptt_active(bool active);
DPV_EXPORT void __cdecl dpv_set_input_device(const char* device_id);
DPV_EXPORT void __cdecl dpv_refresh_input_devices();
DPV_EXPORT int32_t __cdecl dpv_get_input_device_count();
DPV_EXPORT const char* __cdecl dpv_get_input_device_id(int32_t index);
DPV_EXPORT const char* __cdecl dpv_get_input_device_name(int32_t index);
DPV_EXPORT float __cdecl dpv_get_input_level();
DPV_EXPORT int32_t __cdecl dpv_set_peer_volume(const char* voice_token, float volume);
DPV_EXPORT void __cdecl dpv_disconnect();
DPV_EXPORT void __cdecl dpv_shutdown();
DPV_EXPORT const char* __cdecl dpv_get_discord_user_id();
