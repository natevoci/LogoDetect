using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogoDetect.Models;

namespace LogoDetect.Services;

public class SharedDataManager
{
    private readonly VideoProcessorSettings _settings;
    private readonly List<CombinedFrameData> _frameData = new();
    private Action<string>? _debugFileTracker;

    public SharedDataManager(VideoProcessorSettings settings)
    {
        _settings = settings;
    }

    public void SetDebugFileTracker(Action<string> tracker)
    {
        _debugFileTracker = tracker;
    }

    public void AddLogoDetection(TimeSpan time, float logoDiff)
    {
        GetOrCreateFrameData(time).LogoDiff = logoDiff;
    }

    public void AddMeanLuminance(TimeSpan time, double meanLuminance)
    {
        GetOrCreateFrameData(time).MeanLuminance = meanLuminance;
    }

    public void AddBlackWhiteFrame(TimeSpan time, bool isBlack, bool isWhite)
    {
        var frameData = GetOrCreateFrameData(time);
        frameData.IsBlackFrame = isBlack;
        frameData.IsWhiteFrame = isWhite;
    }

    public void AddSceneChangeData(TimeSpan time, double changeAmount)
    {
        GetOrCreateFrameData(time).SceneChange = changeAmount;
    }

    private CombinedFrameData GetOrCreateFrameData(TimeSpan time)
    {
        var existing = _frameData.FirstOrDefault(f => f.Time == time);
        if (existing != null)
        {
            return existing;
        }

        var newData = new CombinedFrameData { Time = time };
        _frameData.Add(newData);
        return newData;
    }

    public void SaveCombinedCsv()
    {
        var csvFilePath = _settings.GetOutputFileWithExtension(".combined.csv");
        
        // Create the directory if it doesn't exist
        var directory = Path.GetDirectoryName(csvFilePath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Delete existing file if it exists
        if (File.Exists(csvFilePath))
        {
            File.Delete(csvFilePath);
        }

        try
        {
            using (var writer = new StreamWriter(csvFilePath, false))
            {
                // Write header
                writer.WriteLine("Time,LogoDiff,MeanLuminance,IsBlackFrame,IsWhiteFrame,SceneChange");
                
                // Write data sorted by time
                foreach (var data in _frameData.OrderBy(f => f.Time))
                {
                    writer.WriteLine($"{data.Time:hh\\:mm\\:ss\\.fff},{data.LogoDiff:F6},{data.MeanLuminance:F2},{data.IsBlackFrame},{data.IsWhiteFrame},{data.SceneChange:F6}");
                }
            }
            
            _debugFileTracker?.Invoke(csvFilePath);
            Console.WriteLine($"Combined CSV saved to: {csvFilePath}");
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.WriteLine($"Error saving combined CSV: {ex}");
#else
            Console.WriteLine($"Error saving combined CSV: {ex.Message}");
#endif
        }
    }

    private class CombinedFrameData
    {
        public TimeSpan Time { get; set; }
        public float LogoDiff { get; set; } = 0.0f;
        public double MeanLuminance { get; set; } = 0.0;
        public bool IsBlackFrame { get; set; } = false;
        public bool IsWhiteFrame { get; set; } = false;
        public double SceneChange { get; set; } = 0.0;
    }
}
