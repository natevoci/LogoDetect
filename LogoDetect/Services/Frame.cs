using FFmpeg.AutoGen;

namespace LogoDetect.Services;

public class Frame
{
    YData _yData;
    long _timestamp;


    public YData YData => _yData;

    public long Timestamp => _timestamp;

    public Frame(YData yData, long timestamp)
    {
        _yData = yData;
        _timestamp = timestamp;
    }
}
