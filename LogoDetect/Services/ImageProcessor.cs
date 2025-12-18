using SkiaSharp;
using System.Drawing;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using System.Runtime.InteropServices;
using MathNet.Numerics;
using LogoDetect.Models;

namespace LogoDetect.Services;

public class ImageProcessor : IImageProcessor
{
    private readonly float[] _sobelX = {
        -1, 0, 1,
        -2, 0, 2,
        -1, 0, 1
    };

    private readonly float[] _sobelY = {
        -1, -2, -1,
         0,  0,  0,
         1,  2,  1
    };

    private readonly PerformanceTracker _performanceTracker;

    public ImageProcessor(PerformanceTracker? performanceTracker = null)
    {
        _performanceTracker = performanceTracker ?? new PerformanceTracker();
        
        // Try to use CUDA provider for MathNet.Numerics if available
        try
        {
            Control.UseNativeCUDA();
            Console.WriteLine("Using CUDA provider for matrix operations");
        }
        catch (Exception)
        {
            Console.WriteLine("CUDA provider not available, using managed provider");
        }
    }

    public YData DetectEdges(YData input)
    {
        const int distance = 1;
        const int edgeMargin = 10;

        var yData = input.MatrixData;

        // Create padded matrix with zeros around the edges to handle boundaries
        var padded = MatrixRowMajor<float>.BuildDense(yData.Width + (distance * 2), yData.Height + (distance * 2));
        padded.SetSubMatrix(distance, distance, yData);

        // Create shifted matrices for gradient calculation (these operations run on GPU when available)
        var rightShift = padded.SubMatrix(distance, yData.Width, distance * 2, yData.Height);
        var downShift = padded.SubMatrix(distance * 2, yData.Width, distance, yData.Height);
        var centerRegion = padded.SubMatrix(distance, yData.Width, distance, yData.Height);


        // Calculate X gradient using hardware acceleration
        var dx = rightShift.Subtract(centerRegion);

        // Calculate Y gradient using hardware acceleration
        var dy = downShift.Subtract(centerRegion);

        var output = dx.Add(dy);

        // Clear margin pixels around the edges
        var rowVector = Vector<float>.Build.Dense(output.Width, 0.0f);
        var colVector = Vector<float>.Build.Dense(output.Height, 0.0f);
        foreach (var rowIndex in Enumerable.Range(0, edgeMargin).Concat(Enumerable.Range(output.Height - edgeMargin, edgeMargin)))
        {
            output.SetRow(rowIndex, rowVector);
        }
        foreach (var colIndex in Enumerable.Range(0, edgeMargin).Concat(Enumerable.Range(output.Width - edgeMargin, edgeMargin)))
        {
            output.SetColumn(colIndex, colVector);
        }

        // Scale output to a suitable range
        // output = output
        //     .Divide(2.0f)
        //     .Multiply(4.0f);

        // Restrict the output values to be in the 0-1 range
        var result = output
            .Add(YData.MAX_PIXEL_VALUE / 2.0f);
            // .PointwiseMaximum(0f)
            // .PointwiseMinimum(YData.MAX_PIXEL_VALUE);

        // YData.SaveBitmapToFile(padded, "D:\\temp\\logo\\padded.png");
        // YData.SaveBitmapToFile(rightShift, "D:\\temp\\logo\\right.png");
        // YData.SaveBitmapToFile(downShift, "D:\\temp\\logo\\down.png");
        // YData.SaveBitmapToFile(centerRegion, "D:\\temp\\logo\\center.png");
        // YData.SaveBitmapToFile(dx, "D:\\temp\\logo\\dx.png");
        // YData.SaveBitmapToFile(dy, "D:\\temp\\logo\\dy.png");
        // YData.SaveBitmapToFile(output, "D:\\temp\\logo\\output.png");
        // YData.SaveBitmapToFile(result, "D:\\temp\\logo\\result.png");

        return new YData(result);
    }

    public bool IsSceneChange(YData prevData, YData currData, double threshold)
    {
        return CalculateSceneChangeAmount(prevData, currData) > threshold;
    }

    public double CalculateSceneChangeAmount(YData prevData, YData currData)
    {
        if (prevData.Width != currData.Width || prevData.Height != currData.Height) 
            return 0.0;

        // Calculate absolute difference using hardware acceleration
        var diff = prevData.MatrixData.Subtract(currData.MatrixData);
        var diffAbs = diff.PointwiseAbs();
        var diffSum = diffAbs.ColumnSums().Sum();
        var totalPixels = prevData.Width * prevData.Height;

        return diffSum / totalPixels;
    }

    public float CompareEdgeData(MatrixRowMajor<float> reference, MatrixRowMajor<float> current)
    {
        // Move matrix values to a zero centered range
        reference = reference.Subtract(YData.MAX_PIXEL_VALUE / 2.0f);
        current = current.Subtract(YData.MAX_PIXEL_VALUE / 2.0f);

        var currentSignal = reference.PointwiseMultiply(current).PointwiseAbs().Enumerate().Sum();
        var maxSignal = reference.PointwiseMultiply(reference).PointwiseAbs().Enumerate().Sum();

        return currentSignal / maxSignal;
    }

