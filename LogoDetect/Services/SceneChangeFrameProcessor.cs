using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogoDetect.Models;

namespace LogoDetect.Services;

public class SceneChangeFrameProcessor : IFrameProcessor
{
    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly IImageProcessor _imageProcessor;
    private readonly List<(TimeSpan Time, double ChangeAmount, string Type)> _sceneChanges = new();

    private Action<string>? _debugFileTracker;

    public IReadOnlyList<(TimeSpan Time, double ChangeAmount, string Type)> SceneChanges => _sceneChanges;

    public SceneChangeFrameProcessor(VideoProcessorSettings settings, MediaFile mediaFile, IImageProcessor imageProcessor)
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
        // var scenePath = Path.ChangeExtension(_settings.outputPath ?? _mediaFile.FilePath, ".scenechanges.csv");
        // if (File.Exists(scenePath))
        // {
        //     try
        //     {
        //         var lines = File.ReadAllLines(scenePath);
        //         // Skip header line
        //         foreach (var line in lines.Skip(1))
        //         {
        //             var parts = line.Split(',');
        //             if (parts.Length >= 3)
        //             {
        //                 if (TimeSpan.TryParse(parts[0], out var timeSpan) &&
        //                     double.TryParse(parts[1], out var changeAmount))
        //                 {
        //                     var type = parts[2].Trim();
        //                     _sceneChanges.Add((timeSpan, changeAmount, type));
        //                 }
        //             }
        //         }
        //         progress?.Report(0, $"Loaded {_sceneChanges.Count} scene changes from existing CSV file.");
        //     }
        //     catch (Exception ex)
        //     {
        //         progress?.Report(0, $"Warning: Could not load existing CSV file: {ex.Message}");
        //     }
            
        //     // Save visualization of scene changes
        //     var graphFilePath = Path.ChangeExtension(scenePath, ".png");
        //     SaveSceneChangeGraph(_sceneChanges.ToList(), graphFilePath);

        // }
    }

    public void ProcessFrame(Frame current, Frame? previous)
    {
        // Use the optimized combined function to check for black or white frames with mean luminance
        var (isBlack, isWhite, meanLuminance) = _imageProcessor.IsBlackOrWhiteFrame(current.YData, _settings.blankThreshold);
        
        if (isBlack)
        {
            _sceneChanges.Add((current.TimeSpan, meanLuminance / 255.0, "black"));
        }
        else if (isWhite)
        {
            _sceneChanges.Add((current.TimeSpan, meanLuminance / 255.0, "white"));
        }
        else if (previous != null)
        {
            var changeAmount = _imageProcessor.CalculateSceneChangeAmount(previous.YData, current.YData);
            _sceneChanges.Add((current.TimeSpan, changeAmount, "scene"));
            // if (changeAmount > _settings.sceneThreshold)
            // {
            // }
        }
    }

    public void Complete(IProgressMsg? progress = null)
    {
        var scenePath = Path.ChangeExtension(_settings.outputPath ?? _mediaFile.FilePath, ".scenechanges.csv");

        // Create the directory if it doesn't exist
        var directory = Path.GetDirectoryName(scenePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

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

        // Check if there are any scene changes to plot
        if (sceneChanges == null || sceneChanges.Count == 0)
        {
            Console.WriteLine("No scene changes to plot. Skipping graph generation.");
            return;
        }

        try
        {
            var plot = new ScottPlot.Plot();

            // Split data by type
            var sceneChangeData = sceneChanges.Where(x => x.Type == "scene").ToList();
            var blackFrameData = sceneChanges.Where(x => x.Type == "black").ToList();
            var whiteFrameData = sceneChanges.Where(x => x.Type == "white").ToList();

            // Add scene changes as columns (green)
            if (sceneChangeData.Any())
            {
                var sceneChangeColumns = plot.Add.Scatter(
                    sceneChangeData.Select(d => d.Time.TotalSeconds).ToArray(),
                    sceneChangeData.Select(d => d.ChangeAmount).ToArray());
                sceneChangeColumns.Color = new ScottPlot.Color(64, 64, 64);
                sceneChangeColumns.LineWidth = 0;
                sceneChangeColumns.MarkerSize = 4;
                plot.Legend.IsVisible = true;
            }

            // Add black frames as black dots
            if (blackFrameData.Any())
            {
                var blackFrameDots = plot.Add.Scatter(
                    blackFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                    blackFrameData.Select(d => d.ChangeAmount).ToArray());
                blackFrameDots.Color = new ScottPlot.Color(255, 0, 0);
                blackFrameDots.LineWidth = 0;
                blackFrameDots.MarkerSize = 5;
                plot.Legend.IsVisible = true;
            }

            // Add white frames as black dots
            if (whiteFrameData.Any())
            {
                var whiteFrameDots = plot.Add.Scatter(
                    whiteFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                    whiteFrameData.Select(d => d.ChangeAmount).ToArray());
                whiteFrameDots.Color = new ScottPlot.Color(0, 0, 255);
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
                    .Select(m => TimeSpan.FromMinutes(m).ToString(@"m"))
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating scene change graph: {ex.Message}");
        }
    }

}
