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

    public void GenerateLogoReference(string logoPath)
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
    }

    public List<(TimeSpan Time, bool HasLogo)> DetectLogoFrames(double logoThreshold)
    {
        if (_logoReference == null)
            throw new InvalidOperationException("Logo reference not generated. Call GenerateLogoReference first."); var duration = _mediaFile.GetDuration();
        var logoDetections = new List<(TimeSpan Time, bool HasLogo)>();

        // Process frames at 1-second intervals
        for (var time = TimeSpan.Zero; time < TimeSpan.FromMilliseconds(duration / 1000); time += TimeSpan.FromSeconds(1))
        {
            var timestamp = (long)(time.TotalSeconds * 1_000_000);
            var frame = _mediaFile.GetYDataAtTimestamp(timestamp);

            if (frame == null) break;

            // Detect logo presence using hardware-accelerated matrix operations
            var edges = _imageProcessor.DetectEdges(frame.YData.MatrixData, frame.YData.Width, frame.YData.Height);
            var diff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, edges);
            logoDetections.Add((time, diff <= logoThreshold));
        }

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
        double sceneThreshold)
    {
        return segments.Select(segment =>
        {
            // Search backwards from segment start in 10-second chunks until a scene change is found
            TimeSpan nearestStartChange = FindPreviousSceneChange(segment.Start, sceneThreshold);

            // Search forwards from segment end until next scene change
            var nearestEndChange = FindNextSceneChange(segment.End, sceneThreshold);

            return new VideoSegment(nearestStartChange, nearestEndChange);
        });
    }

    public void Dispose()
    {
        _mediaFile.Dispose();
    }

    private TimeSpan FindPreviousSceneChange(TimeSpan startTime, double sceneThreshold)
    {
        Frame? previousFrame = null;
        var lastSceneChange = startTime;

        // Search backwards in 10-second chunks
        for (var time = startTime; time >= TimeSpan.Zero; time -= TimeSpan.FromSeconds(1))
        {
            var timestamp = (long)(time.TotalSeconds * 1_000_000);
            var frame = _mediaFile.GetYDataAtTimestamp(timestamp);

            if (frame == null) continue;

            if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData.MatrixData, frame.YData.MatrixData, sceneThreshold))
            {
                lastSceneChange = time;
            }

            previousFrame = frame;
        }

        return lastSceneChange;
    }

    private TimeSpan FindNextSceneChange(TimeSpan startTime, double sceneThreshold)
    {
        Frame? previousFrame = null;

        for (var time = startTime; true; time += TimeSpan.FromSeconds(1))
        {
            var timestamp = (long)(time.TotalSeconds * 1_000_000);
            var frame = _mediaFile.GetYDataAtTimestamp(timestamp);
            
            if (frame == null) break;

            if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData.MatrixData, frame.YData.MatrixData, sceneThreshold))
            {
                return time;
            }

            previousFrame = frame;
        }

        return startTime; // No scene change found, return original time;
    }

}
