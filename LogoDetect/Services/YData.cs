using SkiaSharp;
using System.Runtime.InteropServices;
using MathNet.Numerics.LinearAlgebra;
using System.IO;

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

    public void SaveToCSV(string path)
    {
        using var writer = new StreamWriter(path);
        
        // Write header with dimensions
        writer.WriteLine($"{_height},{_width}");
        
        // Write matrix data row by row
        for (int i = 0; i < _height; i++)
        {
            var row = new string[_width];
            for (int j = 0; j < _width; j++)
            {
                row[j] = _matrixData[i, j].ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            }
            writer.WriteLine(string.Join(",", row));
        }
    }

    public static YData LoadFromCSV(string path)
    {
        using var reader = new StreamReader(path);

        // Read dimensions from header
        var dimensions = reader.ReadLine()?.Split(',');
        if (dimensions?.Length != 2 || 
            !int.TryParse(dimensions[0], out int height) || 
            !int.TryParse(dimensions[1], out int width))
        {
            throw new FormatException("Invalid CSV format: First line should contain height,width");
        }

        // Create matrix to hold the data
        var matrix = Matrix<float>.Build.Dense(height, width);

        // Read each row
        for (int i = 0; i < height; i++)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                throw new FormatException($"Invalid CSV format: Missing data at row {i}");
            }

            var values = line.Split(',');
            if (values.Length != width)
            {
                throw new FormatException($"Invalid CSV format: Row {i} has {values.Length} values, expected {width}");
            }

            for (int j = 0; j < width; j++)
            {
                if (!float.TryParse(values[j], System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    throw new FormatException($"Invalid CSV format: Could not parse value at row {i}, column {j}");
                }
                matrix[i, j] = value;
            }
        }

        return new YData(matrix);
    }

    public static void SaveMatrixToCSV(Matrix<float> matrix, string path)
    {
        var yData = new YData(matrix);
        yData.SaveToCSV(path);
    }
}
