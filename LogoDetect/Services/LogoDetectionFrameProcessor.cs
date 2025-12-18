using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using MathNet.Numerics.LinearAlgebra;
using LogoDetect.Models;
using ScottPlot;

namespace LogoDetect.Services;

public class LogoDetectionFrameProcessor : IFrameProcessor
{
    private const int MaxSecondsInRollingAverage = 30;

    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly IImageProcessor _imageProcessor;

    private YData? _logoReference;
    private readonly List<LogoDetection> _logoDetections = new();
    private readonly Queue<Matrix<float>> _rollingEdgeMaps = new();
    private readonly Queue<TimeSpan> _rollingEdgeTimeSpans = new();
    private Matrix<float>? _sumMatrix;
    private int _height;
    private int _width;
    private bool _initialized = false;
    private TimeSpan _lastProcessedFrameTime = TimeSpan.Zero;
    private Action<string>? _debugFileTracker;
    private SharedPlotManager? _sharedPlotManager;
    private SharedDataManager? _sharedDataManager;

    public IReadOnlyList<LogoDetection> Detections => _logoDetections;

    public LogoDetectionFrameProcessor(VideoProcessorSettings settings, MediaFile mediaFile, IImageProcessor imageProcessor)
    {
        _settings = settings;
        _mediaFile = mediaFile;
        _imageProcessor = imageProcessor;
    }

    public void SetDebugFileTracker(Action<string> tracker)
    {
        _debugFileTracker = tracker;
    }

    public void SetSharedPlotManager(SharedPlotManager plotManager)
    {
        _sharedPlotManager = plotManager;
    }

    public void SetSharedDataManager(SharedDataManager dataManager)
    {
        _sharedDataManager = dataManager;
    }

    public void Initialize(IProgressMsg? progress = null)
    {
        var refFrame = _mediaFile.GetFrameAtTimestamp(0);
        if (refFrame == null)
            return;

        _height = refFrame.YData.Height;
        _width = refFrame.YData.Width;
        _sumMatrix = Matrix<float>.Build.Dense(_height, _width);
        // Pre-fill the rolling average with blank edge maps
        var blankEdgeMap = Matrix<float>.Build.Dense(_height, _width, byte.MaxValue / 2.0f);
        for (int i = 0; i < MaxSecondsInRollingAverage; i++)
        {
            _rollingEdgeMaps.Enqueue(blankEdgeMap);
            _rollingEdgeTimeSpans.Enqueue(TimeSpan.FromSeconds(i - MaxSecondsInRollingAverage));
            _sumMatrix = _sumMatrix.Add(blankEdgeMap);
        }

        GenerateLogoReference(progress);

        _initialized = true;
    }

