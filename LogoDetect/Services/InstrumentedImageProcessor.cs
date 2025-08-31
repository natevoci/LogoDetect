using LogoDetect.Models;

namespace LogoDetect.Services;

/// <summary>
/// A wrapper that instruments ImageProcessor with performance tracking
/// </summary>
public class InstrumentedImageProcessor : IImageProcessor
{
    private readonly ImageProcessor _innerProcessor;
    private readonly PerformanceTracker _performanceTracker;

    public InstrumentedImageProcessor(ImageProcessor innerProcessor, PerformanceTracker performanceTracker)
    {
        _innerProcessor = innerProcessor;
        _performanceTracker = performanceTracker;
    }

    public InstrumentedImageProcessor(PerformanceTracker performanceTracker)
    {
        _performanceTracker = performanceTracker;
        _innerProcessor = new ImageProcessor(performanceTracker);
    }

    public YData DetectEdges(YData input)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.DetectEdges",
            () => _innerProcessor.DetectEdges(input),
            $"Size: {input.Width}x{input.Height}"
        );
    }

    public bool IsSceneChange(YData prevData, YData currData, double threshold)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.IsSceneChange",
            () => _innerProcessor.IsSceneChange(prevData, currData, threshold),
            $"Size: {currData.Width}x{currData.Height}, Threshold: {threshold:F3}"
        );
    }

    public double CalculateSceneChangeAmount(YData prevData, YData currData)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.CalculateSceneChangeAmount",
            () => _innerProcessor.CalculateSceneChangeAmount(prevData, currData),
            $"Size: {currData.Width}x{currData.Height}"
        );
    }

    public (bool IsBlack, bool IsWhite, float MeanLuminance) IsBlackOrWhiteFrame(YData data, double threshold)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.IsBlackOrWhiteFrame",
            () => _innerProcessor.IsBlackOrWhiteFrame(data, threshold),
            $"Size: {data.Width}x{data.Height}, Threshold: {threshold:F3}"
        );
    }

    // Delegate other methods if needed
    public float CompareEdgeData(MathNet.Numerics.LinearAlgebra.Matrix<float> reference, MathNet.Numerics.LinearAlgebra.Matrix<float> current, System.Drawing.Rectangle boundingRect)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.CompareEdgeData",
            () => _innerProcessor.CompareEdgeData(reference, current, boundingRect),
            $"BoundingRect: {boundingRect.Width}x{boundingRect.Height}"
        );
    }

    public float CompareEdgeData(MathNet.Numerics.LinearAlgebra.Matrix<float> reference, MathNet.Numerics.LinearAlgebra.Matrix<float> current)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.CompareEdgeData",
            () => _innerProcessor.CompareEdgeData(reference, current),
            "FullFrame"
        );
    }

    public bool IsBlackFrame(YData data, double threshold)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.IsBlackFrame",
            () => _innerProcessor.IsBlackFrame(data, threshold),
            $"Size: {data.Width}x{data.Height}, Threshold: {threshold:F3}"
        );
    }

    public bool IsWhiteFrame(YData data, double threshold)
    {
        return _performanceTracker.MeasureMethod(
            "ImageProcessor.IsWhiteFrame",
            () => _innerProcessor.IsWhiteFrame(data, threshold),
            $"Size: {data.Width}x{data.Height}, Threshold: {threshold:F3}"
        );
    }
}
