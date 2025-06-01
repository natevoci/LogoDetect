using SkiaSharp;
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

    public Matrix<float> DetectEdges(Matrix<float> yData, int width, int height)
    {
        var result = Matrix<float>.Build.Dense(height, width);
        
        // Create matrices for Sobel operation
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                // Apply Sobel operators
                float gx = 0, gy = 0;
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        var val = yData[y + i, x + j];
                        var kernelIdx = (i + 1) * 3 + (j + 1);
                        
                        gx += val * _sobelX[kernelIdx];
                        gy += val * _sobelY[kernelIdx];
                    }
                }

                // Calculate gradient magnitude
                result[y, x] = (float)Math.Sqrt((gx * gx) + (gy * gy));
            }
        }

        return result;    }

    public bool IsSceneChange(byte[] prevYData, byte[] currYData, double threshold)
    {
        if (prevYData.Length != currYData.Length) return false;

        // Convert byte arrays to matrices for hardware-accelerated operations
        var prevMatrix = Matrix<float>.Build.Dense(prevYData.Length, 1, 
            (i, j) => prevYData[i]);
        
        var currMatrix = Matrix<float>.Build.Dense(currYData.Length, 1,
            (i, j) => currYData[i]);

        // Calculate absolute difference using hardware acceleration
        var diff = prevMatrix.Subtract(currMatrix).PointwiseAbs();
        var diffSum = diff.Enumerate().Sum();

        return (diffSum / (prevYData.Length * 255.0)) > threshold;
    }

    public bool IsSceneChange(Matrix<float> prevData, Matrix<float> currData, double threshold)
    {
        if (prevData.RowCount != currData.RowCount || prevData.ColumnCount != currData.ColumnCount) 
            return false;

        // Calculate absolute difference using hardware acceleration
        var diff = prevData.Subtract(currData).PointwiseAbs();
        var diffSum = diff.Enumerate().Sum();
        var totalPixels = prevData.RowCount * prevData.ColumnCount;

        return (diffSum / (totalPixels * 255.0)) > threshold;
    }

    public float CompareEdgeData(Matrix<float> reference, Matrix<float> edges)
    {
        // Use hardware-accelerated matrix subtraction and element-wise operations
        var diff = reference.Subtract(edges);
        return (float)diff.PointwiseAbs().Enumerate().Average();
    }
}
