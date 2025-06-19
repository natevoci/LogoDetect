using System.Runtime.InteropServices;
using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using SkiaSharp;
using ScottPlot;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogoDetect.Services;

public class VideoProcessorSettings
{
    public required string outputPath;
    public double logoThreshold;
    public double sceneThreshold;
    public double blankThreshold;
    public TimeSpan minDuration;
    public string? sceneChangesPath = null;
    public bool forceReload;
}

public class VideoProcessor : IDisposable
{
    private readonly ImageProcessor _imageProcessor;
    private readonly MediaFile _mediaFile;

    public VideoProcessor(string inputPath)
    {
        _imageProcessor = new ImageProcessor();
        _mediaFile = new MediaFile(inputPath);
    }

    public async Task ProcessVideo(VideoProcessorSettings settings)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var logoDetectionProcessor = new LogoDetectionFrameProcessor(settings, _mediaFile, _imageProcessor);
        var sceneChangeProcessor = new SceneChangeFrameProcessor(settings, _mediaFile, _imageProcessor);

        await ProcessFrames(
            [
                logoDetectionProcessor,
                sceneChangeProcessor
            ],
            new Progress()
        );

        stopwatch.Stop();
        Console.WriteLine($"\nFrames processed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        stopwatch.Restart();
        var segments = new List<LogoDetect.Models.VideoSegment>();
        segments.AddRange(GenerateSegments(logoDetectionProcessor.Detections, settings.logoThreshold, settings.minDuration));
        File.WriteAllLines(Path.ChangeExtension(settings.outputPath, "-edge.csv"), segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"Segments generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Write segments to CSV file
        stopwatch.Restart();
        File.WriteAllLines(settings.outputPath, segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"CSV file written in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {settings.outputPath}");
    }

    private async Task ProcessFrames(IEnumerable<IFrameProcessor> processors, IProgressMsg? progress = null)
    {
        var duration = _mediaFile.GetDuration();

        foreach (var processor in processors)
        {
            processor.Initialize(progress);
        }

        Frame? previous = null;
        await foreach (var frame in GetFramesToAnalyze(false))
        {
            Parallel.ForEach(processors, processor =>
            {
                processor.ProcessFrame(frame, previous);
            });
            previous = frame;
            progress?.Report((double)frame.Timestamp / duration * 100, "Processing video for logos, scene changes, and blank scenes");
        }
        if (previous != null)
            progress?.NewLine();

        foreach (var processor in processors)
        {
            processor.Complete(progress);
        }
    }

    private List<VideoSegment> GenerateSegments(
        IReadOnlyList<LogoDetection> logoDetections,
        double logoThreshold,
        TimeSpan minDuration)
    {
        var segments = new List<VideoSegment>();
        TimeSpan? segmentStart = null;
        bool lastHadLogo = false;

        foreach (var frame in logoDetections.OrderBy(f => f.Time))
        {
            var hasLogo = frame.LogoDiff >= logoThreshold;
            if (hasLogo != lastHadLogo)
            {
                if (hasLogo)
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
                        segments.Add(new VideoSegment(segmentStart.Value, frame.Time));
                    }
                    segmentStart = null;
                }
                lastHadLogo = hasLogo;
            }
        }

        // Handle case where video ends with logo present
        if (segmentStart.HasValue && lastHadLogo)
        {
            var duration = logoDetections.Last().Time - segmentStart.Value;
            if (duration >= minDuration)
            {
                segments.Add(new VideoSegment(segmentStart.Value, logoDetections.Last().Time));
            }
        }

