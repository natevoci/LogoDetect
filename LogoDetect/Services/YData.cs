using SkiaSharp;
using System.Runtime.InteropServices;
using MathNet.Numerics.LinearAlgebra;

namespace LogoDetect.Services;

public class YData
{
    private readonly Matrix<float> _matrixData;
    private readonly int _width;
    private readonly int _height;

    public Matrix<float> MatrixData => _matrixData;
    public int Width => _width;
    public int Height => _height;

    public YData(byte[] rawData, int width, int height)
    {
        _width = width;
        _height = height;
        // Convert byte array to float matrix
        _matrixData = Matrix<float>.Build.Dense(
            height,
            width,
            (i, j) => rawData[i * width + j]
        );
    }

    public YData(Matrix<float> matrix)
    {
        _width = matrix.ColumnCount;
        _height = matrix.RowCount;
        _matrixData = matrix;
    }

    public SKBitmap ToBitmap()
    {
        var bitmap = new SKBitmap(_width, _height, SKColorType.Gray8, SKAlphaType.Opaque);
        var bytes = new byte[_width * _height];

        // Convert float matrix back to bytes
        for (int i = 0; i < _height; i++)
        {
            for (int j = 0; j < _width; j++)
            {
                bytes[i * _width + j] = (byte)Math.Clamp(_matrixData[i, j], 0, 255);
            }
        }

        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                bitmap.SetPixels((IntPtr)ptr);
            }
        }
        return bitmap;
    }

    public static YData FromBitmap(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Gray8)
        {
            // Convert bitmap to Gray8 format if necessary
            using var grayBitmap = bitmap.Resize(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Gray8), SKFilterQuality.High);
            var data = new byte[grayBitmap.Width * grayBitmap.Height];
            Marshal.Copy(grayBitmap.GetPixels(), data, 0, data.Length);
            return new YData(data, grayBitmap.Width, grayBitmap.Height);
        }
        else
        {
            var data = new byte[bitmap.Width * bitmap.Height];
            Marshal.Copy(bitmap.GetPixels(), data, 0, data.Length);
            return new YData(data, bitmap.Width, bitmap.Height);
        }
    }

    public static void SaveBitmapToFile(Matrix<float> matrix, string path)
    {
        var yData = new YData(matrix);
        yData.SaveBitmapToFile(path);
    }

    public void SaveBitmapToFile(string path)
    {
        using var bitmap = ToBitmap();
        using var stream = File.Create(path);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    public static YData LoadFromFile(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return FromBitmap(bitmap);
    }
}
