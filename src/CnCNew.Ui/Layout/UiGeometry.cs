namespace CnCNew.Ui.Layout;

/// <summary>The display area the UI is laid out on, in pixels.</summary>
public readonly record struct UiViewport
{
    public UiViewport(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public UiRect Bounds => new(0, 0, Width, Height);
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>An integer screen rectangle. Containment is half-open: <c>[X, X + Width) x [Y, Y + Height)</c>.</summary>
public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    public static UiRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    public bool Intersects(UiRect other)
        => !IsEmpty && !other.IsEmpty && X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public UiRect Intersect(UiRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? new UiRect(left, top, 0, 0) : FromEdges(left, top, right, bottom);
    }

    public bool IsInside(UiRect outer) => X >= outer.X && Y >= outer.Y && Right <= outer.Right && Bottom <= outer.Bottom;

    public UiRect Inset(int amount) => FromEdges(X + amount, Y + amount, Math.Max(X + amount, Right - amount), Math.Max(Y + amount, Bottom - amount));

    /// <summary>Grows the rectangle symmetrically to at least <paramref name="minimum"/> pixels per axis (extra pixel to the right/bottom).</summary>
    public UiRect ExpandTo(int minimum)
    {
        var left = X;
        var right = Right;
        var top = Y;
        var bottom = Bottom;
        if (Width < minimum)
        {
            var missing = minimum - Width;
            left -= missing / 2;
            right += missing - missing / 2;
        }
        if (Height < minimum)
        {
            var missing = minimum - Height;
            top -= missing / 2;
            bottom += missing - missing / 2;
        }
        return FromEdges(left, top, right, bottom);
    }

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}
