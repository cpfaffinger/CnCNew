using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.UiRenderTool;

/// <summary>Adapts <see cref="LocalizedText"/> to <see cref="IUiTextSource"/> for a live <c>UiWindowTree</c>.</summary>
internal sealed class LocalizedTextSource(LocalizedText text) : IUiTextSource
{
    public bool TryResolve(string label, out string value) => text.TryGet(label, out value!);
}
