using LogoDetect.Models;

namespace LogoDetect.Services;

/// <summary>
/// A wrapper that instruments IFrameProcessor implementations with performance tracking
/// </summary>
public class InstrumentedFrameProcessor : IFrameProcessor
{
    private readonly IFrameProcessor _innerProcessor;
    private readonly PerformanceTracker _performanceTracker;
    private readonly string _processorName;

    public InstrumentedFrameProcessor(IFrameProcessor innerProcessor, PerformanceTracker performanceTracker)
    {
        _innerProcessor = innerProcessor;
        _performanceTracker = performanceTracker;
        _processorName = innerProcessor.GetType().Name;
    }

    public void SetDebugFileTracker(Action<string> tracker)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.SetDebugFileTracker",
            () => _innerProcessor.SetDebugFileTracker(tracker)
        );
    }

    public void SetSharedPlotManager(SharedPlotManager plotManager)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.SetSharedPlotManager",
            () => _innerProcessor.SetSharedPlotManager(plotManager)
        );
    }

    public void SetSharedDataManager(SharedDataManager dataManager)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.SetSharedDataManager",
            () => _innerProcessor.SetSharedDataManager(dataManager)
        );
    }

    public void Initialize(IProgressMsg? progress = null)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.Initialize",
            () => _innerProcessor.Initialize(progress)
        );
    }

    public void ProcessFrame(Frame current, Frame? previous)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.ProcessFrame",
            () => _innerProcessor.ProcessFrame(current, previous),
            $"Frame: {current.TimeSpan:hh\\:mm\\:ss\\.fff}"
        );
    }

    public void Complete(IProgressMsg? progress = null)
    {
        _performanceTracker.MeasureMethod(
            $"{_processorName}.Complete",
            () => _innerProcessor.Complete(progress)
        );
    }

    /// <summary>
    /// Gets the underlying processor instance (useful for accessing processor-specific properties)
    /// </summary>
    public T GetInnerProcessor<T>() where T : class, IFrameProcessor
    {
        return _innerProcessor as T ?? throw new InvalidCastException($"Inner processor is not of type {typeof(T).Name}");
    }
}