        return segments;
    }

    private List<VideoSegment> ExtendSegmentsToSceneChanges(
        List<VideoSegment> segments,
        double sceneThreshold,
        IProgress<double>? progress = null)
    {
        var startTimes = segments.Select(s => s.Start).ToList();
        var endTimes = segments.Select(s => s.End).ToList();
        var totalSegments = startTimes.Count;
        var processedSegments = 0;
        var result = new List<VideoSegment>();

        for (int index = 0; index < totalSegments; index++)
        {
            var segment = segments[index];

            // Get previous segment end time
            var previousSegmentEnd = index > 0 ? endTimes[index - 1] : TimeSpan.Zero;

            // Search backwards from segment start in 10-second chunks until a scene change is found
            TimeSpan nearestStartChange = FindPreviousSceneChange(segment.Start, previousSegmentEnd, sceneThreshold);
            if (nearestStartChange == TimeSpan.MinValue)
            {
                // No scene change found, use original start time
                nearestStartChange = segment.Start;
            }

            // Get next segment start time
            var nextSegmentStart = index < startTimes.Count - 1 ? startTimes[index + 1] : _mediaFile.GetDurationTimeSpan();

            // Search forwards from segment end until next scene change
            var nearestEndChange = FindNextSceneChange(segment.End, nextSegmentStart, sceneThreshold);
            if (nearestEndChange == TimeSpan.MaxValue)
            {
                // No scene change found, use original end time
                nearestEndChange = segment.End;
            }

            processedSegments++;
            progress?.Report((double)processedSegments / totalSegments * 100);

            result.Add(new VideoSegment(nearestStartChange, nearestEndChange));
        }

        return result;
    }

    public void Dispose()
    {
        _mediaFile?.Dispose();
    }

    private async IAsyncEnumerable<Frame> GetFramesToAnalyze(bool onlyUseKeyFrames = false)
    {
        var duration = _mediaFile.GetDuration();

        int nextSecond = 0;

        var (frame, second) = await GetNextFrameToAnalyzeAsync(null, duration, onlyUseKeyFrames, nextSecond);
        if (frame == null)
            yield break; // No frames to analyze
        nextSecond = second + 1;

        while (frame != null && frame.Timestamp < duration)
        {
            var task = GetNextFrameToAnalyzeAsync(frame, duration, onlyUseKeyFrames, nextSecond);
            yield return frame;
            (frame, second) = await task;
            nextSecond = second + 1;
        }
    }

    private Task<(Frame?, int)> GetNextFrameToAnalyzeAsync(Frame? frame, long duration, bool onlyUseKeyFrames, int nextSecond)
    {
        return Task.Run<(Frame?, int)>(() =>
        {
            if (frame == null)
            {
                frame = _mediaFile.GetFrameAtTimeSpan(TimeSpan.FromSeconds(nextSecond));
                if (frame == null)
                    return (null, nextSecond); // No frames to analyze
            }

            while (frame != null && frame.Timestamp < duration && frame.TimeSpan.TotalSeconds < nextSecond)
            {
                // Skip frames until we reach the next second
                frame = _mediaFile.ReadNextFrame(onlyUseKeyFrames);
            }

            return (frame, nextSecond);
        });
    }

    private IEnumerable<Frame> GetFramesToAnalyzeBySeeking()
    {
        var duration = _mediaFile.GetDurationTimeSpan();

        // Process frames at 2-second intervals
        for (var time = TimeSpan.Zero; time < duration; time += TimeSpan.FromSeconds(2))
        {
            var frame = _mediaFile.GetFrameAtTimeSpan(time);

            if (frame == null)
                break;

            yield return frame;
        }
    }


    private TimeSpan FindPreviousSceneChange(TimeSpan startTime, TimeSpan minTime, double sceneThreshold)
    {
        var earliestTimeLoaded = startTime;

        for (var searchTime = startTime; searchTime >= minTime;)
        {
            // Search backwards in 30-second chunks
            searchTime -= TimeSpan.FromSeconds(30);
            if (searchTime < minTime)
                searchTime = minTime;

            var previousFrame = _mediaFile.GetFrameAtTimeSpan(searchTime);
            if (previousFrame == null)
                continue; // No frame at this time, skip

            var firstFrame = previousFrame;

            Frame? latestSceneChangeFrame = null;

            while (true)
            {
                var frame = _mediaFile.ReadNextFrame();

                if (frame == null)
                    break; // End of video

                if (frame.TimeSpan > earliestTimeLoaded)
                    break; // Stop if we pass the maximum time

                if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData, frame.YData, sceneThreshold))
                {
                    latestSceneChangeFrame = frame;
                }

                previousFrame = frame;
            }

            if (latestSceneChangeFrame != null)
            {
                // Found a scene change, return result
                return latestSceneChangeFrame.TimeSpan;
            }

            earliestTimeLoaded = firstFrame.TimeSpan;
        }

        return TimeSpan.MinValue; // No scene change found before minTime
    }

    private TimeSpan FindNextSceneChange(TimeSpan startTime, TimeSpan maxTime, double sceneThreshold)
    {
        var startTimestamp = startTime.ToTimestamp();
        var maxTimestamp = maxTime.ToTimestamp();

        var previousFrame = _mediaFile.GetFrameAtTimeSpan(startTime);
        if (previousFrame == null)
            return startTime; // No frame at start time, return original time

        while (true)
        {
            var frame = _mediaFile.ReadNextFrame();

            if (frame == null)
                return previousFrame.TimeSpan; // End of video

            if (frame.Timestamp > maxTimestamp)
                return TimeSpan.MaxValue; // Stop if we exceed the maximum time

            if (frame.Timestamp < startTimestamp)
                continue; // Skip frames before the start time in case the keyframe that was seeked to is before the start time

            if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData, frame.YData, sceneThreshold))
            {
                // Convert timestamp to TimeSpan
                return frame.TimeSpan;
            }

            previousFrame = frame;
        }
    }

}
