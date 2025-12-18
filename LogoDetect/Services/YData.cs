using SkiaSharp;
using System.Runtime.InteropServices;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using System.Drawing;
using System.IO;

namespace LogoDetect.Services;

public class YData
{
    public const float MAX_PIXEL_VALUE = 1.0f;

    private readonly MatrixRowMajor<float> _matrixData;
    private readonly float[] _floatData;
    private readonly int _width;
    private readonly int _height;
    private Rectangle _boundingRect;

    public MatrixRowMajor<float> MatrixData => _matrixData;
    public float[] FloatData => _floatData;
    public int Width => _width;
    public int Height => _height;

    public Rectangle BoundingRect
    {
        get => _boundingRect;
        set => _boundingRect = value;
    }

    public YData(float[] floatData, int width, int height, int stride)
    {
        _width = width;
        _height = height;
        _floatData = floatData;

        _matrixData = MatrixRowMajor<float>.BuildDense(stride, height, floatData);
        
        if (stride > width)
        {
            _matrixData = _matrixData.SubMatrix(0, width, 0, height);
        }
    }

    public YData(MatrixRowMajor<float> matrix)
    {
        _width = matrix.Width;
        _height = matrix.Height;
        _matrixData = matrix;
        
        // Extract float data from matrix
        _floatData = matrix.ToRowMajorArray();
    }

    public SKBitmap ToBitmap()
    {
        var bitmap = new SKBitmap(_width, _height, SKColorType.Gray8, SKAlphaType.Opaque);
        var bytes = new byte[_width * _height];

        // Convert float matrix back to bytes
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                bytes[y * _width + x] = (byte)Math.Clamp(_matrixData[x, y] * 255.0f, 0, 255);
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

    public static void SaveBitmapToFile(MatrixRowMajor<float> matrix, string path)
    {
        var yData = new YData(matrix);
        yData.SaveBitmapToFile(path);
    }

    public static void SaveBitmapToFile(MatrixRowMajor<float> matrix, string path, Action<string>? debugFileTracker = null)
    {
        var yData = new YData(matrix);
        yData.SaveBitmapToFile(path);
        debugFileTracker?.Invoke(path);
    }

    public void SaveBitmapToFile(string path)
    {
        using var bitmap = ToBitmap();
        using var stream = File.Create(path);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    public void SaveBitmapToFile(string path, Action<string>? debugFileTracker = null)
    {
        using var bitmap = ToBitmap();
        using var stream = File.Create(path);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
        debugFileTracker?.Invoke(path);
    }

    public void SaveBitmapToStream(Stream stream)
    {
        using var bitmap = ToBitmap();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }

    public void SaveToCSV(string path)
    {
        SaveToCSV(path, null);
    }

    public void SaveToCSV(string path, Action<string>? debugFileTracker = null)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine($"{_height},{_width}");
        for (int y = 0; y < _height; y++)
        {
            var row = new string[_width];
            for (int x = 0; x < _width; x++)
            {
                row[x] = (_matrixData[x, y] * 255.0f).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            }
            writer.WriteLine(string.Join(",", row));
        }
        if (_boundingRect != Rectangle.Empty)
        {
            writer.WriteLine($"BoundingRect,{_boundingRect.X},{_boundingRect.Y},{_boundingRect.Width},{_boundingRect.Height}");
        }
        debugFileTracker?.Invoke(path);
    }

    public static YData LoadFromCSV(string path)
    {
        using var reader = new StreamReader(path);
        var dimensions = reader.ReadLine()?.Split(',');
        if (dimensions?.Length != 2 ||
            !int.TryParse(dimensions[0], out int height) ||
            !int.TryParse(dimensions[1], out int width))
        {
            throw new FormatException("Invalid CSV format: First line should contain height,width");
        }
        var matrix = MatrixRowMajor<float>.BuildDense(width, height);
        for (int y = 0; y < height; y++)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                throw new FormatException($"Invalid CSV format: Missing data at row {y}");
            }
            var values = line.Split(',');
            if (values.Length != width)
            {
                throw new FormatException($"Invalid CSV format: Row {y} has {values.Length} values, expected {width}");
            }
            for (int x = 0; x < width; x++)
            {
                if (!float.TryParse(values[x], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    throw new FormatException($"Invalid CSV format: Could not parse value at row {y}, column {x}");
                }
                matrix[x, y] = value / 255.0f;
            }
        }
        var yData = new YData(matrix);
        
        var extraLine = reader.ReadLine();
        if (!string.IsNullOrEmpty(extraLine))
        {
            var parts = extraLine.Split(',');
            if (parts.Length == 5 && parts[0] == "BoundingRect" &&
                int.TryParse(parts[1], out int x) &&
                int.TryParse(parts[2], out int y) &&
                int.TryParse(parts[3], out int w) &&
                int.TryParse(parts[4], out int h))
            {
                yData.BoundingRect = new Rectangle(x, y, w, h);
            }
        }

        return yData;
    }
    

}
