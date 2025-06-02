using System.Runtime.InteropServices;
using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using SkiaSharp;

namespace LogoDetect.Services;

public unsafe class VideoProcessor : IDisposable
{
    private const int MaxFramesInRollingAverage = 30;
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
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();
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
            var timestamp = duration * i / 250;
            var timeSpan = TimeSpanExtensions.FromTimestamp(timestamp);
            frame = _mediaFile.GetYDataAtTimestamp(timestamp);
            if (frame != null)
            {
                var edges = _imageProcessor.DetectEdges(frame.YData);
                // frame.YData.SaveBitmapToFile(Path.ChangeExtension(logoPath, $".{timeSpan:hh\\-mm\\-ss}.png"));
                // edges.SaveBitmapToFile(Path.ChangeExtension(logoPath, $".{i}.png"));

                referenceMatrix = referenceMatrix.Add(edges.MatrixData);
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
        _logoReference.SaveBitmapToFile(logoPath);

        // Report 100% completion
        progress?.Report(100);
    }
    
    public List<LogoDetection> DetectLogoFrames(double logoThreshold, IProgress<double>? progress = null)
    {
        if (_logoReference == null)
            throw new InvalidOperationException("Logo reference not generated. Call GenerateLogoReference first.");

        var duration = _mediaFile.GetDuration();
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();
        var logoDetections = new List<LogoDetection>();

        var frame = _mediaFile.GetYDataAtTimestamp(0);
        if (frame == null) return logoDetections;

        // Initialize rolling average queue and sum matrix
        var rollingEdgeMaps = new Queue<Matrix<float>>();
        var height = frame.YData.Height;
        var width = frame.YData.Width;
        var sumMatrix = Matrix<float>.Build.Dense(height, width);

        // Pre-fill the rolling average with MaxFramesInRollingAverage frames with blank edge maps
        var blankEdgeMap = Matrix<float>.Build.Dense(height, width, byte.MaxValue / 2.0f);
        for (int i = 0; i < MaxFramesInRollingAverage; i++)
        {
            rollingEdgeMaps.Enqueue(blankEdgeMap);
            sumMatrix = sumMatrix.Add(blankEdgeMap);
        }

        while (frame != null && frame.Timestamp < duration)
        {
            // Report progress as a percentage (0-100)
            progress?.Report((double)frame.Timestamp / duration * 100);

            // Detect edges in the current frame
            var edgeMap = _imageProcessor.DetectEdges(frame.YData);

            // Add current edge map to rolling average
            rollingEdgeMaps.Enqueue(edgeMap.MatrixData);
            sumMatrix = sumMatrix.Add(edgeMap.MatrixData);

            // Remove oldest edge map if we exceed the maximum
            if (rollingEdgeMaps.Count > MaxFramesInRollingAverage)
            {
                var oldestMatrix = rollingEdgeMaps.Dequeue();
                sumMatrix = sumMatrix.Subtract(oldestMatrix);
            }

            // Calculate average edge map
            var averageEdgeMap = sumMatrix.Divide(rollingEdgeMaps.Count);

            // Compare against logo reference
            var logoDiff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, averageEdgeMap);

            // Check if the difference is below the threshold
            logoDetections.Add(new LogoDetection(frame.TimeSpan, logoDiff, logoDiff <= logoThreshold));

            // Read next frame
            frame = _mediaFile.ReadNextKeyFrame();
        }

