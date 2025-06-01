namespace LogoDetect.Models;

public class VideoSegment
{
    public TimeSpan Start { get; }
    public TimeSpan End { get; }

    public VideoSegment(TimeSpan start, TimeSpan end)
    {
        Start = start;
        End = end;
    }

    public override string ToString()
    {
        return $"{FormatTimestamp(Start)},{FormatTimestamp(End)}";
    }

    private string FormatTimestamp(TimeSpan ts)
    {
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
