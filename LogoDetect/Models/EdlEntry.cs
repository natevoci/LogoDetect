namespace LogoDetect.Models;

public class EdlEntry
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Description { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{FormatTimecode(StartTime)} {FormatTimecode(EndTime)} C";
    }

    private static string FormatTimecode(TimeSpan time)
    {
        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}:{(time.Milliseconds / (1000.0 / 30.0)):D2}";
    }
}