        // Create debug CSV file
        var debugFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".debug.csv");
        using (var writer = new StreamWriter(debugFilePath, false))
        {
            writer.WriteLine("TimeSpan,Diff,IsAboveThreshold");
            foreach (var detection in logoDetections)
            {
                // Write debug information to CSV
                writer.WriteLine($"{detection.Time:hh\\:mm\\:ss\\.fff},{detection.LogoDiff:F6},{detection.HasLogo}");
            }
        }

        SaveGraphOfLogoDetections(logoThreshold, durationTimeSpan, logoDetections);


        // Report 100% completion
        progress?.Report(100);

        return logoDetections;
    }

    private void SaveGraphOfLogoDetections(double logoThreshold, TimeSpan durationTimeSpan, List<LogoDetection> logoDetections)
    {
        var graphFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".logodifferences.png");
        using (var bitmap = new SKBitmap(800, 400))
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);

                // Setup paint styles
                var axisPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    StrokeWidth = 2,
                    IsAntialias = true
                };

                var linePaint = new SKPaint
                {
                    Color = SKColors.Blue,
                    StrokeWidth = 2,
                    IsAntialias = true
                };

                var textPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 12,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                };

                var tickPaint = new SKPaint
                {
                    Color = SKColors.Gray,
                    StrokeWidth = 1,
                    IsAntialias = true,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 2, 2 }, 0)
                };

                // Draw axes
                canvas.DrawLine(50, 350, 750, 350, axisPaint); // X-axis
                canvas.DrawLine(50, 350, 50, 50, axisPaint); // Y-axis

                // Draw tick marks and labels on X axis
                int minuteInterval = Math.Max(1, (int)(durationTimeSpan.TotalMinutes / 10)); // Show at most 10 labels
                for (int minute = 0; minute <= durationTimeSpan.TotalMinutes; minute += minuteInterval)
                {
                    var timeSpan = TimeSpan.FromMinutes(minute);
                    var x = (float)(50 + (timeSpan.TotalSeconds / durationTimeSpan.TotalSeconds) * 700);
                    
                    // Draw tick mark
                    canvas.DrawLine(x, 350, x, 355, axisPaint);
                    
                    // Draw grid line
                    canvas.DrawLine(x, 350, x, 50, tickPaint);
                    
                    // Draw time label
                    var label = timeSpan.TotalHours >= 1 
                        ? $"{timeSpan:h\\:mm}"
                        : $"{timeSpan:m\\:ss}";
                    canvas.DrawText(label, x, 370, textPaint);
                }

                // Draw Y-axis labels and tick marks
                for (float ratio = 0; ratio <= 1.0f; ratio += 0.2f)
                {
                    var y = 350 - ratio * 300;
                    canvas.DrawLine(45, y, 50, y, axisPaint); // Tick mark
                    canvas.DrawLine(50, y, 750, y, tickPaint); // Grid line
                    canvas.DrawText($"{ratio:F1}", 35, y + 4, new SKPaint 
                    { 
                        Color = SKColors.Black, 
                        TextSize = 12,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Right
                    });
                }

                // Draw axis labels
                canvas.DrawText("Time", 400, 390, new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 14,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                });

                // Draw Y-axis label with rotation
                canvas.Save();
                canvas.RotateDegrees(-90, 20, 200);
                canvas.DrawText("Logo Difference", 20, 200, new SKPaint
                {
                    Color = SKColors.Black,
                    TextSize = 14,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                });
                canvas.Restore();

                // Draw graph lines
                for (int i = 1; i < logoDetections.Count; i++)
                {
                    var prev = logoDetections[i - 1];
                    var curr = logoDetections[i];

                    var x1 = (float)(50 + (prev.Time.TotalSeconds / durationTimeSpan.TotalSeconds) * 700);
                    var y1 = (float)(350 - (prev.LogoDiff / logoThreshold) * 300);
                    var x2 = (float)(50 + (curr.Time.TotalSeconds / durationTimeSpan.TotalSeconds) * 700);
                    var y2 = (float)(350 - (curr.LogoDiff / logoThreshold) * 300);

                    canvas.DrawLine(x1, y1, x2, y2, linePaint);
                }

                // Draw threshold line
                var thresholdPaint = new SKPaint
                {
                    Color = SKColors.Red,
                    StrokeWidth = 1,
                    IsAntialias = true,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0)
                };
                canvas.DrawLine(50, 350 - 300, 750, 350 - 300, thresholdPaint);
                canvas.DrawText("Threshold", 760, 350 - 300, new SKPaint
                {
                    Color = SKColors.Red,
                    TextSize = 12,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Left
                });
            }

            // Save the bitmap to a file
            using var stream = File.Create(graphFilePath);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(stream);
        }
    }

    public IEnumerable<VideoSegment> GenerateSegments(
        List<LogoDetection> logoDetections,
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

            if (previousFrame != null && _imageProcessor.IsSceneChange(previousFrame.YData, frame.YData, sceneThreshold))
            {
                // Convert timestamp to TimeSpan
                return frame.TimeSpan;
            }

            previousFrame = frame;
        }
    }

}
