# Performance Instrumentation System

This document describes the performance instrumentation system added to LogoDetect for gathering detailed performance data and enabling A/B testing of different functionality implementations.

## Overview

The performance instrumentation system tracks execution time of methods across the video processing pipeline, providing detailed statistics to identify performance bottlenecks and compare different implementations.

## Key Components

### 1. PerformanceTracker (`Services/PerformanceTracker.cs`)
- **Core tracking engine** that measures method execution times
- **Statistics calculation** including averages, percentiles, min/max values
- **Multiple output formats** (JSON, CSV, console table)
- **Thread-safe** for concurrent operations

Key features:
- High-precision timing using `Stopwatch` and `DateTime`
- Statistical analysis (P95, P99, median, etc.)
- Session tracking with unique IDs
- Automatic resource disposal

### 2. InstrumentedFrameProcessor (`Services/InstrumentedFrameProcessor.cs`)
- **Wrapper for IFrameProcessor** implementations
- **Transparent instrumentation** - automatically measures all interface methods
- **Frame-level tracking** with timestamps and frame information
- **Type preservation** - access to underlying processor via `GetInnerProcessor<T>()`

### 3. InstrumentedImageProcessor (`Services/InstrumentedImageProcessor.cs`)
- **Wrapper for ImageProcessor** class
- **Method-level tracking** for frequently called image processing functions:
  - `DetectEdges()` - Edge detection algorithm
  - `IsSceneChange()` - Scene change detection
  - `CalculateSceneChangeAmount()` - Scene change quantification
  - `IsBlackOrWhiteFrame()` - SIMD-accelerated frame classification
  - `CompareEdgeData()` - Logo matching comparison

## Usage

### Automatic Instrumentation
The system is automatically integrated into `VideoProcessor`:

```csharp
// VideoProcessor automatically creates instrumented versions
var logoDetectionProcessor = new LogoDetectionFrameProcessor(settings, _mediaFile, _instrumentedImageProcessor);
var sceneChangeProcessor = new SceneChangeFrameProcessor(settings, _mediaFile, _instrumentedImageProcessor);

// Wrapped with performance tracking
var instrumentedProcessors = new List<IFrameProcessor>
{
    new InstrumentedFrameProcessor(sceneChangeProcessor, _performanceTracker),
};
```

### Manual Method Tracking
For custom instrumentation:

```csharp
// Measure method execution
var result = _performanceTracker.MeasureMethod(
    "MyMethod",
    () => SomeExpensiveOperation(),
    "additional context info"
);

// Measure async methods
var result = await _performanceTracker.MeasureMethodAsync(
    "MyAsyncMethod", 
    async () => await SomeAsyncOperation()
);

// Using disposable timer
using var timer = _performanceTracker.StartTimer("MethodName", "context");
// ... method execution ...
// Timer automatically stops and records when disposed
```

## Output Files

After processing, the system generates several performance data files:

### 1. Performance Statistics CSV (`video.performance.csv`)
Summary statistics for each tracked method:
```csv
MethodName,CallCount,TotalMs,AverageMs,MinMs,MaxMs,MedianMs,P95Ms,P99Ms
ImageProcessor.IsBlackOrWhiteFrame,15420,892.5,0.058,0.032,2.145,0.054,0.098,0.156
SceneChangeFrameProcessor.ProcessFrame,15420,1205.8,0.078,0.045,3.221,0.072,0.134,0.198
```

### 2. Detailed Performance JSON (`video.performance.json`)
Complete session data with individual measurements:
```json
{
  "sessionId": "a1b2c3d4",
  "sessionStart": "2025-08-31T...",
  "sessionEnd": "2025-08-31T...",
  "metrics": [
    {
      "methodName": "ImageProcessor.IsBlackOrWhiteFrame",
      "elapsedTicks": 520000,
      "startTime": "2025-08-31T...",
      "endTime": "2025-08-31T...",
      "additionalInfo": "Size: 1920x1080, Threshold: 0.050"
    }
  ]
}
```