    public void ProcessFrame(Frame current, Frame? previous)
    {
        if (!_initialized || _logoReference == null || _logoReference.BoundingRect == Rectangle.Empty)
            return;

        // Only process frames that are at least 1 second apart
        if (current.TimeSpan < _lastProcessedFrameTime.Add(TimeSpan.FromSeconds(1)))
            return;

        _lastProcessedFrameTime = current.TimeSpan;

        var edgeMap = _imageProcessor.DetectEdges(current.YData);
        _rollingEdgeMaps.Enqueue(edgeMap.MatrixData);
        _rollingEdgeTimeSpans.Enqueue(current.TimeSpan);
        _sumMatrix = _sumMatrix!.Add(edgeMap.MatrixData);
        while (_rollingEdgeTimeSpans.Peek() < current.TimeSpan.Subtract(TimeSpan.FromSeconds(MaxSecondsInRollingAverage)))
        {
            var oldestMatrix = _rollingEdgeMaps.Dequeue();
            _rollingEdgeTimeSpans.Dequeue();
            _sumMatrix = _sumMatrix.Subtract(oldestMatrix);
        }
        var averageEdgeMap = _sumMatrix.Divide(_rollingEdgeMaps.Count);
        var logoDiff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, averageEdgeMap, _logoReference.BoundingRect);
        var logoTimeSpan = current.TimeSpan.Subtract(TimeSpan.FromSeconds(MaxSecondsInRollingAverage / 2.0));
        if (logoTimeSpan >= TimeSpan.Zero)
        {
            _logoDetections.Add(new LogoDetection(logoTimeSpan, logoDiff));
            _sharedDataManager?.AddLogoDetection(logoTimeSpan, logoDiff);
        }
    }

    public void Complete(IProgressMsg? progress = null)
    {
        // Save logo detections to CSV file
        var logoDetectionsPath = _settings.GetOutputFileWithExtension(".logodetections.csv");

        // Create the directory if it doesn't exist
        var directory = Path.GetDirectoryName(logoDetectionsPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write logo detections to CSV file
        using (var writer = new StreamWriter(logoDetectionsPath, false))
        {
            writer.WriteLine("TimeSpan,LogoDiff");
            foreach (var detection in _logoDetections.OrderBy(x => x.Time))
            {
                writer.WriteLine($"{detection.Time:hh\\:mm\\:ss\\.fff},{detection.LogoDiff:F6}");
            }
        }
        _debugFileTracker?.Invoke(logoDetectionsPath);

        // Add logo detection data to shared plot if available
        if (_sharedPlotManager != null)
        {
            SaveLogoDetectionDataToSharedPlot(_sharedPlotManager, _logoDetections, "edge");
        }
    }

    private void GenerateLogoReference(IProgressMsg? progress = null)
    {
        var logoPath = _settings.GetOutputFileWithExtension(".logo.png");
        var logoReferenceFilePath = Path.Join(_settings.outputPath, "logo_reference.csv");
        if (File.Exists(logoReferenceFilePath) && !_settings.forceReload)
        {
            _logoReference = YData.LoadFromCSV(logoReferenceFilePath);
            if (_logoReference.BoundingRect == Rectangle.Empty)
            {
                AnalyzeLogoBoundingRect();
            }
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

        // Sample 250 frames evenly spaced from 10% to 70% of the video duration
        var startPercentage = 0.1;
        var endPercentage = 0.70;
        var GetFramesToAnalyze = 500;
        for (int i = 0; i < GetFramesToAnalyze; i++)
        {
            var timestamp = (long)(duration * (i / (double)GetFramesToAnalyze) * (endPercentage - startPercentage) + (duration * startPercentage));
            var timeSpan = TimeSpanExtensions.FromTimestamp(timestamp);
            frame = _mediaFile.GetFrameAtTimestamp(timestamp);
            if (frame != null)
            {
                var edges = _imageProcessor.DetectEdges(frame.YData);

                var saveIndividualFrames = false;
                if (saveIndividualFrames)
                {
                    var yDataPath = Path.ChangeExtension(logoPath, $".{i}.png");
                    frame.YData.SaveBitmapToFile(yDataPath);
                    _debugFileTracker?.Invoke(yDataPath);

                    var edgesPath = Path.ChangeExtension(logoPath, $".{i}.edges.png");
                    edges.SaveBitmapToFile(edgesPath);
                    _debugFileTracker?.Invoke(edgesPath);
                }

                referenceMatrix = referenceMatrix.Add(edges.MatrixData);
                framesProcessed++;
            }

            // Report progress as a percentage (0-100)
            progress?.Report(i / (double)(GetFramesToAnalyze - 1) * 100, "Finding Logo");
        }

        // Divide by number of frames using hardware acceleration
        if (framesProcessed > 0)
        {
            referenceMatrix = referenceMatrix.Divide(framesProcessed);
        }

        _logoReference = new YData(referenceMatrix);

        // Convert _logoReference to a bitmap and save to logoPath
        // _logoReference.SaveBitmapToFile(logoPath);
        // _debugFileTracker?.Invoke(logoPath);

        // Populate the bounding rectangle of the _logoReference
        AnalyzeLogoBoundingRect();

        // Create logo CSV file
        _logoReference.SaveToCSV(logoReferenceFilePath);
        _debugFileTracker?.Invoke(logoReferenceFilePath);

        var logoReferencePngPath = Path.ChangeExtension(logoReferenceFilePath, ".png");
        SaveLogoBoundingVisualization(logoReferencePngPath);
    }

    private void AnalyzeLogoBoundingRect()
    {
        if (_logoReference == null)
            return;

        var matrix = _logoReference.MatrixData;
        var baseValue = 127.0f;
        var originalThreshold = 0.2f * baseValue; // Original threshold for deviation from base value
        var threshold = originalThreshold;

        int minX = _width, maxX = 0, minY = _height, maxY = 0;
        bool logoFound = false;
        float maxDeviation = 0.0f;
        int retryCount = 0;
        const int maxRetries = 2;

        while (!logoFound && retryCount <= maxRetries)
        {
            minX = _width;
            maxX = 0;
            minY = _height;
            maxY = 0;
            maxDeviation = 0.0f;

            // Find the bounding rectangle by scanning for pixels that deviate significantly from the base value
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var deviation = Math.Abs(matrix[y, x] - baseValue);
                    maxDeviation = Math.Max(maxDeviation, deviation);

                    if (deviation > threshold)
                    {
                        logoFound = true;
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            Console.WriteLine($"Logo bounding analysis (attempt {retryCount + 1}) - Max deviation: {maxDeviation:F2}, Threshold: {threshold:F2}, Logo found: {logoFound}");

            if (logoFound && retryCount == 0)
            {
                // Check if bounding rectangle is too large and we can increase threshold
                var boundingArea = (maxX - minX + 1) * (maxY - minY + 1);
                var totalArea = _width * _height;
                var areaPercentage = (double)boundingArea / totalArea;

                if (areaPercentage > 0.25 && maxDeviation > (threshold * 1.6f))
                {
                    Console.WriteLine($"Bounding rectangle covers {areaPercentage:P1} of image and max deviation is > 1.6x threshold. Increasing threshold and retrying...");
                    threshold = threshold * 1.5f;
                    logoFound = false; // Reset to retry
                    continue;
                }
            }

            if (!logoFound && retryCount < maxRetries)
            {
                threshold = threshold / 2.0f;
                retryCount++;
                Console.WriteLine($"No logo found, halving threshold to {threshold:F2} and retrying...");
            }
            else
            {
                break;
            }
        }

        if (logoFound)
        {
            Console.WriteLine($"Logo bounds found using threshold {threshold:F2} completed.");
            // Add some padding to the bounding rectangle
            var padding = 10;
            _logoReference.BoundingRect = new Rectangle(
                Math.Max(0, minX - padding),
                Math.Max(0, minY - padding),
                Math.Min(_width - 1, maxX + padding) - Math.Max(0, minX - padding),
                Math.Min(_height - 1, maxY + padding) - Math.Max(0, minY - padding)
            );
        }
        else
        {
            Console.WriteLine($"Logo bounds not found using threshold {threshold:F2}. Using entire frame.");
            // If no logo found, use the entire frame
            _logoReference.BoundingRect = new Rectangle(0, 0, _width, _height);
        }

        // Interactively confirm bounding rectangle with user
        ShowBoundingBoxSelector();
    }

    private void ShowBoundingBoxSelector()
    {
        if (_logoReference == null)
            return;

        try
        {
            var selector = new BoundingBoxSelector(_logoReference, _logoReference.BoundingRect);
            if (selector.ShowDialog() == true)
            {
                _logoReference.BoundingRect = selector.SelectedBounds;
                Console.WriteLine($"User selected bounding box: {_logoReference.BoundingRect}");
            }
            else
            {
                _logoReference.BoundingRect = Rectangle.Empty;
                Console.WriteLine("User indicated no logo present, setting bounding box to empty.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing bounding box selector: {ex.Message}");
            Console.WriteLine("Continuing with auto-detected bounds");
        }
    }

    private void SaveLogoBoundingVisualization(string? logoPngPath = null)
    {
        if (_logoReference == null || _logoReference.BoundingRect == Rectangle.Empty)
            return;

        var boundingPath = logoPngPath ?? Path.ChangeExtension(_mediaFile.FilePath, ".logo.bounding.png");

        // Create a copy of the logo reference for visualization
        var visualMatrix = _logoReference.MatrixData.Clone();

        // Draw the bounding rectangle on the visualization
        // Top and bottom horizontal lines
        for (int x = _logoReference.BoundingRect.Left; x < _logoReference.BoundingRect.Right; x++)
        {
            if (x >= 0 && x < _width)
            {
                if (_logoReference.BoundingRect.Top >= 0 && _logoReference.BoundingRect.Top < _height)
                    visualMatrix[_logoReference.BoundingRect.Top, x] = 255.0f; // White line
                if (_logoReference.BoundingRect.Bottom - 1 >= 0 && _logoReference.BoundingRect.Bottom - 1 < _height)
                    visualMatrix[_logoReference.BoundingRect.Bottom - 1, x] = 255.0f; // White line
            }
        }

        // Left and right vertical lines
        for (int y = _logoReference.BoundingRect.Top; y < _logoReference.BoundingRect.Bottom; y++)
        {
            if (y >= 0 && y < _height)
            {
                if (_logoReference.BoundingRect.Left >= 0 && _logoReference.BoundingRect.Left < _width)
                    visualMatrix[y, _logoReference.BoundingRect.Left] = 255.0f; // White line
                if (_logoReference.BoundingRect.Right - 1 >= 0 && _logoReference.BoundingRect.Right - 1 < _width)
                    visualMatrix[y, _logoReference.BoundingRect.Right - 1] = 255.0f; // White line
            }
        }

        var boundingVisualization = new YData(visualMatrix);
        boundingVisualization.SaveBitmapToFile(boundingPath);
        _debugFileTracker?.Invoke(boundingPath);
    }

    private void SaveLogoDetectionDataToSharedPlot(SharedPlotManager plotManager, List<LogoDetection> logoDetections, string method)
    {
        // Check if there are any logo detections to plot
        if (logoDetections == null || logoDetections.Count == 0)
        {
            Console.WriteLine($"No logo detections to plot for method {method}. Skipping graph generation.");
            return;
        }

        try
        {
            var plot = plotManager.GetSharedPlot();

            var times = logoDetections.Select(d => d.Time.TotalSeconds).ToArray();
            var diffs = logoDetections.Select(d => (double)d.LogoDiff).ToArray();

            // Ensure we have valid data
            if (times.Length == 0 || diffs.Length == 0)
            {
                Console.WriteLine($"No valid data points for method {method}. Skipping graph generation.");
                return;
            }

            var line = plot.Add.Scatter(times, diffs);
            line.LineWidth = 2;
            line.Color = Colors.Blue;
            line.MarkerSize = 0;
            line.LegendText = $"Logo Differences ({method})";
            line.Axes.YAxis = plot.Axes.Right;

            // Add horizontal threshold line
            var threshold = plot.Add.HorizontalLine(_settings.logoThreshold);
            threshold.Color = Colors.Red;
            threshold.LinePattern = ScottPlot.LinePattern.Dashed;
            threshold.LineWidth = 2;
            threshold.LegendText = "Logo Threshold";
            threshold.Axes.YAxis = plot.Axes.Right;

            // Configure right Y-axis range based on the data
            // var maxDiff = logoDetections.Max(d => (double)d.LogoDiff);
            // var minDiff = logoDetections.Min(d => (double)d.LogoDiff);
            // var range = maxDiff - minDiff;
            // var padding = range * 0.1; // 10% padding
            
            // plot.Axes.Right.Range.Set(Math.Max(0, minDiff - padding), maxDiff + padding);
            // plot.Axes.Right.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"Error adding logo detection data to shared plot: {ex}");
#else
            Console.WriteLine($"Error adding logo detection data to shared plot: {ex.Message}");
#endif
        }
    }
}
