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
                // frame.YData.SaveBitmapToFile(Path.ChangeExtension(logoPath, $".{i}.png"));
                // edges.SaveBitmapToFile(Path.ChangeExtension(logoPath, $".{i}.edges.png"));

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
        var csvFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".logodifferences.csv");
        if (File.Exists(csvFilePath))
        {
            var logoDetection = File.ReadAllLines(csvFilePath)
                .Skip(1) // Skip header
                .Select(line => line.Split(','))
                .Select(parts => new LogoDetection(
                    TimeSpan.Parse(parts[0]),
                    float.Parse(parts[1])
                ))
                .ToList();

            return logoDetection;
        }

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

        int nextSecond = 0;

        while (frame != null && frame.Timestamp < duration)
        {
            if (frame.TimeSpan.TotalSeconds < nextSecond)
            {
                frame = _mediaFile.ReadNextFrame();
                continue; // Skip frames until we reach the next second
            }

            nextSecond++;

            // Report progress as a percentage (0-100)
            progress?.Report((double)frame.Timestamp / duration * 100);

            // frame.YData.SaveBitmapToFile(Path.ChangeExtension(_mediaFile.FilePath, $".{frame.TimeSpan:hh\\-mm\\-ss\\-fff}.png"));

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
            logoDetections.Add(new LogoDetection(frame.TimeSpan, logoDiff));

            // Read next frame
            frame = _mediaFile.ReadNextFrame();
        }

        // Create logodifferences CSV file
        using (var writer = new StreamWriter(csvFilePath, false))
        {
            writer.WriteLine("TimeSpan,Diff,IsAboveThreshold");
            foreach (var detection in logoDetections)
            {
                // Write debug information to CSV
                writer.WriteLine($"{detection.Time:hh\\:mm\\:ss\\.fff},{detection.LogoDiff:F6}");
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
        
        // Delete existing file if it exists
        if (File.Exists(graphFilePath))
        {
            File.Delete(graphFilePath);
        }
        
        var plot = new ScottPlot.Plot();

        // Create arrays for plotting
        var times = logoDetections.Select(d => d.Time.TotalSeconds).ToArray();
        var diffs = logoDetections.Select(d => (double)d.LogoDiff).ToArray();

        // Add the logo difference line
        var line = plot.Add.Scatter(times, diffs);
        line.LineWidth = 2;
        line.Color = new ScottPlot.Color(0, 0, 255); // Blue
        line.MarkerSize = 0;

        // Add horizontal threshold line
        var threshold = plot.Add.HorizontalLine(1.0);
        threshold.Color = new ScottPlot.Color(255, 0, 0); // Red
        threshold.LinePattern = ScottPlot.LinePattern.Dashed;

        // Configure axes
        plot.Axes.Title.Label.Text = "";
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Left.Label.Text = "Logo Difference";
        
        // Format X axis as time
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions: Enumerable.Range(0, (int)(durationTimeSpan.TotalMinutes) + 1)
                .Select(m => m * 60.0)
                .ToArray(),
            labels: Enumerable.Range(0, (int)(durationTimeSpan.TotalMinutes) + 1)
                .Select(m => TimeSpan.FromMinutes(m).ToString(@"mm\:ss"))
                .ToArray()
        );

        // Format Y axis
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions: Enumerable.Range(0, 6).Select(n => n * 0.2).ToArray(),
            labels: Enumerable.Range(0, 6).Select(n => (n * 0.2).ToString("F1")).ToArray()
        );

        // Style the plot
        plot.FigureBackground.Color = new ScottPlot.Color(255, 255, 255); // White
        plot.DataBackground.Color = new ScottPlot.Color(255, 255, 255); // White
        plot.Grid.MajorLineColor = new ScottPlot.Color(200, 200, 200); // Light gray
        plot.Grid.MajorLineWidth = 1;

        // Add legend for threshold line
        plot.Legend.IsVisible = true;
        threshold.LabelText = "Threshold";

        // Save the plot
        plot.SavePng(graphFilePath, 2000, 1000);
    }

    public List<VideoSegment> GenerateSegments(
        List<LogoDetection> logoDetections,
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

    public List<VideoSegment> ExtendSegmentsToSceneChanges(
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
        _mediaFile.Dispose();
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

            var previousFrame = _mediaFile.GetYDataAtTimeSpan(searchTime);
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
