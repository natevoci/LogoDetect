using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LogoDetect.Services;

public class CsvGenerator
{
    public IEnumerable<(TimeSpan Start, TimeSpan End)> GenerateSegments(
        IEnumerable<(TimeSpan Time, bool HasLogo)> keyframes,
        TimeSpan minDuration,
        IEnumerable<TimeSpan> sceneChanges)
    {
        var frames = keyframes.OrderBy(f => f.Time).ToList();
        var changes = sceneChanges.OrderBy(t => t).ToList();
        var segments = new List<(TimeSpan Start, TimeSpan End)>();

        // Find continuous segments where logo is present
        TimeSpan? segmentStart = null;
        bool lastHadLogo = false;

        foreach (var frame in frames)
        {
            if (frame.HasLogo != lastHadLogo)
            {
                if (frame.HasLogo)
                {
                    // Logo appeared - start of new segment
                    segmentStart = frame.Time;
                }
                else if (segmentStart.HasValue)
                {
                    // Logo disappeared - end of segment
                    var duration = frame.Time - segmentStart.Value;
                    if (duration >= minDuration)
                    {
                        segments.Add((segmentStart.Value, frame.Time));
                    }
                    segmentStart = null;
                }
                lastHadLogo = frame.HasLogo;
            }
        }

        // Handle case where video ends with logo present
        if (segmentStart.HasValue && lastHadLogo)
        {
            var duration = frames.Last().Time - segmentStart.Value;
            if (duration >= minDuration)
            {
                segments.Add((segmentStart.Value, frames.Last().Time));
            }
        }

        // Adjust segment boundaries to scene changes
        return segments.Select(segment =>
        {
            var nearestStartChange = changes
                .Where(t => t <= segment.Start)
                .DefaultIfEmpty(segment.Start)
                .Max();

            var nearestEndChange = changes
                .Where(t => t >= segment.End)
                .DefaultIfEmpty(segment.End)
                .Min();

            return (nearestStartChange, nearestEndChange);
        });
    }

    public void WriteCsvFile(string path, IEnumerable<(TimeSpan Start, TimeSpan End)> segments)
    {
        File.WriteAllLines(path, segments.Select(s => 
            $"{FormatTimestamp(s.Start)},{FormatTimestamp(s.End)}"));
    }

    private string FormatTimestamp(TimeSpan ts)
    {
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
