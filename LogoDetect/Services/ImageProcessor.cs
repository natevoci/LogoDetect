using SkiaSharp;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using System.Runtime.InteropServices;

using MathNet.Numerics;
using MathNet.Numerics.Providers.Common;

namespace LogoDetect.Services;

public class ImageProcessor
{    public ImageProcessor()
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
    private readonly float[] _sobelX = new float[]
    {
        -1, 0, 1,
        -2, 0, 2,
        -1, 0, 1
    };

    private readonly float[] _sobelY = new float[]
    {
        -1, -2, -1,
         0,  0,  0,
         1,  2,  1
    };

    public SKBitmap DetectEdges(SKBitmap input)
    {
        var width = input.Width;
        var height = input.Height;
        var output = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        
        // Convert to grayscale and apply Sobel operator
        var grayscale = new byte[width * height];
        var pixels = input.Pixels;
        
        for (int i = 0; i < pixels.Length; i++)
        {
            var color = pixels[i];
            grayscale[i] = (byte)((color.Red * 0.299 + color.Green * 0.587 + color.Blue * 0.114));
        }

        var gradientMagnitude = new byte[width * height];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                float gx = 0, gy = 0;

                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        int pixelIndex = (y + i) * width + (x + j);
                        float pixelValue = grayscale[pixelIndex];
                        
                        int kernelIndex = (i + 1) * 3 + (j + 1);
                        gx += pixelValue * _sobelX[kernelIndex];
                        gy += pixelValue * _sobelY[kernelIndex];
                    }
                }

                float magnitude = (float)Math.Sqrt(gx * gx + gy * gy);
                gradientMagnitude[y * width + x] = (byte)Math.Min(255, magnitude);
            }
        }

        Marshal.Copy(gradientMagnitude, 0, output.GetPixels(), gradientMagnitude.Length);
        return output;
    }

    public SKBitmap GenerateLogoReference(SKBitmap[] edgeDetectedFrames)
    {
        var width = edgeDetectedFrames[0].Width;
        var height = edgeDetectedFrames[0].Height;
        var pixelCount = width * height;
        var accumulator = new double[pixelCount];

        foreach (var frame in edgeDetectedFrames)
        {
            var pixels = frame.Pixels;
            for (int i = 0; i < pixelCount; i++)
            {
                accumulator[i] += pixels[i].Red; // Since we're using grayscale, we can use any channel
            }
        }

        // Calculate average
        var output = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        var avgPixels = new byte[pixelCount];
        
        for (int i = 0; i < pixelCount; i++)
        {
            avgPixels[i] = (byte)(accumulator[i] / edgeDetectedFrames.Length);
        }

        Marshal.Copy(avgPixels, 0, output.GetPixels(), avgPixels.Length);
        return output;
    }

    public double CompareImages(SKBitmap reference, SKBitmap target)
    {
        var width = reference.Width;
        var height = reference.Height;
        var referencePixels = reference.Pixels;
        var targetPixels = target.Pixels;
        
        var diffSum = 0.0;
        var pixelCount = width * height;

        for (int i = 0; i < pixelCount; i++)
        {
            var diff = Math.Abs(referencePixels[i].Red - targetPixels[i].Red);
            diffSum += diff;
        }

        return diffSum / (pixelCount * 255.0); // Normalize to 0-1 range
    }

    public bool IsSceneChange(SKBitmap previous, SKBitmap current, double threshold)
    {
        return CompareImages(previous, current) > threshold;
    }
}
