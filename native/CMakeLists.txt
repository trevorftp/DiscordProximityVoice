cmake_minimum_required(VERSION 3.20)
project(DiscordProximityVoiceNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(DISCORD_SDK_ROOT "" CACHE PATH "Path to extracted discord_social_sdk folder")

if(NOT DISCORD_SDK_ROOT)
    message(FATAL_ERROR "Set DISCORD_SDK_ROOT to the extracted discord_social_sdk folder")
endif()

set(DISCORD_SDK_INCLUDE_DIR "${DISCORD_SDK_ROOT}/include")
set(DISCORD_SDK_LIB_PATH "${DISCORD_SDK_ROOT}/lib/release/discord_partner_sdk.lib")

if(NOT EXISTS "${DISCORD_SDK_INCLUDE_DIR}/discordpp.h")
    message(FATAL_ERROR "discordpp.h was not found under ${DISCORD_SDK_INCLUDE_DIR}")
endif()

if(NOT EXISTS "${DISCORD_SDK_LIB_PATH}")
    message(FATAL_ERROR "discord_partner_sdk.lib was not found at ${DISCORD_SDK_LIB_PATH}")
endif()

add_library(DiscordProximityVoice.Native SHARED
    src/DiscordProximityVoice.Native.cpp
    DiscordProximityVoice.Native.h
)

target_include_directories(DiscordProximityVoice.Native PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${DISCORD_SDK_INCLUDE_DIR}"
)

target_link_libraries(DiscordProximityVoice.Native PRIVATE "${DISCORD_SDK_LIB_PATH}")

if(WIN32)
    target_compile_definitions(DiscordProximityVoice.Native PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
endif()