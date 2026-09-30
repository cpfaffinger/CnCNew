namespace CnCNew.Ui.Rendering;

/// <summary>A rectangle of an atlas page in texels.</summary>
public readonly record struct AtlasRegion(int Page, int X, int Y, int Width, int Height);

/// <summary>Receives the pixels of the atlas; the Stride renderer backs it with textures, tests with memory.</summary>
public interface IAtlasPageSink
{
    void CreatePage(int page, int width, int height);

    /// <summary>Copies tightly packed RGBA8 pixels (<paramref name="width"/> x <paramref name="height"/>) to the page.</summary>
    void Upload(int page, int x, int y, int width, int height, byte[] rgba);
}

/// <summary>
/// Shelf-packed RGBA8 atlas pages. Every page starts with a solid white block in its top-left corner
/// (<see cref="WhiteSize"/> x <see cref="WhiteSize"/>) so fills can share the texture of the text around them and need no
/// extra draw call. Regions keep <see cref="Padding"/> empty texels between each other, which also protects against
/// bilinear bleeding. Packing is deterministic: same requests in the same order give the same placement.
/// </summary>
public sealed class UiGlyphAtlas
{
    public const int WhiteSize = 4;
    public const int Padding = 1;

    private readonly IAtlasPageSink _sink;
    private readonly List<Page> _pages = [];

    public UiGlyphAtlas(int pageSize, IAtlasPageSink sink)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 64);
        ArgumentNullException.ThrowIfNull(sink);
        PageSize = pageSize;
        _sink = sink;
    }

    public int PageSize { get; }
    public int PageCount => _pages.Count;

    /// <summary>The white block on <paramref name="page"/>; sample its inner texels for a solid color.</summary>
    public AtlasRegion White(int page = 0)
    {
        if (_pages.Count == 0) AddPage();
        return new AtlasRegion(page, 0, 0, WhiteSize, WhiteSize);
    }

    /// <summary>Packs an image and uploads it; larger than a page is an error (never clipped or scaled silently).</summary>
    public AtlasRegion Add(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != width * height * 4) throw new ArgumentException("Pixel data does not match the size.", nameof(rgba));
        if (width + Padding > PageSize || height + WhiteSize + 2 * Padding > PageSize)
            throw new ArgumentOutOfRangeException(nameof(width), $"A {width}x{height} image does not fit an atlas page of {PageSize}x{PageSize}.");
        if (_pages.Count == 0) AddPage();
        for (var index = 0; index < _pages.Count; index++)
            if (_pages[index].TryPlace(width, height, out var x, out var y))
                return Upload(index, x, y, width, height, rgba);
        AddPage();
        var last = _pages.Count - 1;
        if (!_pages[last].TryPlace(width, height, out var px, out var py))
            throw new InvalidOperationException("A fresh atlas page could not hold an image that passed the size check.");
        return Upload(last, px, py, width, height, rgba);
    }

    private AtlasRegion Upload(int page, int x, int y, int width, int height, byte[] rgba)
    {
        _sink.Upload(page, x, y, width, height, rgba);
        return new AtlasRegion(page, x, y, width, height);
    }

    private void AddPage()
    {
        var index = _pages.Count;
        _sink.CreatePage(index, PageSize, PageSize);
        var white = new byte[WhiteSize * WhiteSize * 4];
        Array.Fill(white, (byte)255);
        _sink.Upload(index, 0, 0, WhiteSize, WhiteSize, white);
        _pages.Add(new Page(PageSize));
    }

    /// <summary>
    /// One shelf page. The white block occupies the first shelf's left end; regions are placed left to right on shelves whose
    /// height is fixed by their first member, a new shelf opens below the tallest one when a row is full.
    /// </summary>
    private sealed class Page
    {
        private readonly int _size;
        private readonly List<(int Y, int Height, int NextX)> _shelves = [];
        private int _nextShelfY;

        public Page(int size)
        {
            _size = size;
            _shelves.Add((0, WhiteSize + Padding, WhiteSize + Padding));
            _nextShelfY = WhiteSize + Padding;
        }

        public bool TryPlace(int width, int height, out int x, out int y)
        {
            for (var i = 0; i < _shelves.Count; i++)
            {
                var shelf = _shelves[i];
                if (height <= shelf.Height && shelf.NextX + width + Padding <= _size)
                {
                    x = shelf.NextX;
                    y = shelf.Y;
                    _shelves[i] = shelf with { NextX = shelf.NextX + width + Padding };
                    return true;
                }
            }
            if (_nextShelfY + height + Padding <= _size)
            {
                y = _nextShelfY;
                x = 0;
                _shelves.Add((y, height, width + Padding));
                _nextShelfY += height + Padding;
                return true;
            }
            x = y = 0;
            return false;
        }
    }
}
