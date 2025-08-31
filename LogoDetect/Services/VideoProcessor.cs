using System.Runtime.InteropServices;
using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using SkiaSharp;
using ScottPlot;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogoDetect.Services;

public enum FrameProcessingMode
{
    AllFrames,
    OneFramePerSecond,
    KeyFrames
}

public class VideoProcessorSettings
{
    public required string outputPath;
    public double logoThreshold;
    public double sceneThreshold;
    public double blankThreshold;
    public TimeSpan minDuration;
    public string? sceneChangesPath = null;
    public bool forceReload;
    public bool keepDebugFiles;
    public int? maxFramesToProcess = null;
}

public class VideoProcessor : IDisposable
{
    private readonly IImageProcessor _imageProcessor;
    private readonly MediaFile _mediaFile;
    private readonly List<string> _debugFiles = new();
    private readonly PerformanceTracker _performanceTracker = new();

    public VideoProcessor(string inputPath)
    {
        _imageProcessor = new InstrumentedImageProcessor(new ImageProcessor(), _performanceTracker);
        _mediaFile = new MediaFile(inputPath);
    }

    public async Task ProcessVideo(VideoProcessorSettings settings)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var logoDetectionProcessor = new LogoDetectionFrameProcessor(settings, _mediaFile, _imageProcessor);
        var sceneChangeProcessor = new SceneChangeFrameProcessor(settings, _mediaFile, _imageProcessor);

        await _performanceTracker.MeasureMethodAsync(
            "ProcessFrames",
            async () => await ProcessFrames([
                new InstrumentedFrameProcessor(logoDetectionProcessor, _performanceTracker),
                new InstrumentedFrameProcessor(sceneChangeProcessor, _performanceTracker),
            ], settings, new Progress())
        );

