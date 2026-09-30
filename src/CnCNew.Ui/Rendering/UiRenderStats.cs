using System.Collections.Immutable;

namespace CnCNew.Ui.Rendering;

/// <summary>
/// What a renderer drew and what it could not draw. Nothing that falls back to a placeholder is silent: every case has a
/// counter and, where a name exists, the sorted list of distinct names.
/// </summary>
/// <param name="Commands">Draw list commands processed (fills, borders, images, texts, clips, custom draws).</param>
/// <param name="Sprites">Quads submitted to the sprite batch (glyphs, rectangles, images, placeholders).</param>
/// <param name="DrawCalls">Runs of sprites sharing a texture inside one batch: the GPU draw calls the sprite batch issues.</param>
/// <param name="Batches">Sprite batch begin/end pairs: one per frame plus one per change of the clip state.</param>
/// <param name="MissingImages">Image draws whose name is not in the mapped image table (drawn as a magenta placeholder).</param>
/// <param name="MissingTextures">Image draws whose texture page could not be found or decoded (same placeholder).</param>
/// <param name="MissingLabels">Text draws of an unresolved label (drawn in magenta with an underline).</param>
/// <param name="MissingGlyphs">Drawn characters that no font of the chain has (drawn as the face's notdef box).</param>
/// <param name="SubstitutedGlyphs">Drawn characters taken from a fallback face.</param>
/// <param name="CustomDraws">Custom draw commands handed to the host.</param>
/// <param name="UnbalancedClipPops">Pops without push, and pushes left open at the end of the frame.</param>
public sealed record UiRenderStats(
    int Commands,
    int Sprites,
    int DrawCalls,
    int Batches,
    int MissingImages,
    int MissingTextures,
    int MissingLabels,
    int MissingGlyphs,
    int SubstitutedGlyphs,
    int CustomDraws,
    int UnbalancedClipPops,
    ImmutableArray<string> MissingImageNames,
    ImmutableArray<string> MissingTextureFiles,
    ImmutableArray<string> MissingLabelKeys,
    ImmutableArray<string> MissingGlyphCodes)
{
    public static readonly UiRenderStats Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], [], [], []);

    /// <summary>A line per value, deterministic; used by the tool report.</summary>
    public string Format()
        => $"commands={Commands} sprites={Sprites} drawCalls={DrawCalls} batches={Batches} missingImages={MissingImages} "
           + $"missingTextures={MissingTextures} missingLabels={MissingLabels} missingGlyphs={MissingGlyphs} "
           + $"substitutedGlyphs={SubstitutedGlyphs} customDraws={CustomDraws} unbalancedClips={UnbalancedClipPops}";
}

/// <summary>Mutable accumulator behind <see cref="UiRenderStats"/>; the renderer keeps one for the frame and one for its lifetime.</summary>
public sealed class UiRenderCounters
{
    private readonly SortedSet<string> _images = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _textures = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _labels = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _glyphs = new(StringComparer.Ordinal);

    public int Commands;
    public int Sprites;
    public int DrawCalls;
    public int Batches;
    public int MissingImages;
    public int MissingTextures;
    public int MissingLabels;
    public int MissingGlyphs;
    public int SubstitutedGlyphs;
    public int CustomDraws;
    public int UnbalancedClipPops;

    public void MissingImage(string name)
    {
        MissingImages++;
        _images.Add(name);
    }

    public void MissingTexture(string file)
    {
        MissingTextures++;
        _textures.Add(file);
    }

    public void MissingLabel(string key)
    {
        MissingLabels++;
        _labels.Add(key);
    }

    public void MissingGlyph(int codepoint)
    {
        MissingGlyphs++;
        _glyphs.Add($"U+{codepoint:X4}");
    }

    public void Reset()
    {
        Commands = Sprites = DrawCalls = Batches = 0;
        MissingImages = MissingTextures = MissingLabels = MissingGlyphs = SubstitutedGlyphs = CustomDraws = UnbalancedClipPops = 0;
        _images.Clear();
        _textures.Clear();
        _labels.Clear();
        _glyphs.Clear();
    }

    public void Add(UiRenderCounters other)
    {
        Commands += other.Commands;
        Sprites += other.Sprites;
        DrawCalls += other.DrawCalls;
        Batches += other.Batches;
        MissingImages += other.MissingImages;
        MissingTextures += other.MissingTextures;
        MissingLabels += other.MissingLabels;
        MissingGlyphs += other.MissingGlyphs;
        SubstitutedGlyphs += other.SubstitutedGlyphs;
        CustomDraws += other.CustomDraws;
        UnbalancedClipPops += other.UnbalancedClipPops;
        _images.UnionWith(other._images);
        _textures.UnionWith(other._textures);
        _labels.UnionWith(other._labels);
        _glyphs.UnionWith(other._glyphs);
    }

    public UiRenderStats Snapshot() => new(
        Commands, Sprites, DrawCalls, Batches, MissingImages, MissingTextures, MissingLabels, MissingGlyphs, SubstitutedGlyphs,
        CustomDraws, UnbalancedClipPops, [.._images], [.._textures], [.._labels], [.._glyphs]);
}
