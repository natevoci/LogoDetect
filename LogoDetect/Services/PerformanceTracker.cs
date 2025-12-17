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

public class ABTestResult<T>
{
    public required T Result { get; set; }
    public string MethodUsed { get; set; } = string.Empty;
    public double ExecutionTimeMs { get; set; }
}

public class ABTestStatistics
{
    public string TestName { get; set; } = string.Empty;
    public string MethodA { get; set; } = string.Empty;
    public string MethodB { get; set; } = string.Empty;
    public int MethodACount { get; set; }
    public int MethodBCount { get; set; }
    public double MethodAAvgMs { get; set; }
    public double MethodBAvgMs { get; set; }
    public double MethodAMinMs { get; set; }
    public double MethodAMaxMs { get; set; }
    public double MethodBMinMs { get; set; }
    public double MethodBMaxMs { get; set; }
    public string Winner { get; set; } = string.Empty;
    public double SpeedupFactor { get; set; }
    public double PerformanceImprovementPercent { get; set; }
}

public class PerformanceTracker : IDisposable
{
    private readonly ConcurrentBag<PerformanceMetric> _metrics = new();
    private readonly object _lockObject = new();
    private readonly PerformanceSession _session = new();
    private readonly Random _random = new();
    private readonly Dictionary<string, List<double>> _abTestMethodATimes = new();
    private readonly Dictionary<string, List<double>> _abTestMethodBTimes = new();
    private readonly Dictionary<string, (string MethodA, string MethodB)> _abTestConfigurations = new();
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
    /// Configures an A/B test with two competing methods
    /// </summary>
    public void ConfigureABTest(string testName, string methodAName, string methodBName)
    {
        lock (_lockObject)
        {
            _abTestConfigurations[testName] = (methodAName, methodBName);
            if (!_abTestMethodATimes.ContainsKey(testName))
                _abTestMethodATimes[testName] = new List<double>();
            if (!_abTestMethodBTimes.ContainsKey(testName))
                _abTestMethodBTimes[testName] = new List<double>();
        }
    }

    /// <summary>
    /// Performs an A/B test by randomly selecting between two methods
    /// </summary>
    public ABTestResult<T> PerformABTest<T>(string testName, Func<T> methodA, Func<T> methodB, string? additionalInfo = null)
    {
        if (!_abTestConfigurations.ContainsKey(testName))
        {
            throw new InvalidOperationException($"A/B test '{testName}' not configured. Call ConfigureABTest first.");
        }

        var config = _abTestConfigurations[testName];
        bool useMethodA = _random.NextDouble() < 0.5;
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;

        T result;
        string methodUsed;

        if (useMethodA)
        {
            result = methodA();
            methodUsed = config.MethodA;
        }
        else
        {
            result = methodB();
            methodUsed = config.MethodB;
        }

        stopwatch.Stop();
        var endTime = DateTime.UtcNow;
        var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;

        // Record the performance metric
        var metric = new PerformanceMetric
        {
            MethodName = $"{testName}.{methodUsed}",
            ElapsedTicks = stopwatch.ElapsedTicks,
            StartTime = startTime,
            EndTime = endTime,
            AdditionalInfo = additionalInfo
        };
        _metrics.Add(metric);

        // Record A/B test timing
        lock (_lockObject)
        {
            if (useMethodA)
            {
                _abTestMethodATimes[testName].Add(executionTimeMs);
            }
            else
            {
                _abTestMethodBTimes[testName].Add(executionTimeMs);
            }
        }

        return new ABTestResult<T>
        {
            Result = result,
            MethodUsed = methodUsed,
            ExecutionTimeMs = executionTimeMs
        };
    }

    /// <summary>
    /// Performs an async A/B test by randomly selecting between two methods
    /// </summary>
    public async Task<ABTestResult<T>> PerformABTestAsync<T>(string testName, Func<Task<T>> methodA, Func<Task<T>> methodB, string? additionalInfo = null)
    {
        if (!_abTestConfigurations.ContainsKey(testName))
        {
            throw new InvalidOperationException($"A/B test '{testName}' not configured. Call ConfigureABTest first.");
        }

        var config = _abTestConfigurations[testName];
        bool useMethodA = _random.NextDouble() < 0.5;
        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;

        T result;
        string methodUsed;

        if (useMethodA)
        {
            result = await methodA();
            methodUsed = config.MethodA;
        }
        else
        {
            result = await methodB();
            methodUsed = config.MethodB;
        }

        stopwatch.Stop();
        var endTime = DateTime.UtcNow;
        var executionTimeMs = stopwatch.Elapsed.TotalMilliseconds;

        // Record the performance metric
        var metric = new PerformanceMetric
        {
            MethodName = $"{testName}.{methodUsed}",
            ElapsedTicks = stopwatch.ElapsedTicks,
            StartTime = startTime,
            EndTime = endTime,
            AdditionalInfo = additionalInfo
        };
        _metrics.Add(metric);

        // Record A/B test timing
        lock (_lockObject)
        {
            if (useMethodA)
            {
                _abTestMethodATimes[testName].Add(executionTimeMs);
            }
            else
            {
                _abTestMethodBTimes[testName].Add(executionTimeMs);
            }
        }

        return new ABTestResult<T>
        {
            Result = result,
            MethodUsed = methodUsed,
            ExecutionTimeMs = executionTimeMs
        };
    }

