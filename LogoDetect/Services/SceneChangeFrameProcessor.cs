using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogoDetect.Models;
using ScottPlot;

namespace LogoDetect.Services;

public class SceneChangeFrameProcessor : IFrameProcessor
{
    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly IImageProcessor _imageProcessor;
    private readonly PerformanceTracker _performanceTracker;
    private readonly List<(TimeSpan Time, double ChangeAmount, string Type)> _sceneChanges = new();

    private Action<string>? _debugFileTracker;
    private SharedPlotManager? _sharedPlotManager;
    private SharedDataManager? _sharedDataManager;

    public IReadOnlyList<(TimeSpan Time, double ChangeAmount, string Type)> SceneChanges => _sceneChanges;

    public SceneChangeFrameProcessor(VideoProcessorSettings settings, MediaFile mediaFile, IImageProcessor imageProcessor, PerformanceTracker performanceTracker)
    {
        _settings = settings;
        _mediaFile = mediaFile;
        _imageProcessor = imageProcessor;
        _performanceTracker = performanceTracker;
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
        // var scenePath = _settings.GetOutputFileWithExtension(".scenechanges.csv");
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
        var (isBlack, isWhite, meanLuminance) = _imageProcessor.IsBlackOrWhiteFrame(current.QuarterYData, _settings.blankThreshold);
        
        if (isBlack)
        {
            _sceneChanges.Add((current.TimeSpan, meanLuminance / 255.0, "black"));
            _sharedDataManager?.AddSceneChangeData(current.TimeSpan, meanLuminance / 255.0, "black");
        }
        else if (isWhite)
        {
            _sceneChanges.Add((current.TimeSpan, meanLuminance / 255.0, "white"));
            _sharedDataManager?.AddSceneChangeData(current.TimeSpan, meanLuminance / 255.0, "white");
        }
        else if (previous != null)
        {
            var changeAmount = _imageProcessor.CalculateSceneChangeAmount(previous.QuarterYData, current.QuarterYData);
            _sceneChanges.Add((current.TimeSpan, changeAmount, "scene"));
            _sharedDataManager?.AddSceneChangeData(current.TimeSpan, changeAmount, "scene");
            // if (changeAmount > _settings.sceneThreshold)
            // {
            // }
        }
    }

    public void Complete(IProgressMsg? progress = null)
    {
        var scenePath = _settings.GetOutputFileWithExtension(".scenechanges.csv");

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

        // Add scene change data to shared plot if available
        if (_sharedPlotManager != null)
        {
            SaveSceneChangeDataToSharedPlot(_sharedPlotManager);
        }
    }

    private void SaveSceneChangeDataToSharedPlot(SharedPlotManager plotManager)
    {
        var sceneChanges = _sceneChanges;
        // Check if there are any scene changes to plot
        if (sceneChanges == null || sceneChanges.Count == 0)
        {
            Console.WriteLine("No scene changes to plot. Skipping graph generation.");
            return;
        }

        try
        {
            var plot = plotManager.GetSharedPlot();

            // Split data by type
            var sceneChangeData = sceneChanges.Where(x => x.Type == "scene").ToList();
            var blackFrameData = sceneChanges.Where(x => x.Type == "black").ToList();
            var whiteFrameData = sceneChanges.Where(x => x.Type == "white").ToList();

            // Add scene changes as scatter plot (using left Y-axis)
            if (sceneChangeData.Any())
            {
                var sceneChangeColumns = plot.Add.Scatter(
                    sceneChangeData.Select(d => d.Time.TotalSeconds).ToArray(),
                    sceneChangeData.Select(d => d.ChangeAmount).ToArray());
                sceneChangeColumns.Color = new ScottPlot.Color(64, 64, 64, 64);
                sceneChangeColumns.LineWidth = 0;
                sceneChangeColumns.MarkerSize = 4;
                sceneChangeColumns.LegendText = "Scene Changes";
            }

            // Add black frames as red dots (using left Y-axis)
            if (blackFrameData.Any())
            {
                var blackFrameDots = plot.Add.Scatter(
                    blackFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                    blackFrameData.Select(d => d.ChangeAmount).ToArray());
                blackFrameDots.Color = Colors.Red;
                blackFrameDots.LineWidth = 0;
                blackFrameDots.MarkerSize = 5;
                blackFrameDots.LegendText = "Black Frames";
            }

            // Add white frames as orange dots (using left Y-axis)
            if (whiteFrameData.Any())
            {
                var whiteFrameDots = plot.Add.Scatter(
                    whiteFrameData.Select(d => d.Time.TotalSeconds).ToArray(),
                    whiteFrameData.Select(d => d.ChangeAmount).ToArray());
                whiteFrameDots.Color = Colors.Orange;
                whiteFrameDots.LineWidth = 0;
                whiteFrameDots.MarkerSize = 5;
                whiteFrameDots.LegendText = "White Frames";
            }
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"Error adding scene change data to shared plot: {ex}");
#else
            Console.WriteLine($"Error adding scene change data to shared plot: {ex.Message}");
#endif
        }
    }
}
