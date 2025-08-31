using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace LogoDetect.Services;

public class PerformanceMetric
{
    public string MethodName { get; set; } = string.Empty;
    public long ElapsedTicks { get; set; }
    public double ElapsedMilliseconds => (double)ElapsedTicks / TimeSpan.TicksPerMillisecond;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? AdditionalInfo { get; set; }
}

public class PerformanceSession
{
    public List<PerformanceMetric> Metrics { get; } = new();
    public DateTime SessionStart { get; init; } = DateTime.UtcNow;
    public DateTime SessionEnd { get; set; }
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N")[..8];
}

public class PerformanceTracker : IDisposable
{
    private readonly ConcurrentBag<PerformanceMetric> _metrics = new();
    private readonly object _lockObject = new();
    private readonly PerformanceSession _session = new();
    private bool _disposed = false;

    public string SessionId => _session.SessionId;

    /// <summary>
    /// Measures the execution time of a method and returns both the result and performance metric
    /// </summary>
    public T MeasureMethod<T>(string methodName, Func<T> method, string? additionalInfo = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var result = method();
            return result;
        }
        finally
        {
            stopwatch.Stop();
            var endTime = DateTime.UtcNow;
            
            var metric = new PerformanceMetric
            {
                MethodName = methodName,
                ElapsedTicks = stopwatch.ElapsedTicks,
                StartTime = startTime,
                EndTime = endTime,
                AdditionalInfo = additionalInfo
            };
            
            _metrics.Add(metric);
        }
    }

    /// <summary>
    /// Measures the execution time of an async method
    /// </summary>
    public async Task<T> MeasureMethodAsync<T>(string methodName, Func<Task<T>> method, string? additionalInfo = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var result = await method();
            return result;
        }
        finally
        {
            stopwatch.Stop();
            var endTime = DateTime.UtcNow;
            
            var metric = new PerformanceMetric
            {
                MethodName = methodName,
                ElapsedTicks = stopwatch.ElapsedTicks,
                StartTime = startTime,
                EndTime = endTime,
                AdditionalInfo = additionalInfo
            };
            
            _metrics.Add(metric);
        }
    }

    /// <summary>
    /// Measures the execution time of an async void method
    /// </summary>
    public async Task MeasureMethodAsync(string methodName, Func<Task> method, string? additionalInfo = null)
    {
        await MeasureMethodAsync(methodName, async () => { await method(); return 0; }, additionalInfo);
    }

    /// <summary>
    /// Measures the execution time of a void method
    /// </summary>
    public void MeasureMethod(string methodName, Action method, string? additionalInfo = null)
    {
        MeasureMethod(methodName, () => { method(); return 0; }, additionalInfo);
    }

    /// <summary>
    /// Creates a disposable timer for measuring execution time within a using block
    /// </summary>
    public IDisposable StartTimer(string methodName, string? additionalInfo = null)
    {
        return new PerformanceTimer(this, methodName, additionalInfo);
    }

    /// <summary>
    /// Internal method to add a metric (used by PerformanceTimer)
    /// </summary>
    internal void AddMetric(PerformanceMetric metric)
    {
        _metrics.Add(metric);
    }

    /// <summary>
    /// Gets performance statistics for all measured methods
    /// </summary>
    public Dictionary<string, MethodStatistics> GetStatistics()
    {
        lock (_lockObject)
        {
            var stats = new Dictionary<string, MethodStatistics>();
            
            foreach (var group in _metrics.GroupBy(m => m.MethodName))
            {
                var methodMetrics = group.OrderBy(m => m.ElapsedMilliseconds).ToList();
                var statistics = new MethodStatistics
                {
                    MethodName = group.Key,
                    CallCount = methodMetrics.Count,
                    TotalMilliseconds = methodMetrics.Sum(m => m.ElapsedMilliseconds),
                    AverageMilliseconds = methodMetrics.Average(m => m.ElapsedMilliseconds),
                    MinimumMilliseconds = methodMetrics.Min(m => m.ElapsedMilliseconds),
                    MaximumMilliseconds = methodMetrics.Max(m => m.ElapsedMilliseconds),
                    MedianMilliseconds = GetMedian(methodMetrics.Select(m => m.ElapsedMilliseconds)),
                    P95Milliseconds = GetPercentile(methodMetrics.Select(m => m.ElapsedMilliseconds), 0.95),
                    P99Milliseconds = GetPercentile(methodMetrics.Select(m => m.ElapsedMilliseconds), 0.99)
                };
                stats[group.Key] = statistics;
            }
            
            return stats;
        }
    }

    /// <summary>
    /// Exports detailed performance data to a JSON file
    /// </summary>
    public void ExportToJson(string filePath)
    {
        lock (_lockObject)
        {
            _session.SessionEnd = DateTime.UtcNow;
            _session.Metrics.Clear();
            _session.Metrics.AddRange(_metrics.OrderBy(m => m.StartTime));

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_session, options);
            File.WriteAllText(filePath, json);
        }
    }

    /// <summary>
    /// Exports performance statistics to a CSV file
    /// </summary>
    public void ExportStatisticsToCsv(string filePath)
    {
        var stats = GetStatistics();
        
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("MethodName,CallCount,TotalMs,AverageMs,MinMs,MaxMs,MedianMs,P95Ms,P99Ms");
        
        foreach (var stat in stats.Values.OrderByDescending(s => s.TotalMilliseconds))
        {
            writer.WriteLine($"{stat.MethodName},{stat.CallCount},{stat.TotalMilliseconds:F3}," +
                           $"{stat.AverageMilliseconds:F3},{stat.MinimumMilliseconds:F3}," +
                           $"{stat.MaximumMilliseconds:F3},{stat.MedianMilliseconds:F3}," +
                           $"{stat.P95Milliseconds:F3},{stat.P99Milliseconds:F3}");
        }
    }

    /// <summary>
    /// Prints performance statistics to console in a formatted table
    /// </summary>
    public void PrintStatistics()
    {
        var stats = GetStatistics().Values.OrderByDescending(s => s.TotalMilliseconds).ToList();
        
        if (!stats.Any())
        {
            Console.WriteLine("No performance data collected.");
            return;
        }

        Console.WriteLine("\n=== PERFORMANCE STATISTICS ===");
        Console.WriteLine($"Session ID: {SessionId}");
        Console.WriteLine($"Session Duration: {(_session.SessionEnd - _session.SessionStart).TotalSeconds:F1}s");
        Console.WriteLine();
        
        // Calculate column widths
        var methodNameWidth = Math.Max(10, stats.Max(s => s.MethodName.Length) + 2);
        
        // Header
        var headerLine = $"{"Method".PadRight(methodNameWidth)} {"Calls",6} {"Total",8} {"Avg",8} {"Min",8} {"Max",8} {"P95",8} {"P99",8}";
        Console.WriteLine(headerLine);
        Console.WriteLine($"{new string('-', methodNameWidth)} {new string('-', 6)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)}");
        
        // Data rows
        foreach (var stat in stats)
        {
            var dataLine = $"{stat.MethodName.PadRight(methodNameWidth)} {stat.CallCount,6} " +
                            $"{stat.TotalMilliseconds,8:F1} {stat.AverageMilliseconds,8:F1} " +
                            $"{stat.MinimumMilliseconds,8:F1} {stat.MaximumMilliseconds,8:F1} " +
                            $"{stat.P95Milliseconds,8:F1} {stat.P99Milliseconds,8:F1}";
            Console.WriteLine(dataLine);
        }
        
        Console.WriteLine();
        Console.WriteLine($"Total methods tracked: {stats.Count}");
        Console.WriteLine($"Total measurements: {stats.Sum(s => s.CallCount)}");
        Console.WriteLine($"Total execution time: {stats.Sum(s => s.TotalMilliseconds):F1}ms");
    }

    private static double GetMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        
        if (sorted.Length % 2 == 0)
        {
            return (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
        }
        return sorted[sorted.Length / 2];
    }

    private static double GetPercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        
        var index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Length - 1))];
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session.SessionEnd = DateTime.UtcNow;
            _disposed = true;
        }
    }
}