    public float CompareEdgeData(MatrixRowMajor<float> reference, MatrixRowMajor<float> current, Rectangle boundingRect)
    {
        // Extract submatrices based on the bounding rectangle
        var refSubMatrix = reference.SubMatrix(
            boundingRect.Left,
            boundingRect.Width,
            boundingRect.Top,
            boundingRect.Height);
        
        var currentSubMatrix = current.SubMatrix(
            boundingRect.Left,
            boundingRect.Width,
            boundingRect.Top,
            boundingRect.Height);

        // Move matrix values to a zero centered range
        refSubMatrix = refSubMatrix.Subtract(YData.MAX_PIXEL_VALUE / 2.0f);
        currentSubMatrix = currentSubMatrix.Subtract(YData.MAX_PIXEL_VALUE / 2.0f);

        var currentSignal = refSubMatrix.PointwiseMultiply(currentSubMatrix).PointwiseAbs().Enumerate().Sum();
        var maxSignal = refSubMatrix.PointwiseMultiply(refSubMatrix).PointwiseAbs().Enumerate().Sum();

        return currentSignal / maxSignal;
    }
    
    public bool IsBlackFrame(YData data, double threshold)
    {
        // Calculate average luminance - closer to 0 means darker
        // Use accelerated sum calculation instead of Enumerate().Average()
        var matrixSum = data.MatrixData.ColumnSums().Sum();
        var totalPixels = data.Width * data.Height;
        var meanLuminance = matrixSum / totalPixels;

        // Calculate percentage of pixels that are very dark (below threshold)
        var darkPixelThreshold = (float)(threshold * YData.MAX_PIXEL_VALUE);
        
        // Use accelerated pointwise comparison instead of Enumerate().Count()
        var darkPixelMatrix = data.MatrixData.Map(pixel => pixel < darkPixelThreshold ? 1.0f : 0.0f);
        var darkPixelCount = darkPixelMatrix.ColumnSums().Sum();
        var darkPixelPercentage = darkPixelCount / totalPixels;

        // Frame is considered black if average luminance is very low and most pixels are dark
        return meanLuminance < darkPixelThreshold && darkPixelPercentage > 0.95;
    }

    public bool IsWhiteFrame(YData data, double threshold)
    {
        // Calculate average luminance - closer to YData.MAX_PIXEL_VALUE means brighter
        // Use accelerated sum calculation instead of Enumerate().Average()
        var matrixSum = data.MatrixData.ColumnSums().Sum();
        var totalPixels = data.Width * data.Height;
        var meanLuminance = matrixSum / totalPixels;

        // Calculate percentage of pixels that are very bright (above (1-threshold))
        var brightPixelThreshold = (float)((1.0 - threshold) * YData.MAX_PIXEL_VALUE);
        
        // Use accelerated pointwise comparison instead of Enumerate().Count()
        var brightPixelMatrix = data.MatrixData.Map(pixel => pixel > brightPixelThreshold ? 1.0f : 0.0f);
        var brightPixelCount = brightPixelMatrix.ColumnSums().Sum();
        var brightPixelPercentage = brightPixelCount / totalPixels;

        // Frame is considered white if average luminance is very high and most pixels are bright
        return meanLuminance > brightPixelThreshold && brightPixelPercentage > 0.95;
    }

    public (bool IsBlack, bool IsWhite, float MeanLuminance) IsBlackOrWhiteFrame(YData data, double threshold)
    {
        // GPU-accelerated implementation using MathNet.Numerics CUDA operations
        var matrix = data.MatrixData;
        var totalPixels = data.Width * data.Height;
        
        var darkPixelThreshold = (float)(threshold * YData.MAX_PIXEL_VALUE);
        var brightPixelThreshold = (float)((1.0 - threshold) * YData.MAX_PIXEL_VALUE);

        try
        {
            // Calculate mean luminance using GPU-accelerated column sums
            var meanLuminance = matrix.ColumnSums().Sum() / totalPixels;

            // Frame is considered black if average luminance is very low and most pixels are dark
            var isBlack = false;
            if (meanLuminance < darkPixelThreshold)
            {
                // GPU-accelerated element-wise comparison for dark pixels
                var darkMask = matrix.Map((pixel) => pixel < darkPixelThreshold ? 1.0f : 0.0f);
                var darkPixelCount = (int)darkMask.ColumnSums().Sum();
                var darkPixelPercentage = (float)darkPixelCount / totalPixels;
                isBlack = darkPixelPercentage > 0.95;
            }
            
            // Frame is considered white if average luminance is very high and most pixels are bright
            var isWhite = false;
            if (meanLuminance > brightPixelThreshold)
            {
                // GPU-accelerated element-wise comparison for bright pixels
                var brightMask = matrix.Map((pixel) => pixel > brightPixelThreshold ? 1.0f : 0.0f);
                var brightPixelCount = (int)brightMask.ColumnSums().Sum();
                var brightPixelPercentage = (float)brightPixelCount / totalPixels;
                isWhite = brightPixelPercentage > 0.95;
            }

            return (isBlack, isWhite, meanLuminance);
        }
        catch (Exception ex)
        {
            // Fallback to SIMD implementation if GPU operations fail
#if DEBUG
            Console.WriteLine($"GPU acceleration failed, falling back to SIMD: {ex}");
#else
            Console.WriteLine($"GPU acceleration failed, falling back to SIMD: {ex.Message}");
#endif
            return IsBlackOrWhiteFrameSIMD(data, threshold);
        }
    }
    
