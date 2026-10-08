using System.Numerics;
using CnCNew.Data;

namespace CnCNew.Presentation.Input;

/// <summary>
/// Camera input tuning. Defaults follow <c>LookAtXlat.cpp</c> and the retail <c>GameData.ini</c>: a 3 pixel edge band (<c>edgeScrollSize</c>),
/// <c>HorizontalScrollSpeedFactor = 1.6</c>, <c>VerticalScrollSpeedFactor = 2.0</c>,
/// <c>KeyboardCameraRotateSpeed = 0.1</c> rad per logic frame, 0.01 rad per pixel of middle-button drag, 10 height units per wheel detent
/// (<c>ZoomHeightPerSecond</c>) and a 5 pixel middle-click limit for the reset click.
/// <para><see cref="KeyboardScrollSpeedFactor"/> is the original's <c>m_keyboardScrollFactor</c> as the game actually runs with it: the
/// <c>KeyboardScrollSpeedFactor = 2.0</c> of GameData.ini never takes effect, because <c>GlobalData::parseGameDataDefinition</c> overwrites it right after
/// parsing with <c>OptionPreferences::getScrollFactor</c> - the scroll speed slider (0..100) divided by 100, and
/// <c>m_keyboardDefaultScrollFactor = 0.5</c> when the player never touched it. Keys and the screen edge then scroll by
/// <c>speedFactor * SCROLL_AMT * m_keyboardScrollFactor</c> per client frame (<c>LookAtTranslator</c>, <c>MSG_FRAME_TICK</c>).</para>
/// </summary>
public sealed record CameraSettings(
    bool EdgeScrollEnabled = true,
    float EdgeScrollMarginPixels = 3f,
    float HorizontalScrollSpeedFactor = 1.6f,
    float VerticalScrollSpeedFactor = 2.0f,
    float KeyboardScrollSpeedFactor = CameraSettings.DefaultScrollOption,
    bool RightDragScrolls = true,
    float KeyboardRotateRadiansPerFrame = 0.1f,
    float DragRotateRadiansPerPixel = 0.01f,
    float ZoomPerWheelNotch = 10f,
    float KeyboardZoomPerFrame = 10f,
    float MiddleClickResetPixels = 5f,
    float FramesPerSecond = OriginalFrame.PerSecond)
{
    /// <summary><c>m_keyboardDefaultScrollFactor</c> (GlobalData.cpp): the scroll speed option of a player who never moved the slider.</summary>
    public const float DefaultScrollOption = 0.5f;
}

/// <summary>
/// What the camera should do this frame, as plain data for the camera owner. <see cref="Scroll"/> is camera-relative (x right, y forward) and
/// dimensionless: 1 means the camera's base scroll speed for one second, scaled by the speed factors. <see cref="RotateDelta"/> is radians
/// (positive counter-clockwise seen from above), <see cref="ZoomDelta"/> height units (positive zooms out). <see cref="CenterOn"/> is a world
/// ground position (x, y).
/// </summary>
public readonly record struct CameraIntent(Vector2 Scroll, float RotateDelta, float ZoomDelta, Vector2? CenterOn, bool Reset)
{
    public bool IsIdle => Scroll == Vector2.Zero && RotateDelta == 0 && ZoomDelta == 0 && CenterOn is null && !Reset;
}

/// <summary>
/// Turns held keys, the cursor at the screen edge, right-drag, middle-drag, the wheel and camera meta actions into a <see cref="CameraIntent"/>.
/// It never touches a camera: the owner applies the intent.
/// </summary>
public sealed class CameraInputController(CameraSettings? settings = null)
{
    private bool _rotateLeft, _rotateRight, _zoomIn, _zoomOut, _reset;
    private float _pendingRotate, _pendingZoom;
    private Vector2? _center;
    private float _middleDragLastX;

    public CameraSettings Settings { get; } = settings ?? new CameraSettings();

    public void RequestCenter(Vector2 world) => _center = world;

    public void OnWheel(float notches) => _pendingZoom -= notches * Settings.ZoomPerWheelNotch;

    /// <summary>Consumes the camera meta actions; returns false for anything else.</summary>
    public bool OnMeta(MetaAction action)
    {
        switch (action)
        {
            case MetaAction.BeginCameraRotateLeft: _rotateLeft = true; return true;
            case MetaAction.EndCameraRotateLeft: _rotateLeft = false; return true;
            case MetaAction.BeginCameraRotateRight: _rotateRight = true; return true;
            case MetaAction.EndCameraRotateRight: _rotateRight = false; return true;
            case MetaAction.BeginCameraZoomIn: _zoomIn = true; return true;
            case MetaAction.EndCameraZoomIn: _zoomIn = false; return true;
            case MetaAction.BeginCameraZoomOut: _zoomOut = true; return true;
            case MetaAction.EndCameraZoomOut: _zoomOut = false; return true;
            case MetaAction.CameraReset: _reset = true; return true;
            default: return false;
        }
    }

