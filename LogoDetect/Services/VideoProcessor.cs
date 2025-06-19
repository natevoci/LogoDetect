using System.Runtime.InteropServices;
using LogoDetect.Models;
using MathNet.Numerics.LinearAlgebra;
using SkiaSharp;
using ScottPlot;

namespace LogoDetect.Services;

public unsafe class VideoProcessor : IDisposable
{
    private const int MaxSecondsInRollingAverage = 30;
    private readonly ImageProcessor _imageProcessor;
    private readonly MediaFile _mediaFile;
    private YData? _logoReference;
    private readonly bool _forceReload;

    public MediaFile MediaFile => _mediaFile;

    public VideoProcessor(string inputPath, bool forceReload = false)
    {
        _imageProcessor = new ImageProcessor();
        _mediaFile = new MediaFile(inputPath);
        _forceReload = forceReload;
    }

    public void ProcessVideo(
        double logoThreshold,
        double sceneThreshold,
        double blankThreshold,
        TimeSpan minDuration,
        string outputPath,
        string? sceneChangesPath = null)
    {
        // Edge detection method
        Console.WriteLine("Detecting logo frames with edge detection...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var logoDetections = DetectLogoFramesWithEdgeDetection(logoThreshold, new Progress<double>(p =>
        {
            Console.Write($"\rProgress: {p:F1}%".PadRight(50, ' '));
        }));
        stopwatch.Stop();
        Console.WriteLine($"\nLogo frames detected in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Generate segments based on logo detections
        Console.WriteLine("Generating segments...");
        stopwatch.Restart();
        var segments = new List<LogoDetect.Models.VideoSegment>();
        segments.AddRange(GenerateSegments(logoDetections, logoThreshold, minDuration));
        File.WriteAllLines(Path.ChangeExtension(outputPath, "-edge.csv"), segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"Segments generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Scene changes
        Console.WriteLine("Processing scene changes and blank scenes...");
        stopwatch.Restart();
        var scenePath = sceneChangesPath ?? Path.ChangeExtension(_mediaFile.FilePath, ".scenechanges.csv");
        ProcessSceneChanges(
            scenePath,
            sceneThreshold,
            blankThreshold,
            new Progress<double>(p =>
            {
                Console.Write($"\rProgress: {p:F1}%".PadRight(50, ' '));
                if (p >= 100) Console.WriteLine();
            })
        );
        stopwatch.Stop();
        Console.WriteLine($"\nScene changes processed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Write segments to CSV file
        stopwatch.Restart();
        File.WriteAllLines(outputPath, segments.Select(s => s.ToString()));
        stopwatch.Stop();
        Console.WriteLine($"CSV file written in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Processing complete!");
        Console.WriteLine($"CSV file written to: {outputPath}");
    }

    private List<LogoDetection> DetectLogoFramesWithEdgeDetection(double logoThreshold, IProgress<double>? progress = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine("Generating logo reference...");
        stopwatch.Restart();
        GenerateLogoReference(progress);
        stopwatch.Stop();
        Console.WriteLine($"\nLogo reference generated in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        Console.WriteLine("Detecting logo frames...");
        stopwatch.Restart();
        var logoDetections = DetectLogoFrames(progress);
        stopwatch.Stop();
        Console.WriteLine($"\nLogo frames detected in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        // Save graphs
        Console.WriteLine("Saving graph of logo detections...");
        stopwatch.Restart();
        SaveGraphOfLogoDetectionsWithMethod(logoThreshold, _mediaFile.GetDurationTimeSpan(), logoDetections, "edge");
        stopwatch.Stop();
        Console.WriteLine($"Graph saved in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        return logoDetections;
    }

    private void GenerateLogoReference(IProgress<double>? progress = null)
    {
        var logoPath = Path.ChangeExtension(_mediaFile.FilePath, ".logo.png");
        var csvFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".logo.csv");
        if (File.Exists(csvFilePath) && !_forceReload)
        {
            _logoReference = YData.LoadFromCSV(csvFilePath);
            return;
        }

        var duration = _mediaFile.GetDuration();
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();
        var frame = _mediaFile.GetFrameAtTimestamp(0);
        if (frame == null)
            return;

        var height = frame.YData.Height;
        var width = frame.YData.Width;

        // Create a hardware-accelerated matrix for accumulation
        var referenceMatrix = Matrix<float>.Build.Dense(height, width);
        var framesProcessed = 0;

        // Sample 250 frames evenly spaced from 10% to 75% of the video duration
        for (int i = 0; i < 250; i++)
        {
            var timestamp = (long)(duration * (i / 250.0) * 0.65 + (duration * 0.1));
            var timeSpan = TimeSpanExtensions.FromTimestamp(timestamp);
            frame = _mediaFile.GetFrameAtTimestamp(timestamp);
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

        // Create logo CSV file
        _logoReference.SaveToCSV(csvFilePath);

        // Report 100% completion
        progress?.Report(100);
    }

    private List<LogoDetection> DetectLogoFrames(IProgress<double>? progress = null)
    {
        var logoDetections = new List<LogoDetection>();
        var csvFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".logodifferences.csv");
        if (File.Exists(csvFilePath) && !_forceReload)
        {
            logoDetections = File.ReadAllLines(csvFilePath)
                .Skip(1) // Skip header
                .Select(line => line.Split(','))
                .Select(parts => new LogoDetection(
                    TimeSpan.Parse(parts[0]),
                    float.Parse(parts[1])
                ))
                .ToList();

            return logoDetections;
        }

        if (_logoReference == null)
            throw new InvalidOperationException("Logo reference not generated. Call GenerateLogoReference first.");

        var duration = _mediaFile.GetDuration();
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();

        var refFrame = _mediaFile.GetFrameAtTimestamp(0);
        if (refFrame == null)
            return logoDetections;

        // Initialize rolling average queue and sum matrix
        var rollingEdgeMaps = new Queue<Matrix<float>>();
        var rollingEdgeTimeSpans = new Queue<TimeSpan>();
        var height = refFrame.YData.Height;
        var width = refFrame.YData.Width;
        var sumMatrix = Matrix<float>.Build.Dense(height, width);

        // Pre-fill the rolling average with MaxFramesInRollingAverage frames with blank edge maps
        var blankEdgeMap = Matrix<float>.Build.Dense(height, width, byte.MaxValue / 2.0f);
        for (int i = 0; i < MaxSecondsInRollingAverage; i++)
        {
            rollingEdgeMaps.Enqueue(blankEdgeMap);
            rollingEdgeTimeSpans.Enqueue(TimeSpan.FromSeconds(i - MaxSecondsInRollingAverage));
            sumMatrix = sumMatrix.Add(blankEdgeMap);
        }

        foreach (var frame in GetFramesToAnalyze(false))
        {
            // Report progress as a percentage (0-100)
            progress?.Report((double)frame.Timestamp / duration * 100);

            // frame.YData.SaveBitmapToFile(Path.ChangeExtension(_mediaFile.FilePath, $".{frame.TimeSpan:hh\\-mm\\-ss\\-fff}.png"));

            // Detect edges in the current frame
            var edgeMap = _imageProcessor.DetectEdges(frame.YData);

            // Add current edge map to rolling average
            rollingEdgeMaps.Enqueue(edgeMap.MatrixData);
            rollingEdgeTimeSpans.Enqueue(frame.TimeSpan);
            sumMatrix = sumMatrix.Add(edgeMap.MatrixData);

            // Remove oldest edge map if we exceed the maximum
            while (rollingEdgeTimeSpans.Peek() < frame.TimeSpan.Subtract(TimeSpan.FromSeconds(MaxSecondsInRollingAverage)))
            {
                var oldestMatrix = rollingEdgeMaps.Dequeue();
                rollingEdgeTimeSpans.Dequeue();
                sumMatrix = sumMatrix.Subtract(oldestMatrix);
            }

            // Calculate average edge map
            var averageEdgeMap = sumMatrix.Divide(rollingEdgeMaps.Count);

            // Compare against logo reference
            var logoDiff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, averageEdgeMap);

            var logoTimeSpan = frame.TimeSpan.Subtract(TimeSpan.FromSeconds(MaxSecondsInRollingAverage / 2.0));
            if (logoTimeSpan >= TimeSpan.Zero)
            {
                logoDetections.Add(new LogoDetection(logoTimeSpan, logoDiff));
            }
        }

        // Create logodifferences CSV file
        using (var writer = new StreamWriter(csvFilePath, false))
        {
            writer.WriteLine("TimeSpan,Diff");
            foreach (var detection in logoDetections)
            {
                // Write debug information to CSV
                writer.WriteLine($"{detection.Time:hh\\:mm\\:ss\\.fff},{detection.LogoDiff:F6}");
            }
        }

        // Report 100% completion
        progress?.Report(100);

        return logoDetections;
    }

    private void SaveGraphOfLogoDetections(double logoThreshold, TimeSpan durationTimeSpan, List<LogoDetection> logoDetections)
    {
        SaveGraphOfLogoDetectionsWithMethod(logoThreshold, durationTimeSpan, logoDetections, "edge");
    }

    private void SaveGraphOfLogoDetectionsWithMethod(double logoThreshold, TimeSpan durationTimeSpan, List<LogoDetection> logoDetections, string method)
    {
        var graphFilePath = Path.ChangeExtension(_mediaFile.FilePath, $".logodifferences.{method}.png");

        // Delete existing file if it exists
        if (File.Exists(graphFilePath))
        {
            File.Delete(graphFilePath);
        }

        var plot = new Plot();

        var times = logoDetections.Select(d => d.Time.TotalSeconds).ToArray();
        var diffs = logoDetections.Select(d => (double)d.LogoDiff).ToArray();

        var line = plot.Add.Scatter(times, diffs);
        line.LineWidth = 2;
        line.Color = Colors.Blue;
        line.MarkerSize = 0;
        line.LegendText = $"Logo Differences ({method})";

        // Add horizontal threshold line
        var threshold = plot.Add.HorizontalLine(logoThreshold);
        threshold.Color = Colors.Red;
        threshold.LinePattern = ScottPlot.LinePattern.Dashed;
        threshold.LineWidth = 2;
        threshold.LegendText = "Logo Threshold";

        // Configure axes
        plot.Title($"Logo Detection Results - {method}");
        plot.XLabel("Time (seconds)");
        plot.YLabel("Logo Difference");

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

    private List<VideoSegment> GenerateSegments(
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
    
    private void ProcessSceneChanges(string outputPath, double sceneThreshold, double blankThreshold, IProgress<double>? progress = null)
    {
        var duration = _mediaFile.GetDuration();
        var sceneChanges = new List<(TimeSpan Time, double ChangeAmount, string Type)>();

        // Get all frames and process them sequentially
        Frame? previousFrame = null;
        foreach (var frame in GetFramesToAnalyze(onlyUseKeyFrames: false))
        {
            // Check for black frames
            if (_imageProcessor.IsBlackFrame(frame.YData, blankThreshold))
            {
                sceneChanges.Add((frame.TimeSpan, 0.0, "black"));
            }
            else
            {
                // Calculate scene change percentage
                if (previousFrame != null)
                {
                    var changeAmount = _imageProcessor.CalculateSceneChangeAmount(previousFrame.YData, frame.YData);
                    if (changeAmount > sceneThreshold)
                    {
                        sceneChanges.Add((frame.TimeSpan, changeAmount, "scene"));
                    }
                }
            }

            previousFrame = frame;

            // Report progress as a percentage (0-100)
            progress?.Report((double)frame.Timestamp / duration * 100);
        }

        // Create the directory if it doesn't exist
        var directory = Path.GetDirectoryName(outputPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write scene changes to CSV file
        using (var writer = new StreamWriter(outputPath, false))
        {
            writer.WriteLine("TimeSpan,ChangeAmount,Type");
            foreach (var change in sceneChanges.OrderBy(x => x.Time))
            {
                writer.WriteLine($"{change.Time:hh\\:mm\\:ss\\.fff},{change.ChangeAmount:F6},{change.Type}");
            }
        }

        // Save visualization of scene changes
        var graphFilePath = Path.ChangeExtension(outputPath, ".png");
        SaveSceneChangeGraph(sceneChanges, graphFilePath);
    }

    private void SaveSceneChangeGraph(List<(TimeSpan Time, double ChangeAmount, string Type)> sceneChanges, string graphFilePath)
    {
        if (File.Exists(graphFilePath))
        {
            File.Delete(graphFilePath);
        }

        var plot = new ScottPlot.Plot();

        // Split data by type
        var sceneChangeData = sceneChanges.Where(x => x.Type == "scene").ToList();
        var blackFrameData = sceneChanges.Where(x => x.Type == "black").ToList();

        // Add scene changes (green)
        if (sceneChangeData.Any())
        {
            var sceneChangeLine = plot.Add.Scatter(
                sceneChangeData.Select(d => d.Time.TotalSeconds).ToArray(),
                sceneChangeData.Select(d => d.ChangeAmount).ToArray());            sceneChangeLine.LineWidth = 2;
            sceneChangeLine.Color = new ScottPlot.Color(0, 255, 0);
            sceneChangeLine.MarkerSize = 0;
            plot.Legend.IsVisible = true;
        }

        // Add blank scenes as vertical lines (red)
        bool addedBlankLegend = false;
        foreach (var blackFrame in blackFrameData)
        {
            var verticalLine = plot.Add.VerticalLine(blackFrame.Time.TotalSeconds);
            verticalLine.Color = new ScottPlot.Color(255, 0, 0);
            verticalLine.LineWidth = 1;
            if (!addedBlankLegend)
            {
                addedBlankLegend = true;
                plot.Legend.IsVisible = true;
            }
        }

        // Configure axes
        plot.Axes.Title.Label.Text = "Scene Changes and Blank Scenes";
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Left.Label.Text = "Change Amount";
        
        // Format X axis as time
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();
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

        // Save the plot
        plot.SavePng(graphFilePath, 2000, 1000);
    }

    public void Dispose()
    {
        _mediaFile?.Dispose();
    }

    private IEnumerable<Frame> GetFramesToAnalyze(bool onlyUseKeyFrames = false)
    {
        var duration = _mediaFile.GetDuration();

        var frame = _mediaFile.GetFrameAtTimestamp(0);
        if (frame == null)
            yield break; // No frames to analyze

        int nextSecond = 0;

        while (frame != null && frame.Timestamp < duration)
        {
            if (frame.TimeSpan.TotalSeconds < nextSecond)
            {
                frame = _mediaFile.ReadNextFrame(onlyUseKeyFrames);
                continue; // Skip frames until we reach the next second
            }

            yield return frame;

            frame = _mediaFile.ReadNextFrame(onlyUseKeyFrames);
            nextSecond++;
        }
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
