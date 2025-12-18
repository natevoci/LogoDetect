using MathNet.Numerics.LinearAlgebra;

namespace LogoDetect.Services;

/// <summary>
/// A wrapper around MathNet.Numerics Matrix that provides a width/height coordinate system
/// instead of row/column, abstracting away the underlying column-major storage.
/// </summary>
public class MatrixRowMajor<T> where T : struct, IEquatable<T>, IFormattable
{
    private readonly Matrix<T> _underlying;

    public MatrixRowMajor(Matrix<T> underlying)
    {
        _underlying = underlying;
    }

    // Static factory methods
    public static MatrixRowMajor<T> BuildDense(int width, int height)
    {
        return new MatrixRowMajor<T>(Matrix<T>.Build.Dense(width, height));
    }

    public static MatrixRowMajor<T> BuildDense(int width, int height, T[] data)
    {
        return new MatrixRowMajor<T>(Matrix<T>.Build.Dense(width, height, data));
    }

    public static MatrixRowMajor<T> BuildDense(int width, int height, T value)
    {
        return new MatrixRowMajor<T>(Matrix<T>.Build.Dense(width, height, value));
    }

    // Dimensions using width/height terminology
    // Underlying is column-major with rows=height, cols=width
    public int Width => _underlying.RowCount;
    public int Height => _underlying.ColumnCount;

    public T this[int x, int y]
    {
        get => _underlying[x, y];
        set => _underlying[x, y] = value;
    }

    // Matrix operations
    public MatrixRowMajor<T> Add(MatrixRowMajor<T> other)
    {
        return new MatrixRowMajor<T>(_underlying.Add(other._underlying));
    }

    public MatrixRowMajor<T> Add(T scalar)
    {
        return new MatrixRowMajor<T>(_underlying.Add(scalar));
    }

    public MatrixRowMajor<T> Subtract(MatrixRowMajor<T> other)
    {
        return new MatrixRowMajor<T>(_underlying.Subtract(other._underlying));
    }

    public MatrixRowMajor<T> Subtract(T scalar)
    {
        return new MatrixRowMajor<T>(_underlying.Subtract(scalar));
    }

    public MatrixRowMajor<T> Divide(T divisor)
    {
        return new MatrixRowMajor<T>(_underlying.Divide(divisor));
    }

    public MatrixRowMajor<T> Clone()
    {
        return new MatrixRowMajor<T>(_underlying.Clone());
    }

    public MatrixRowMajor<T> SubMatrix(int xStart, int width, int yStart, int height)
    {
        return new MatrixRowMajor<T>(_underlying.SubMatrix(xStart, width, yStart, height));
    }

    public void SetSubMatrix(int xStart, int yStart, MatrixRowMajor<T> subMatrix)
    {
        _underlying.SetSubMatrix(xStart, yStart, subMatrix._underlying);
    }

    public void SetSubMatrix(int xStart, int width, int yStart, int height, MatrixRowMajor<T> subMatrix)
    {
        _underlying.SetSubMatrix(xStart, width, yStart, height, subMatrix._underlying);
    }

    public void SetRow(int y, Vector<T> value)
    {
        _underlying.SetColumn(y, value);
    }

    public void SetColumn(int x, Vector<T> vector)
    {
        _underlying.SetRow(x, vector);
    }

    // Aggregation methods
    public Vector<T> RowSums()
    {
        return _underlying.ColumnSums();
    }

    public Vector<T> ColumnSums()
    {
        return _underlying.RowSums();
    }

    // Passthrough methods that don't depend on orientation
    public MatrixRowMajor<T> PointwiseMultiply(MatrixRowMajor<T> other)
    {
        return new MatrixRowMajor<T>(_underlying.PointwiseMultiply(other._underlying));
    }

    public MatrixRowMajor<T> PointwiseAbs()
    {
        return new MatrixRowMajor<T>(_underlying.PointwiseAbs());
    }

    public MatrixRowMajor<T> Map(Func<T, T> f)
    {
        return new MatrixRowMajor<T>(_underlying.Map(f));
    }

    public IEnumerable<T> Enumerate()
    {
        return _underlying.Enumerate();
    }

    // Array conversion methods
    public T[] ToRowMajorArray()
    {
        return _underlying.AsColumnMajorArray() ?? _underlying.ToColumnMajorArray();
    }

    public T[] ToColumnMajorArray()
    {
        return _underlying.AsRowMajorArray() ?? _underlying.ToRowMajorArray();
    }
}