    /// <summary>
    /// Gets A/B test statistics for a specific test
    /// </summary>
    public ABTestStatistics? GetABTestStatistics(string testName)
    {
        lock (_lockObject)
        {
            if (!_abTestConfigurations.ContainsKey(testName) || 
                !_abTestMethodATimes.ContainsKey(testName) || 
                !_abTestMethodBTimes.ContainsKey(testName))
            {
                return null;
            }

            var config = _abTestConfigurations[testName];
            var methodATimes = _abTestMethodATimes[testName];
            var methodBTimes = _abTestMethodBTimes[testName];

            if (methodATimes.Count == 0 && methodBTimes.Count == 0)
            {
                return null;
            }

            var stats = new ABTestStatistics
            {
                TestName = testName,
                MethodA = config.MethodA,
                MethodB = config.MethodB,
                MethodACount = methodATimes.Count,
                MethodBCount = methodBTimes.Count
            };

            if (methodATimes.Count > 0)
            {
                stats.MethodAAvgMs = methodATimes.Average();
                stats.MethodAMinMs = methodATimes.Min();
                stats.MethodAMaxMs = methodATimes.Max();
            }

            if (methodBTimes.Count > 0)
            {
                stats.MethodBAvgMs = methodBTimes.Average();
                stats.MethodBMinMs = methodBTimes.Min();
                stats.MethodBMaxMs = methodBTimes.Max();
            }

            if (methodATimes.Count > 0 && methodBTimes.Count > 0)
            {
                var methodAAvg = stats.MethodAAvgMs;
                var methodBAvg = stats.MethodBAvgMs;
                var improvement = ((methodAAvg - methodBAvg) / methodAAvg) * 100;

                stats.Winner = methodBAvg < methodAAvg ? config.MethodB : config.MethodA;
                stats.SpeedupFactor = methodBAvg < methodAAvg ? methodAAvg / methodBAvg : methodBAvg / methodAAvg;
                stats.PerformanceImprovementPercent = Math.Abs(improvement);
            }

            return stats;
        }
    }

    /// <summary>
    /// Gets A/B test statistics for all configured tests
    /// </summary>
    public Dictionary<string, ABTestStatistics> GetAllABTestStatistics()
    {
        var results = new Dictionary<string, ABTestStatistics>();
        
        lock (_lockObject)
        {
            foreach (var testName in _abTestConfigurations.Keys)
            {
                var stats = GetABTestStatistics(testName);
                if (stats != null)
                {
                    results[testName] = stats;
                }
            }
        }
        
        return results;
    }

    /// <summary>
    /// Prints A/B test results for a specific test
    /// </summary>
    public void PrintABTestResults(string testName)
    {
        var stats = GetABTestStatistics(testName);
        if (stats == null)
        {
            Console.WriteLine($"No A/B testing data collected for test '{testName}'.");
            return;
        }

        Console.WriteLine($"\n=== A/B TEST RESULTS: {stats.TestName.ToUpper()} ===");
        
        if (stats.MethodACount > 0)
        {
            Console.WriteLine($"{stats.MethodA} Method: {stats.MethodACount} calls, Avg: {stats.MethodAAvgMs:F2}ms, Min: {stats.MethodAMinMs:F2}ms, Max: {stats.MethodAMaxMs:F2}ms");
        }
        
        if (stats.MethodBCount > 0)
        {
            Console.WriteLine($"{stats.MethodB} Method:  {stats.MethodBCount} calls, Avg: {stats.MethodBAvgMs:F2}ms, Min: {stats.MethodBMinMs:F2}ms, Max: {stats.MethodBMaxMs:F2}ms");
        }
        
        if (stats.MethodACount > 0 && stats.MethodBCount > 0)
        {
            var slower = stats.Winner == stats.MethodA ? stats.MethodB : stats.MethodA;
            Console.WriteLine($"Winner: {stats.Winner} method is {stats.SpeedupFactor:F2}x faster than {slower}");
            Console.WriteLine($"Performance improvement: {stats.PerformanceImprovementPercent:F1}%");
        }
    }

    /// <summary>
    /// Prints A/B test results for all configured tests
    /// </summary>
    public void PrintAllABTestResults()
    {
        var allStats = GetAllABTestStatistics();
        
        if (!allStats.Any())
        {
            Console.WriteLine("No A/B testing data collected.");
            return;
        }
        
        foreach (var stats in allStats.Values)
        {
            PrintABTestResults(stats.TestName);
        }
    }

    /// <summary>
    /// Gets performance statistics for all measured methods
    /// </summary>
    public Dictionary<string, MethodStatistics> GetStatistics()
    {
        lock (_lockObject)
        {
            var stats = new Dictionary<string, MethodStatistics>();
            
            foreach (var group in _session.Metrics.GroupBy(m => m.MethodName))
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
    /// Ends the performance tracking session
    /// </summary>
    public void FinalizeStatistics()
    {
        lock (_lockObject)
        {
            _session.SessionEnd = DateTime.UtcNow;
            _session.Metrics.Clear();
            _session.Metrics.AddRange(_metrics.OrderBy(m => m.StartTime));
            _metrics.Clear();
        }
    }

    /// <summary>
    /// Exports detailed performance data to a JSON file
    /// </summary>
    public void ExportToJson(string filePath)
    {
        lock (_lockObject)
        {

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