### 3. Console Output
Real-time performance table displayed after processing:
```
=== PERFORMANCE STATISTICS ===
Session ID: a1b2c3d4
Session Duration: 45.2s

Method                                      Calls    Total     Avg     Min     Max     P95     P99
------------------------------------------- ------ -------- -------- -------- -------- -------- --------
ImageProcessor.IsBlackOrWhiteFrame          15420    892.5     0.06     0.03     2.15     0.10     0.16
SceneChangeFrameProcessor.ProcessFrame       15420   1205.8     0.08     0.05     3.22     0.13     0.20
ProcessFrames.FrameProcessing                15420   1847.2     0.12     0.08     4.89     0.21     0.31

Total methods tracked: 8
Total measurements: 61680
Total execution time: 5247.8ms
```

## Performance Metrics Explained

- **CallCount**: Number of times method was executed
- **TotalMs**: Total time spent in method across all calls
- **AverageMs**: Mean execution time per call
- **MinMs/MaxMs**: Fastest and slowest execution times
- **MedianMs**: Middle value when all times are sorted
- **P95Ms**: 95th percentile - 95% of calls completed faster than this
- **P99Ms**: 99th percentile - 99% of calls completed faster than this

## A/B Testing Framework

The instrumentation system enables easy A/B testing of different implementations:

### 1. Create Alternative Implementations
```csharp
// Version A: Current SIMD implementation
public (bool IsBlack, bool IsWhite) IsBlackOrWhiteFrame_VersionA(YData data, double threshold)
{
    // SIMD implementation using Vector<float>
}

// Version B: Alternative approach
public (bool IsBlack, bool IsWhite) IsBlackOrWhiteFrame_VersionB(YData data, double threshold)
{
    // Different algorithm or optimization
}
```

### 2. Measure Both Versions
```csharp
var resultA = _performanceTracker.MeasureMethod(
    "IsBlackOrWhiteFrame_VersionA",
    () => IsBlackOrWhiteFrame_VersionA(data, threshold)
);

var resultB = _performanceTracker.MeasureMethod(
    "IsBlackOrWhiteFrame_VersionB", 
    () => IsBlackOrWhiteFrame_VersionB(data, threshold)
);
```

### 3. Compare Results
The performance data will show:
- Which version is faster on average
- Consistency (lower variance in timing)
- Edge case performance (P95/P99 times)
- Resource usage patterns

## Best Practices

### 1. Method Naming Convention
- Use descriptive names: `"ClassName.MethodName"`
- Include version info for A/B testing: `"IsBlackOrWhiteFrame_V2"`
- Add context for parameters: `"DetectEdges_1920x1080"`

### 2. Additional Info Usage
Provide context that helps with analysis:
```csharp
_performanceTracker.MeasureMethod(
    "ImageProcessor.DetectEdges",
    () => DetectEdges(input),
    $"Size: {input.Width}x{input.Height}, Format: {input.Format}"
);
```

### 3. Statistical Interpretation
- **Focus on P95/P99** for user experience (worst-case scenarios)
- **Compare medians** rather than averages for typical performance
- **Monitor variance** - consistent timing is often better than occasionally fast timing
- **Consider total time** for overall system impact

## Integration Points

The instrumentation system integrates at multiple levels:

1. **VideoProcessor** level - Overall processing pipeline timing
2. **FrameProcessor** level - Individual processor performance  
3. **ImageProcessor** level - Core algorithm performance
4. **Method** level - Granular function timing

This hierarchical approach enables identification of bottlenecks at any level of the processing pipeline.

## Future Enhancements

Planned improvements to the performance system:

1. **Memory usage tracking** alongside timing data
2. **Hardware performance counters** (CPU cache misses, branch predictions)
3. **Automatic baseline comparison** against previous runs
4. **Performance regression detection** in CI/CD pipelines
5. **Real-time performance monitoring** during processing
6. **GPU acceleration timing** when CUDA is enabled

## Example Analysis Workflow

1. **Run baseline measurement** with current implementation
2. **Implement alternative approach** (e.g., different SIMD strategy)
3. **Run comparison measurement** with same test data
4. **Analyze results** using CSV/JSON output
5. **Make informed decision** based on statistical data
6. **Deploy best-performing version** to production

The instrumentation system provides the objective data needed to make performance optimization decisions based on real measurements rather than assumptions.
