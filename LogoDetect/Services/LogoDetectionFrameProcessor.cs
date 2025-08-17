using System;
using System.Collections.Generic;
using System.Drawing;
using MathNet.Numerics.LinearAlgebra;
using LogoDetect.Models;
using ScottPlot;

namespace LogoDetect.Services;

public class LogoDetectionFrameProcessor : IFrameProcessor
{
    private const int MaxSecondsInRollingAverage = 30;

    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly ImageProcessor _imageProcessor;

    private YData? _logoReference;
    private Rectangle _logoBoundingRect;
    private readonly List<LogoDetection> _logoDetections = new();
    private readonly Queue<Matrix<float>> _rollingEdgeMaps = new();
    private readonly Queue<TimeSpan> _rollingEdgeTimeSpans = new();
    private Matrix<float>? _sumMatrix;
    private int _height;
    private int _width;
    private bool _initialized = false;
    private Action<string>? _debugFileTracker;

    public IReadOnlyList<LogoDetection> Detections => _logoDetections;

    public LogoDetectionFrameProcessor(VideoProcessorSettings settings, MediaFile mediaFile, ImageProcessor imageProcessor)
    {
        _settings = settings;
        _mediaFile = mediaFile;
        _imageProcessor = imageProcessor;
    }

    public void SetDebugFileTracker(Action<string> tracker)
    {
        _debugFileTracker = tracker;
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
        AnalyzeLogoBoundingRect();

        _initialized = true;
    }

    public void ProcessFrame(Frame current, Frame? previous)
    {
        if (!_initialized || _logoReference == null)
            return;

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
        var logoDiff = _imageProcessor.CompareEdgeData(_logoReference.MatrixData, averageEdgeMap, _logoBoundingRect);
        var logoTimeSpan = current.TimeSpan.Subtract(TimeSpan.FromSeconds(MaxSecondsInRollingAverage / 2.0));
        if (logoTimeSpan >= TimeSpan.Zero)
        {
            _logoDetections.Add(new LogoDetection(logoTimeSpan, logoDiff));
        }
    }

    public void Complete(IProgressMsg? progress = null)
    {
        SaveGraphOfLogoDetectionsWithMethod(_settings.logoThreshold, _mediaFile.GetDurationTimeSpan(), _logoDetections, "edge");
    }

    private void GenerateLogoReference(IProgressMsg? progress = null)
    {
        var logoPath = Path.ChangeExtension(_mediaFile.FilePath, ".logo.png");
        var csvFilePath = Path.ChangeExtension(_mediaFile.FilePath, ".logo.csv");
        if (File.Exists(csvFilePath) && !_settings.forceReload)
        {
            _logoReference = YData.LoadFromCSV(csvFilePath);
            _debugFileTracker?.Invoke(csvFilePath);
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
        _logoReference.SaveBitmapToFile(logoPath);
        _debugFileTracker?.Invoke(logoPath);

        // Create logo CSV file
        _logoReference.SaveToCSV(csvFilePath);
        _debugFileTracker?.Invoke(csvFilePath);
    }

    private void AnalyzeLogoBoundingRect()
    {
        if (_logoReference == null)
            return;

        var matrix = _logoReference.MatrixData;
        var baseValue = 127.0f;
        var threshold = 0.2f * baseValue; // Threshold for deviation from base value
        
        int minX = _width, maxX = 0, minY = _height, maxY = 0;
        bool logoFound = false;

        // Find the bounding rectangle by scanning for pixels that deviate significantly from the base value
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var deviation = Math.Abs(matrix[y, x] - baseValue);
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

        if (logoFound)
        {
            // Add some padding to the bounding rectangle
            var padding = 10;
            _logoBoundingRect = new Rectangle(
                Math.Max(0, minX - padding),
                Math.Max(0, minY - padding),
                Math.Min(_width - 1, maxX + padding) - Math.Max(0, minX - padding),
                Math.Min(_height - 1, maxY + padding) - Math.Max(0, minY - padding)
            );

            SaveLogoBoundingVisualization();
        }
        else
        {
            // If no logo found, use the entire frame
            _logoBoundingRect = new Rectangle(0, 0, _width, _height);
        }
    }

    private void SaveLogoBoundingVisualization()
    {
        if (_logoReference == null)
            return;

        var boundingPath = Path.ChangeExtension(_mediaFile.FilePath, ".logo.bounding.png");
        
        // Create a copy of the logo reference for visualization
        var visualMatrix = _logoReference.MatrixData.Clone();
        
        // Draw the bounding rectangle on the visualization
        // Top and bottom horizontal lines
        for (int x = _logoBoundingRect.Left; x < _logoBoundingRect.Right; x++)
        {
            if (x >= 0 && x < _width)
            {
                if (_logoBoundingRect.Top >= 0 && _logoBoundingRect.Top < _height)
                    visualMatrix[_logoBoundingRect.Top, x] = 255.0f; // White line
                if (_logoBoundingRect.Bottom - 1 >= 0 && _logoBoundingRect.Bottom - 1 < _height)
                    visualMatrix[_logoBoundingRect.Bottom - 1, x] = 255.0f; // White line
            }
        }
        
        // Left and right vertical lines
        for (int y = _logoBoundingRect.Top; y < _logoBoundingRect.Bottom; y++)
        {
            if (y >= 0 && y < _height)
            {
                if (_logoBoundingRect.Left >= 0 && _logoBoundingRect.Left < _width)
                    visualMatrix[y, _logoBoundingRect.Left] = 255.0f; // White line
                if (_logoBoundingRect.Right - 1 >= 0 && _logoBoundingRect.Right - 1 < _width)
                    visualMatrix[y, _logoBoundingRect.Right - 1] = 255.0f; // White line
            }
        }

        var boundingVisualization = new YData(visualMatrix);
        boundingVisualization.SaveBitmapToFile(boundingPath);
        _debugFileTracker?.Invoke(boundingPath);
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
        _debugFileTracker?.Invoke(graphFilePath);
    }
}
