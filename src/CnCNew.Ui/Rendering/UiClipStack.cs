using CnCNew.Ui.Layout;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// The clip rectangle of the draw list: <c>UiPushClip</c> intersects with the current one, <c>UiPopClip</c> restores the
/// previous one. The bottom of the stack is the viewport. An unbalanced pop is not applied and is counted.
/// </summary>
public sealed class UiClipStack
{
    private readonly Stack<UiRect> _stack = new();
    private UiRect _bounds;

    public UiClipStack(UiRect bounds) => _bounds = bounds;

    public UiRect Bounds => _bounds;
    public UiRect Current => _stack.Count == 0 ? _bounds : _stack.Peek();
    public int Depth => _stack.Count;

    /// <summary>Number of pops that had no matching push since the last <see cref="Reset"/>.</summary>
    public int UnderflowCount { get; private set; }

    public void Reset(UiRect bounds)
    {
        _bounds = bounds;
        _stack.Clear();
        UnderflowCount = 0;
    }

    public void Push(UiRect rect) => _stack.Push(Current.Intersect(rect));

    public bool Pop()
    {
        if (_stack.Count == 0)
        {
            UnderflowCount++;
            return false;
        }
        _stack.Pop();
        return true;
    }

    /// <summary>The visible part of a draw with its own extra clip (text clip rect): both clips intersected.</summary>
    public UiRect With(UiRect extra) => Current.Intersect(extra);
}
