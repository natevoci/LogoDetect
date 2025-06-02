using System;

namespace LogoDetect.Models;

public class LogoDetection
{
    public TimeSpan Time { get; }
    public float LogoDiff { get; }
    public bool HasLogo { get; }

    public LogoDetection(TimeSpan time, float logoDiff, bool hasLogo)
    {
        Time = time;
        LogoDiff = logoDiff;
        HasLogo = hasLogo;
    }
}
