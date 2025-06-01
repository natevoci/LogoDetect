using System.Runtime.InteropServices;
using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using SkiaSharp;

namespace LogoDetect.Services;

public unsafe class VideoProcessor : IDisposable
{
    private readonly ImageProcessor _imageProcessor;
    private readonly MediaFile _mediaFile;
    private YData? _logoReference;

    public VideoProcessor(string inputPath)
    {
        _imageProcessor = new ImageProcessor();
        _mediaFile = new MediaFile(inputPath);
    }

    public void GenerateLogoReference(string logoPath, IProgress<double>? progress = null)
    {
        var duration = _mediaFile.GetDuration();
        var frame = _mediaFile.GetYDataAtTimestamp(0);
        if (frame == null)
            return;

        var height = frame.YData.Height;
        var width = frame.YData.Width;

        // Create a hardware-accelerated matrix for accumulation
        var referenceMatrix = Matrix<float>.Build.Dense(height, width);
        var framesProcessed = 0;

        // Sample 250 frames evenly spaced throughout the video
        for (int i = 0; i < 250; i++)
        {
            var timestamp = duration * i / 249;
            frame = _mediaFile.GetYDataAtTimestamp(timestamp);
            if (frame != null)
            {
                var edges = _imageProcessor.DetectEdges(frame.YData.MatrixData, frame.YData.Width, frame.YData.Height);
                referenceMatrix = referenceMatrix.Add(edges);
                framesProcessed++;
            }

            // Report progress as a percentage (0-100)
            progress?.Report(i / 249.0 * 100);
        }

        // Divide by number of frames using hardware acceleration
        if (framesProcessed > 0)
        {
            referenceMatrix = referenceMatrix.Divide(framesProcessed);
        }

        _logoReference = new YData(referenceMatrix);

        // Convert _logoReference to a bitmap and save to logoPath
        using var logoBitmap = _logoReference.ToBitmap();
        using var stream = File.Create(logoPath);
        using var data = logoBitmap.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);

        // Report 100% completion
        progress?.Report(100);
    }

    public List<(TimeSpan Time, bool HasLogo)> DetectLogoFrames(double logoThreshold, IProgress<double>? progress = null)
    {
        if (_logoReference == null)
            throw new InvalidOperationException("Logo reference not generated. Call GenerateLogoReference first.");
            
        var duration = _mediaFile.GetDuration();
        var logoDetections = new List<(TimeSpan Time, bool HasLogo)>();

        var frame = _mediaFile.GetYDataAtTimestamp(0);

        while (frame != null && frame.Timestamp < duration)
        {
            // Report progress as a percentage (0-100)
            progress?.Report((double)frame.Timestamp / duration * 100);

            // Detect edges in the current frame using hardware-accelerated matrix operations
            var edges = _imageProcessor.DetectEdges(frame.YData.MatrixData, frame.YData.Width, frame.YData.Height);
            var diff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, edges);

            // Check if the difference is below the threshold
            logoDetections.Add((frame.TimeSpan, diff <= logoThreshold));

            // Read next frame
            frame = _mediaFile.ReadNextKeyFrame();
        }

        // Report 100% completion
        progress?.Report(100);

        return logoDetections;
    }

    public IEnumerable<VideoSegment> GenerateSegments(
        List<(TimeSpan Time, bool HasLogo)> logoDetections,
        TimeSpan minDuration)
    {
        var segments = new List<VideoSegment>();
        TimeSpan? segmentStart = null;
        bool lastHadLogo = false;

        foreach (var frame in logoDetections.OrderBy(f => f.Time))
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
                        segments.Add(new VideoSegment(segmentStart.Value, frame.Time));
                    }
                    segmentStart = null;
                }
                lastHadLogo = frame.HasLogo;
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

    public IEnumerable<VideoSegment> ExtendSegmentsToSceneChanges(
        IEnumerable<VideoSegment> segments,
        double sceneThreshold,
        IProgress<double>? progress = null)
    {
        var startTimes = segments.Select(s => s.Start).ToList();
        var endTimes = segments.Select(s => s.End).ToList();
        var totalSegments = startTimes.Count;
        var processedSegments = 0;

        return segments.Select((segment, index) =>
        {
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

            return new VideoSegment(nearestStartChange, nearestEndChange);
        });
    }

    public void Dispose()
    {
        _mediaFile.Dispose();
    }

    private TimeSpan FindPreviousSceneChange(TimeSpan startTime, TimeSpan minTime, double sceneThreshold)
    {
        for (var searchTime = startTime; searchTime >= minTime; )
        {
            var maxTimestamp = searchTime.ToTimestamp();

            // Search backwards in 10-second chunks
            searchTime -= TimeSpan.FromSeconds(10);
            if (searchTime < minTime)
                searchTime = minTime;

            var previousFrame = _mediaFile.GetYDataAtTimeSpan(searchTime);
            if (previousFrame == null)
                continue; // No frame at this time, skip

            Frame? latestSceneChangeFrame = null;

            while (true)
            {
                var frame = _mediaFile.ReadNextFrame();

                if (frame == null)
                    break; // End of video

                if (frame.Timestamp > maxTimestamp)
                    break; // Stop if we pass the maximum time

                if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData.MatrixData, frame.YData.MatrixData, sceneThreshold))
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
        }

        return TimeSpan.MinValue; // No scene change found before minTime
    }

    private TimeSpan FindNextSceneChange(TimeSpan startTime, TimeSpan maxTime, double sceneThreshold)
    {
        var startTimestamp = startTime.ToTimestamp();
        var maxTimestamp = maxTime.ToTimestamp();

        var previousFrame = _mediaFile.GetYDataAtTimeSpan(startTime);
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

            if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData.MatrixData, frame.YData.MatrixData, sceneThreshold))
            {
                // Convert timestamp to TimeSpan
                return frame.TimeSpan;
            }

            previousFrame = frame;
        }
    }

}
