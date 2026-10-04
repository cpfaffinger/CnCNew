using System.Globalization;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Wnd;
using Stride.Graphics;

namespace CnCNew.Client.Stride.Ui;

/// <summary>
/// The mouse cursor as a sprite drawn last, on top of the UI: the decoded <c>Mouse.ini</c> cursors (<see cref="CursorSet"/>) with one texture per
/// frame, the animation clock (<see cref="CursorAnimator"/>) and the hot spot offset. The original lets Windows draw these same files
/// (<c>Win32Mouse::setCursor</c>); drawing them here keeps the cursor in captures and independent of the window system.
/// <see cref="Usable"/> is false when the plain arrow could not be decoded: the caller then keeps the operating system cursor.
/// A cursor without a picture (the retail data defines none for building placement, as in the original) draws nothing and is counted.
/// </summary>
internal sealed class SoftwareCursor : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Dictionary<(string File, int Frame), Texture> _textures = [];
    private readonly CursorAnimator _animator;
    private CursorFrame? _last;
    private int _drawn;
    private int _hidden;

    public SoftwareCursor(GraphicsDevice device, GameData data)
    {
        _device = device;
        Cursors = data.LoadCursors();
        _animator = new CursorAnimator(Cursors);
        Usable = Cursors.Resolve(CursorState.Arrow, 0, out _);
        if (!Usable)
            Console.Error.WriteLine($"warning: the software mouse cursor is unavailable (the arrow cursor of Mouse.ini could not be loaded: {Cursors.Report()}); keeping the operating system cursor.");
    }

    public CursorSet Cursors { get; }
    public bool Usable { get; }

    /// <summary>Draws the cursor for <paramref name="state"/> with its hot spot on <paramref name="position"/>; call inside the renderer's frame, after everything else.</summary>
    public void Draw(UiRenderer renderer, CursorState state, Vector2 position, long nowMilliseconds)
    {
        if (!Usable) return;
        if (!_animator.Update(state, nowMilliseconds, out var frame))
        {
            _last = null;
            _hidden++;
            return;
        }
        _last = frame;
        var image = frame.Image;
        if (!_textures.TryGetValue((frame.Cursor.File, frame.FrameIndex), out var texture))
        {
            // Plain UNorm like every other UI texture (see UiImageTextures.Format): the sprite shader does the sRGB handling.
            texture = Texture.New2D(_device, image.Width, image.Height, PixelFormat.R8G8B8A8_UNorm, image.RgbaPixels, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable);
            _textures.Add((frame.Cursor.File, frame.FrameIndex), texture);
        }
        var x = (int)MathF.Round(position.X) - image.HotspotX;
        var y = (int)MathF.Round(position.Y) - image.HotspotY;
        renderer.DrawTexture(texture, new UiRect(x, y, image.Width, image.Height), null, WndColor.White);
        _drawn++;
    }

    /// <summary>The cursor line of the console report: what was loaded, what is missing, and what the last drawn frame showed.</summary>
    public string Report()
    {
        var current = _last is { } frame
            ? string.Create(CultureInfo.InvariantCulture, $"{CursorNames.IniName(frame.Kind)} ({frame.Cursor.File} frame {frame.FrameIndex + 1}/{frame.Cursor.Animation.Frames.Length}, hot spot {frame.Image.HotspotX},{frame.Image.HotspotY})")
            : _drawn + _hidden == 0 ? "none (no pointer position yet)" : $"{CursorNames.IniName(_animator.State.Kind)} (no picture)";
        var unresolved = string.Join(", ", _animator.Unresolved.Select(pair => $"{CursorNames.IniName(pair.Key)} x{pair.Value}"));
        return string.Create(CultureInfo.InvariantCulture,
            $"{(Usable ? "software" : "operating system (fallback)")}; {Cursors.Report()}; {_drawn} frames drawn, {_hidden} without a picture, unresolved [{unresolved}]; current {current}");
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
    }
}
