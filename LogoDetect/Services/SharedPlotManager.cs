using System;
using System.IO;
using System.Linq;
using ScottPlot;

namespace LogoDetect.Services;

public class SharedPlotManager
{
    private readonly VideoProcessorSettings _settings;
    private readonly MediaFile _mediaFile;
    private readonly Plot _sharedPlot;
    private Action<string>? _debugFileTracker;

    public SharedPlotManager(VideoProcessorSettings settings, MediaFile mediaFile)
    {
        _settings = settings;
        _mediaFile = mediaFile;
        _sharedPlot = new Plot();
        InitializePlot();
    }

    public Plot GetSharedPlot() => _sharedPlot;

    public void SetDebugFileTracker(Action<string> tracker)
    {
        _debugFileTracker = tracker;
    }

    private void InitializePlot()
    {
        var durationTimeSpan = _mediaFile.GetDurationTimeSpan();

        // Configure basic plot settings
        _sharedPlot.Title("Combined Video Analysis Results");
        _sharedPlot.XLabel("Time");
        _sharedPlot.YLabel("Scene Change Amount");

        // Format X axis as time
        _sharedPlot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions: Enumerable.Range(0, (int)(durationTimeSpan.TotalMinutes) + 1)
                .Select(m => m * 60.0)
                .ToArray(),
            labels: Enumerable.Range(0, (int)(durationTimeSpan.TotalMinutes) + 1)
                .Select(m => $"{m}m")
                .ToArray()
        );

        // Configure left Y axis for Scene Changes (0-1 range)
        _sharedPlot.Axes.Left.Label.Text = "Scene Change Amount";
        _sharedPlot.Axes.Left.Range.Set(0, 1);
        _sharedPlot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions: new double[] { 0, 0.2, 0.4, 0.6, 0.8, 1.0 },
            labels: new string[] { "0.0", "0.2", "0.4", "0.6", "0.8", "1.0" }
        );

        // Configure right Y axis for Logo Detection (will be set when logo detection data is added)
        _sharedPlot.Axes.Right.Label.Text = "Logo Difference";
        
        // Style the plot
        _sharedPlot.FigureBackground.Color = new ScottPlot.Color(255, 255, 255); // White
        _sharedPlot.DataBackground.Color = new ScottPlot.Color(255, 255, 255); // White
        _sharedPlot.Grid.MajorLineColor = new ScottPlot.Color(200, 200, 200); // Light gray
        _sharedPlot.Grid.MajorLineWidth = 1;

        // Enable legend
        _sharedPlot.Legend.IsVisible = true;
        _sharedPlot.Legend.Alignment = Alignment.UpperRight;
    }

    public void SaveCombinedGraph()
    {
        var graphFilePath = _settings.GetOutputFileWithExtension(".combined.png");
        
        if (File.Exists(graphFilePath))
        {
            File.Delete(graphFilePath);
        }

        try
        {
            // Save the plot
            _sharedPlot.SavePng(graphFilePath, 2000, 1000);
            _debugFileTracker?.Invoke(graphFilePath);
            
            Console.WriteLine($"Combined graph saved to: {graphFilePath}");
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"Error saving combined graph: {ex}");
#else
            Console.WriteLine($"Error saving combined graph: {ex.Message}");
#endif
        }
    }
}