public class MethodStatistics
{
    public string MethodName { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public double TotalMilliseconds { get; set; }
    public double AverageMilliseconds { get; set; }
    public double MinimumMilliseconds { get; set; }
    public double MaximumMilliseconds { get; set; }
    public double MedianMilliseconds { get; set; }
    public double P95Milliseconds { get; set; }
    public double P99Milliseconds { get; set; }
}

internal class PerformanceTimer : IDisposable
{
    private readonly PerformanceTracker _tracker;
    private readonly string _methodName;
    private readonly string? _additionalInfo;
    private readonly Stopwatch _stopwatch;
    private readonly DateTime _startTime;
    private bool _disposed = false;

    public PerformanceTimer(PerformanceTracker tracker, string methodName, string? additionalInfo)
    {
        _tracker = tracker;
        _methodName = methodName;
        _additionalInfo = additionalInfo;
        _startTime = DateTime.UtcNow;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _stopwatch.Stop();
            var endTime = DateTime.UtcNow;
            
            var metric = new PerformanceMetric
            {
                MethodName = _methodName,
                ElapsedTicks = _stopwatch.ElapsedTicks,
                StartTime = _startTime,
                EndTime = endTime,
                AdditionalInfo = _additionalInfo
            };
            
            _tracker.AddMetric(metric);
            _disposed = true;
        }
    }
}