    private (bool IsBlack, bool IsWhite, float MeanLuminance) IsBlackOrWhiteFrameSIMD(YData data, double threshold)
    {
        var floatData = data.FloatData;
        var totalPixels = data.Width * data.Height;

        var darkPixelThreshold = (float)(threshold * YData.MAX_PIXEL_VALUE);
        var brightPixelThreshold = (float)((1.0 - threshold) * YData.MAX_PIXEL_VALUE);

        // Hardware-accelerated calculations using SIMD
        float meanLuminance = 0.0f;

        unsafe
        {
            fixed (float* dataPtr = floatData)
            {
                // Calculate mean luminance using SIMD
                for (int i = 0; i < totalPixels; i++)
                {
                    meanLuminance += dataPtr[i];
                }
                meanLuminance /= totalPixels;
            }
        }

        // Frame is considered black if average luminance is very low and most pixels are dark
        var isBlack = false;
        if (meanLuminance < darkPixelThreshold)
        {
            int darkPixelCount = 0;
            unsafe
            {
                fixed (float* dataPtr = floatData)
                {
                    // Create threshold masks and count pixels using pure SIMD operations
                    var darkThresholdVector = new System.Numerics.Vector<float>(darkPixelThreshold);
                    var oneVector = System.Numerics.Vector<float>.One;

                    int vectorSize = System.Numerics.Vector<float>.Count;
                    int vectorizedLength = (totalPixels / vectorSize) * vectorSize;

                    var darkCountVector = System.Numerics.Vector<float>.Zero;

                    // Process vectors using pure SIMD operations
                    for (int i = 0; i < vectorizedLength; i += vectorSize)
                    {
                        var dataVector = new System.Numerics.Vector<float>(new ReadOnlySpan<float>(dataPtr + i, vectorSize));

                        // Create mask for dark pixels
                        var darkMask = System.Numerics.Vector.LessThan(dataVector, darkThresholdVector);

                        // Convert mask to float counts using conditional select (branchless)
                        darkCountVector += System.Numerics.Vector.ConditionalSelect(darkMask, oneVector, System.Numerics.Vector<float>.Zero);
                    }

                    // Sum the vector elements to get final count
                    for (int i = 0; i < vectorSize; i++)
                    {
                        darkPixelCount += (int)darkCountVector[i];
                    }

                    // Handle remaining elements
                    for (int i = vectorizedLength; i < totalPixels; i++)
                    {
                        if (dataPtr[i] < darkPixelThreshold) darkPixelCount++;
                    }
                }
            }
            var darkPixelPercentage = (float)darkPixelCount / totalPixels;
            isBlack = darkPixelPercentage > 0.95;
        }

        // Frame is considered white if average luminance is very high and most pixels are bright
        var isWhite = false;
        if (meanLuminance > brightPixelThreshold)
        {
            int brightPixelCount = 0;
            unsafe
            {
                fixed (float* dataPtr = floatData)
                {
                    // Create threshold masks and count pixels using pure SIMD operations
                    var brightThresholdVector = new System.Numerics.Vector<float>(brightPixelThreshold);
                    var oneVector = System.Numerics.Vector<float>.One;

                    int vectorSize = System.Numerics.Vector<float>.Count;
                    int vectorizedLength = (totalPixels / vectorSize) * vectorSize;

                    var brightCountVector = System.Numerics.Vector<float>.Zero;

                    // Process vectors using pure SIMD operations
                    for (int i = 0; i < vectorizedLength; i += vectorSize)
                    {
                        var dataVector = new System.Numerics.Vector<float>(new ReadOnlySpan<float>(dataPtr + i, vectorSize));

                        // Create mask for bright pixels
                        var brightMask = System.Numerics.Vector.GreaterThan(dataVector, brightThresholdVector);

                        // Convert mask to float counts using conditional select (branchless)
                        brightCountVector += System.Numerics.Vector.ConditionalSelect(brightMask, oneVector, System.Numerics.Vector<float>.Zero);
                    }

                    // Sum the vector elements to get final count
                    for (int i = 0; i < vectorSize; i++)
                    {
                        brightPixelCount += (int)brightCountVector[i];
                    }

                    // Handle remaining elements
                    for (int i = vectorizedLength; i < totalPixels; i++)
                    {
                        if (dataPtr[i] > brightPixelThreshold) brightPixelCount++;
                    }
                }
            }
            var brightPixelPercentage = (float)brightPixelCount / totalPixels;
            isWhite = brightPixelPercentage > 0.95;
        }

        return (isBlack, isWhite, meanLuminance);
    }

}