        stopwatch.Stop();
        Console.WriteLine($"\nFrames processed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        Console.WriteLine($"Logo detections count: {logoDetectionProcessor.Detections.Count}");
        stopwatch.Restart();
        var segments = new List<LogoDetect.Models.VideoSegment>();
        try
        {
            segments.AddRange(GenerateSegments(logoDetectionProcessor.Detections, settings.logoThreshold, settings.minDuration));
            Console.WriteLine($"Generated {segments.Count} segments");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating segments: {ex.Message}");
            throw;
        }
        // var edgeCsvPath = Path.ChangeExtension(settings.outputPath, "edge.csv");
        // File.WriteAllLines(edgeCsvPath, segments.Select(s => s.ToString()));
        // _debugFiles.Add(edgeCsvPath);
        stopwatch.Stop();
        Console.WriteLine($"Segments generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Write segments to CSV file
        stopwatch.Restart();
        try
        {
            File.WriteAllLines(settings.outputPath, segments.Select(s => s.ToString()));
            Console.WriteLine($"CSV file written successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing CSV file: {ex.Message}");
            throw;
        }
        stopwatch.Stop();
        Console.WriteLine($"CSV file written in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {settings.outputPath}");

        // Export performance data
        try
        {
            var performanceOutputPath = Path.ChangeExtension(settings.outputPath, ".performance.csv");
            _performanceTracker.ExportStatisticsToCsv(performanceOutputPath);
            Console.WriteLine($"Performance CSV exported successfully");
            
            var performanceJsonPath = Path.ChangeExtension(settings.outputPath, ".performance.json");
            _performanceTracker.ExportToJson(performanceJsonPath);
            Console.WriteLine($"Performance JSON exported successfully");
            
            Console.WriteLine($"Performance data written to: {performanceOutputPath}");
            Console.WriteLine($"Detailed performance data written to: {performanceJsonPath}");
            
            // Print performance statistics to console
            _performanceTracker.PrintStatistics();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting performance data: {ex.Message}");
            throw;
        }

        // Delete debug files if not keeping them
        if (!settings.keepDebugFiles)
        {
            foreach (var file in _debugFiles)
            {
                if (!string.Equals(file, settings.outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }

    private void AddDebugFile(string path)
    {
        if (!_debugFiles.Contains(path))
            _debugFiles.Add(path);
    }

    private async Task ProcessFrames(IEnumerable<IFrameProcessor> processors, VideoProcessorSettings settings, IProgressMsg? progress = null)
    {
        var duration = _mediaFile.GetDuration();

        using (var initialiseTimer = _performanceTracker.StartTimer("ProcessFrames.Initialize"))
        {
            foreach (var processor in processors)
            {
                processor.SetDebugFileTracker(AddDebugFile);
                processor.Initialize(progress);
            }
        }

        List<DateTime> framesProcessedTimes = new();
        var frameCount = 0;

        Frame? previous = null;
        await foreach (var frame in GetFramesToAnalyze(FrameProcessingMode.AllFrames))
        {
            frameCount++;
            
            // Check if we've reached the frame limit
            if (settings.maxFramesToProcess.HasValue && frameCount > settings.maxFramesToProcess.Value)
            {
                Console.WriteLine($"Reached frame limit of {settings.maxFramesToProcess.Value} frames. Stopping processing.");
                break;
            }
            
            // Process all frame processors for this frame
            using (var frameTimer = _performanceTracker.StartTimer("ProcessFrames.FrameProcessing", $"Frame {frameCount}: {frame.TimeSpan:hh\\:mm\\:ss\\.fff}"))
            {
                // Parallel.ForEach(processors, processor =>
                // {
                //     processor.ProcessFrame(frame, previous);
                // });
                foreach (var processor in processors)
                {
                    processor.ProcessFrame(frame, previous);
                }
            }
            
            previous = frame;

            framesProcessedTimes.Add(DateTime.UtcNow);
            while (framesProcessedTimes.Count > 1000)
            {
                framesProcessedTimes.RemoveAt(0);
            }

            var fps = framesProcessedTimes.Count > 1 ? framesProcessedTimes.Count / (framesProcessedTimes[framesProcessedTimes.Count - 1] - framesProcessedTimes[0]).TotalSeconds : 0.0;
            var progressText = settings.maxFramesToProcess.HasValue 
                ? $"Processing video for logos, scene changes, and blank scenes: {frameCount}/{settings.maxFramesToProcess.Value} frames  (FPS: {fps:F1})"
                : $"Processing video for logos, scene changes, and blank scenes: {((double)frame.Timestamp / duration * 100):F1}%  (FPS: {fps:F1})";
            progress?.Report(null, progressText);
        }
        if (previous != null)
            progress?.NewLine();

        using (var completeTimer = _performanceTracker.StartTimer("ProcessFrames.Complete"))
        {
            foreach (var processor in processors)
            {
                processor.Complete(progress);
            }
        }
        
        Console.WriteLine($"Total frames processed: {frameCount}");
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
        _performanceTracker?.Dispose();
        _mediaFile?.Dispose();
    }

    private async IAsyncEnumerable<Frame> GetFramesToAnalyze(FrameProcessingMode processingMode = FrameProcessingMode.OneFramePerSecond)
    {
        var duration = _mediaFile.GetDuration();

        switch (processingMode)
        {
            case FrameProcessingMode.AllFrames:
                // Process every frame
                var frame = _mediaFile.GetFrameAtTimeSpan(TimeSpan.Zero);
                while (frame != null && frame.Timestamp < duration)
                {
                    var frameTask = _mediaFile.ReadNextFrameAsync(false);
                    yield return frame;
                    frame = await frameTask;
                }
                break;

            case FrameProcessingMode.OneFramePerSecond:
                // Process one frame per second
                int nextSecond = 0;
                var (framePerSecond, second) = await GetNextFrameToAnalyzeAsync(null, duration, false, nextSecond);
                if (framePerSecond == null)
                    yield break;
                nextSecond = second + 1;

                while (framePerSecond != null && framePerSecond.Timestamp < duration)
                {
                    var task = GetNextFrameToAnalyzeAsync(framePerSecond, duration, false, nextSecond);
                    yield return framePerSecond;
                    (framePerSecond, second) = await task;
                    nextSecond = second + 1;
                }
                break;

            case FrameProcessingMode.KeyFrames:
                // Process only key frames
                var keyFrame = _mediaFile.GetFrameAtTimeSpan(TimeSpan.Zero);
                while (keyFrame != null && keyFrame.Timestamp < duration)
                {
                    var frameTask = _mediaFile.ReadNextFrameAsync(true);
                    yield return keyFrame;
                    keyFrame = await frameTask;
                }
                break;
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
