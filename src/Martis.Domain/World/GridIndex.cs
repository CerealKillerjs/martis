namespace Martis.Domain.World;

/// <summary>
/// A cell coordinate on a grid whose width and height come from the caller (H-26).
/// The linear address is <c>y * width + x</c>. This type does not keep a fixed map size.
/// </summary>
public readonly record struct GridIndex
{
    public GridIndex(int x, int y, int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        }

        if (x < 0 || x >= width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "X is outside the grid.");
        }

        if (y < 0 || y >= height)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Y is outside the grid.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Linear index <c>y * width + x</c>. Throws <see cref="OverflowException"/>
    /// when the product does not fit in <see cref="int"/>.
    /// </summary>
    public int ToLinearIndex() => checked((Y * Width) + X);
}
