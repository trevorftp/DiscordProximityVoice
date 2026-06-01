using System;
using Vintagestory.API.MathTools;

namespace DiscordProximityVoice;

internal static class VoiceProximityMath
{
    public static float VolumeForDistance(float distance, float nearRadius, float farRadius)
    {
        if (distance <= nearRadius) return 1f;
        if (distance >= farRadius) return 0f;

        float t = (distance - nearRadius) / (farRadius - nearRadius);
        return GameMath.Clamp(1f - t, 0f, 1f);
    }

    public static float Distance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
