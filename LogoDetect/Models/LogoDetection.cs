using System;

namespace LogoDetect.Models;

public class LogoDetection
{
    public TimeSpan Time { get; }
    public float LogoDiff { get; }

    public LogoDetection(TimeSpan time, float logoDiff)
    {
        Time = time;
        LogoDiff = logoDiff;
    }
}
