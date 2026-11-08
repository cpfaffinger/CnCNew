using CnCNew.Images;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace CnCNew.Client.Stride;

/// <summary>
/// What the window shows from its first frame until the menu (or the intro) is ready: the picture the original's boot load screen uses (<c>LoadPageHuge</c>, the file
/// <c>Art/Textures/loadpageuserinterface.tga</c> of the user's own content, never a file shipped with the client) kept in its aspect on black, and a thin bar that fills as
/// the game data loads. When the picture cannot be read the screen is plain dark with the bar, which is still something alive. The bar's fill is <see cref="Progress"/>, written by the
/// loading thread.
/// </summary>
internal sealed class StartupSplash : IDisposable
{
    private const string PicturePath = "Art/Textures/loadpageuserinterface.tga";

    // LoadPageHuge: the picture is the 800 x 600 corner of a 1024 x 1024 page (Data/INI/MappedImages/HandCreated).
    private const int PictureWidth = 800;
    private const int PictureHeight = 600;

    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _batch;
    private readonly Texture? _picture;
    private readonly Texture _white;
    private volatile int _progressPercent;

    public StartupSplash(GraphicsDevice device, string contentDirectory)
    {
        _device = device;
        _batch = new SpriteBatch(device);
        _white = Texture.New2D(device, 1, 1, PixelFormat.R8G8B8A8_UNorm, new byte[] { 255, 255, 255, 255 }, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable);
        try
        {
            var file = Path.Combine(contentDirectory, PicturePath);
            if (File.Exists(file))
            {
                using var stream = File.OpenRead(file);
                var tga = TgaReader.Read(stream);
                _picture = Texture.New2D(device, tga.Width, tga.Height, PixelFormat.R8G8B8A8_UNorm, tga.RgbaPixels, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable);
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: the start screen's picture cannot be read ({exception.Message}); showing a plain screen.");
        }
    }

    /// <summary>True when the original's picture is drawn (false: the plain screen).</summary>
    public bool HasPicture => _picture is not null;

    /// <summary>0 to 100: how far the loading has come (it may be written from another thread).</summary>
    public int Progress
    {
        get => _progressPercent;
        set => _progressPercent = Math.Clamp(value, 0, 100);
    }

    /// <summary>Draws one frame into the back buffer: black, the picture in its aspect, the bar.</summary>
    public void Draw(GraphicsContext context)
    {
        var back = _device.Presenter.BackBuffer;
        var commandList = context.CommandList;
        commandList.SetRenderTargetAndViewport(null, back);
        commandList.Clear(back, Color.Black);
        _batch.Begin(context, SpriteSortMode.Deferred, BlendStates.NonPremultiplied, _device.SamplerStates.LinearClamp, DepthStencilStates.None, RasterizerStates.CullNone);
        var width = back.Width;
        var height = back.Height;
        if (_picture is not null)
        {
            var scale = Math.Min(width / (float)PictureWidth, height / (float)PictureHeight);
            var drawn = new Vector2(PictureWidth * scale, PictureHeight * scale);
            var at = new Vector2((width - drawn.X) / 2f, (height - drawn.Y) / 2f);
            var source = new RectangleF(0, 0, PictureWidth * _picture.Width / 1024f - 1f, PictureHeight * _picture.Height / 1024f - 1f);
            _batch.Draw(_picture, new RectangleF(at.X, at.Y, drawn.X, drawn.Y), source, Color.White, 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f);
        }
        else
        {
            _batch.Draw(_white, new RectangleF(0, 0, width, height), null, new Color(16, 18, 22, 255), 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f);
        }
        var barHeight = Math.Max(3, height / 180);
        var barWidth = width * 0.4f;
        var left = (width - barWidth) / 2f;
        var top = height - 3f * barHeight;
        _batch.Draw(_white, new RectangleF(left, top, barWidth, barHeight), null, new Color(40, 40, 40, 200), 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f);
        _batch.Draw(_white, new RectangleF(left, top, barWidth * _progressPercent / 100f, barHeight), null, new Color(230, 200, 90, 255), 0f, Vector2.Zero, SpriteEffects.None, ImageOrientation.AsIs, 0f);
        _batch.End();
    }

    public void Dispose()
    {
        _batch.Dispose();
        _picture?.Dispose();
        _white.Dispose();
    }
}
