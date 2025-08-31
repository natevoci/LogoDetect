using FFmpeg.AutoGen;

namespace LogoDetect.Services;

public class Frame
{
    YData _yData;
    YData _quarterYData;
    long _timestamp;


    public YData YData => _yData;
    public YData QuarterYData => _quarterYData;

    public long Timestamp => _timestamp;

    public TimeSpan TimeSpan => TimeSpanExtensions.FromTimestamp(_timestamp);

    public Frame(YData yData, YData quarterYData, long timestamp)
    {
        _yData = yData;
        _quarterYData = quarterYData;
        _timestamp = timestamp;
    }
}
