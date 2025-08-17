using SkiaSharp;
using System.Drawing;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using System.Runtime.InteropServices;
using MathNet.Numerics;

namespace LogoDetect.Services;

public class ImageProcessor
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

    public ImageProcessor()
    {
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
        var padded = Matrix<float>.Build.Dense(yData.RowCount + (distance * 2), yData.ColumnCount + (distance * 2));
        padded.SetSubMatrix(distance, distance, yData);

        // Create shifted matrices for gradient calculation (these operations run on GPU when available)
        var rightShift = padded.SubMatrix(distance, yData.RowCount, distance * 2, yData.ColumnCount);
        var downShift = padded.SubMatrix(distance * 2, yData.RowCount, distance, yData.ColumnCount);
        var centerRegion = padded.SubMatrix(distance, yData.RowCount, distance, yData.ColumnCount);


        // Calculate X gradient using hardware acceleration
        var dx = rightShift.Subtract(centerRegion);

        // Calculate Y gradient using hardware acceleration
        var dy = downShift.Subtract(centerRegion);

        var output = dx.Add(dy);

        // Clear margin pixels around the edges
        var rowVector = Vector<float>.Build.Dense(output.ColumnCount, 0.0f);
        var colVector = Vector<float>.Build.Dense(output.RowCount, 0.0f);
        foreach (var rowIndex in Enumerable.Range(0, edgeMargin).Concat(Enumerable.Range(output.RowCount - edgeMargin, edgeMargin)))
        {
            output.SetRow(rowIndex, rowVector);
        }
        foreach (var colIndex in Enumerable.Range(0, edgeMargin).Concat(Enumerable.Range(output.ColumnCount - edgeMargin, edgeMargin)))
        {
            output.SetColumn(colIndex, colVector);
        }

        // Scale output to a suitable range
        // output = output
        //     .Divide(2.0f)
        //     .Multiply(4.0f);

        // Restrict the output values to be in the 0-255 range
        var result = output
            .Add(byte.MaxValue / 2.0f);
            // .PointwiseMaximum(byte.MinValue)
            // .PointwiseMinimum(byte.MaxValue);

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
        var diff = prevData.MatrixData.Subtract(currData.MatrixData).PointwiseAbs();
        var diffSum = diff.Enumerate().Sum();
        var totalPixels = prevData.Width * prevData.Height;

        return diffSum / (totalPixels * 255.0);
    }

    public float CompareEdgeData(Matrix<float> reference, Matrix<float> current)
    {
        // Move matrix values to a zero centered range
        reference = reference.Subtract(byte.MaxValue / 2.0f);
        current = current.Subtract(byte.MaxValue / 2.0f);

        var currentSignal = reference.PointwiseMultiply(current).PointwiseAbs().Enumerate().Sum();
        var maxSignal = reference.PointwiseMultiply(reference).PointwiseAbs().Enumerate().Sum();

        return currentSignal / maxSignal;
    }

    public float CompareEdgeData(Matrix<float> reference, Matrix<float> current, Rectangle boundingRect)
    {
        // Extract submatrices based on the bounding rectangle
        var refSubMatrix = reference.SubMatrix(
            boundingRect.Top, 
            boundingRect.Height, 
            boundingRect.Left, 
            boundingRect.Width);
        
        var currentSubMatrix = current.SubMatrix(
            boundingRect.Top, 
            boundingRect.Height, 
            boundingRect.Left, 
            boundingRect.Width);

        // Move matrix values to a zero centered range
        refSubMatrix = refSubMatrix.Subtract(byte.MaxValue / 2.0f);
        currentSubMatrix = currentSubMatrix.Subtract(byte.MaxValue / 2.0f);

        var currentSignal = refSubMatrix.PointwiseMultiply(currentSubMatrix).PointwiseAbs().Enumerate().Sum();
        var maxSignal = refSubMatrix.PointwiseMultiply(refSubMatrix).PointwiseAbs().Enumerate().Sum();

        return currentSignal / maxSignal;
    }
    
    public bool IsBlackFrame(YData data, double threshold)
    {
        // Calculate average luminance - closer to 0 means darker
        var meanLuminance = data.MatrixData.Enumerate().Average();

        // Calculate percentage of pixels that are very dark (below threshold * 255)
        var darkPixelThreshold = threshold * 255.0;
        var darkPixelCount = data.MatrixData.Enumerate().Count(x => x < darkPixelThreshold);
        var totalPixels = data.Width * data.Height;
        var darkPixelPercentage = darkPixelCount / (double)totalPixels;

        // Frame is considered black if average luminance is very low and most pixels are dark
        return meanLuminance < darkPixelThreshold && darkPixelPercentage > 0.95;
    }
}
