using System;
using System.Collections.Generic;
using LogoDetect.Models;

namespace LogoDetect.Services;

public class SceneChangeFrameProcessor : IFrameProcessor
{
    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly ImageProcessor _imageProcessor;
    private readonly List<(TimeSpan Time, double ChangeAmount, string Type)> _sceneChanges = new();

    private Action<string>? _debugFileTracker;

    public IReadOnlyList<(TimeSpan Time, double ChangeAmount, string Type)> SceneChanges => _sceneChanges;

    public SceneChangeFrameProcessor(VideoProcessorSettings settings, MediaFile mediaFile, ImageProcessor imageProcessor)
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
    }

    public void ProcessFrame(Frame current, Frame? previous)
    {
        if (_imageProcessor.IsBlackFrame(current.YData, _settings.blankThreshold))
        {
            _sceneChanges.Add((current.TimeSpan, 0.0, "black"));
        }
        else if (_imageProcessor.IsWhiteFrame(current.YData, _settings.blankThreshold))
        {
            _sceneChanges.Add((current.TimeSpan, 0.0, "white"));
        }
        else if (previous != null)
        {
            var changeAmount = _imageProcessor.CalculateSceneChangeAmount(previous.YData, current.YData);
            if (changeAmount > _settings.sceneThreshold)
            {
                _sceneChanges.Add((current.TimeSpan, changeAmount, "scene"));
            }
        }
    }

    public void Complete(IProgressMsg? progress = null)
    {
        string directory = _settings.outputPath ?? Path.GetDirectoryName(_mediaFile.FilePath)!;
        // Create the directory if it doesn't exist
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var scenePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(_mediaFile.FilePath) + ".scenechanges.csv");

        // Write scene changes to CSV file
        using (var writer = new StreamWriter(scenePath, false))
        {
            writer.WriteLine("TimeSpan,ChangeAmount,Type");
            foreach (var change in _sceneChanges.OrderBy(x => x.Time))
            {
                writer.WriteLine($"{change.Time:hh\\:mm\\:ss\\.fff},{change.ChangeAmount:F6},{change.Type}");
            }
        }

        // Save visualization of scene changes
        var graphFilePath = Path.ChangeExtension(scenePath, ".png");
        SaveSceneChangeGraph(_sceneChanges.ToList(), graphFilePath);
        _debugFileTracker?.Invoke(graphFilePath);
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
        var whiteFrameData = sceneChanges.Where(x => x.Type == "white").ToList();

        // Add scene changes as columns (green)
        if (sceneChangeData.Any())
        {
            var sceneChangeColumns = plot.Add.Bars(
                sceneChangeData.Select(d => d.Time.TotalSeconds).ToArray(),
                sceneChangeData.Select(d => d.ChangeAmount).ToArray());
            sceneChangeColumns.Color = new ScottPlot.Color(0, 255, 0);
            plot.Legend.IsVisible = true;
        }

        // Add black frames as black dots at y=0
        if (blackFrameData.Any())
        {
            var blackFrameDots = plot.Add.Scatter(
                blackFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                blackFrameData.Select(d => 0.0).ToArray());
            blackFrameDots.Color = new ScottPlot.Color(0, 0, 0);
            blackFrameDots.LineWidth = 0;
            blackFrameDots.MarkerSize = 5;
            plot.Legend.IsVisible = true;
        }

        // Add white frames as black dots at y=1
        if (whiteFrameData.Any())
        {
            var whiteFrameDots = plot.Add.Scatter(
                whiteFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                whiteFrameData.Select(d => 1.0).ToArray());
            whiteFrameDots.Color = new ScottPlot.Color(0, 0, 0);
            whiteFrameDots.LineWidth = 0;
            whiteFrameDots.MarkerSize = 5;
            plot.Legend.IsVisible = true;
        }

        // Configure axes
        plot.Axes.Title.Label.Text = "Scene Changes and Blank/White Frames";
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

        // Format Y axis with fixed range 0-1
        plot.Axes.Left.Range.Set(0, 1);
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
        _debugFileTracker?.Invoke(graphFilePath);
    }

}