    /// <summary>Middle-button gestures: a short click resets, a drag rotates by its horizontal travel.</summary>
    public void OnGesture(PointerGesture gesture)
    {
        if (gesture.Button != MouseButton.Middle) return;
        switch (gesture.Kind)
        {
            case GestureKind.Click or GestureKind.DoubleClick when Vector2.Distance(gesture.Start, gesture.Position) <= Settings.MiddleClickResetPixels:
                _reset = true;
                break;
            case GestureKind.DragStarted:
                _middleDragLastX = gesture.Start.X;
                _pendingRotate += (gesture.Position.X - _middleDragLastX) * Settings.DragRotateRadiansPerPixel;
                _middleDragLastX = gesture.Position.X;
                break;
            case GestureKind.DragMoved or GestureKind.DragEnded:
                _pendingRotate += (gesture.Position.X - _middleDragLastX) * Settings.DragRotateRadiansPerPixel;
                _middleDragLastX = gesture.Position.X;
                break;
        }
    }

    public void OnFocusLost() => _rotateLeft = _rotateRight = _zoomIn = _zoomOut = false;

    /// <summary>The intent for a frame of <paramref name="deltaSeconds"/>; one-shot requests (center, reset, wheel, drag rotation) are consumed.</summary>
    public CameraIntent Update(InputState input, HotkeyMap hotkeys, Vector2 viewportSize, float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hotkeys);
        var frames = deltaSeconds * Settings.FramesPerSecond;
        var scroll = Vector2.Zero;

        var keyboard = Vector2.Zero;
        if (Held(input, hotkeys, MetaAction.CameraScrollUp)) keyboard.Y += 1;
        if (Held(input, hotkeys, MetaAction.CameraScrollDown)) keyboard.Y -= 1;
        if (Held(input, hotkeys, MetaAction.CameraScrollRight)) keyboard.X += 1;
        if (Held(input, hotkeys, MetaAction.CameraScrollLeft)) keyboard.X -= 1;
        if (keyboard != Vector2.Zero) scroll += Scaled(keyboard) * Settings.KeyboardScrollSpeedFactor;

        if (Settings.RightDragScrolls && input.IsDragging(MouseButton.Right) && input.PressPosition(MouseButton.Right) is { } anchor)
        {
            // LookAtXlat.cpp: the right-drag speed grows with the distance from the anchor, clamped to half the screen.
            var half = viewportSize / 2;
            var offset = input.MousePosition - anchor;
            var ratio = new Vector2(half.X > 0 ? Math.Clamp(offset.X / half.X, -1, 1) : 0, half.Y > 0 ? Math.Clamp(-offset.Y / half.Y, -1, 1) : 0);
            scroll += Scaled(ratio);
        }
        else if (Settings.EdgeScrollEnabled && input.HasMouse && keyboard == Vector2.Zero
                 && input.MousePosition is { X: >= 0, Y: >= 0 } inside && inside.X < viewportSize.X && inside.Y < viewportSize.Y)
        {
            // Only a cursor on the view scrolls: in a window the pointer can be reported outside it (the original confines the cursor to its full screen).
            var edge = Vector2.Zero;
            var position = input.MousePosition;
            var margin = Settings.EdgeScrollMarginPixels;
            if (position.X < margin) edge.X -= 1;
            if (position.X >= viewportSize.X - margin) edge.X += 1;
            if (position.Y < margin) edge.Y += 1;
            if (position.Y >= viewportSize.Y - margin) edge.Y -= 1;
            if (edge != Vector2.Zero) scroll += Scaled(edge) * Settings.KeyboardScrollSpeedFactor;
        }

        var rotate = _pendingRotate;
        if (_rotateLeft) rotate += Settings.KeyboardRotateRadiansPerFrame * frames;
        if (_rotateRight) rotate -= Settings.KeyboardRotateRadiansPerFrame * frames;
        var zoom = _pendingZoom;
        if (_zoomIn) zoom -= Settings.KeyboardZoomPerFrame * frames;
        if (_zoomOut) zoom += Settings.KeyboardZoomPerFrame * frames;

        var intent = new CameraIntent(scroll * deltaSeconds, rotate, zoom, _center, _reset);
        _pendingRotate = 0;
        _pendingZoom = 0;
        _center = null;
        _reset = false;
        return intent;
    }

    private Vector2 Scaled(Vector2 direction) => new(direction.X * Settings.HorizontalScrollSpeedFactor, direction.Y * Settings.VerticalScrollSpeedFactor);

    /// <summary>A scroll binding is held when its key is down and the modifier state equals the binding's (Ctrl+arrow selects units instead).</summary>
    private static bool Held(InputState input, HotkeyMap hotkeys, MetaAction action)
        => hotkeys.TryGetEntry(action, out var entry) && entry.Binding.Key != KeyCode.None && input.IsKeyDown(entry.Binding.Key) && input.Modifiers == entry.Binding.Modifiers;
}
