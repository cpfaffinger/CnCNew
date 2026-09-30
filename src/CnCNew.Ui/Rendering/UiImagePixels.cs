using CnCNew.Ui.Images;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Rendering;

/// <summary>Sizes of mapped images for window layout (<see cref="IUiImageLookup"/>), on screen orientation: rotated images are transposed.</summary>
public sealed class MappedImageLookup(MappedImageTable table) : IUiImageLookup
{
    public bool TryGetSize(string imageName, out int width, out int height)
    {
        if (table.TryGet(imageName, out var image))
        {
            width = image.Width;
            height = image.Height;
            return true;
        }
        width = height = 0;
        return false;
    }
}

public static class UiImagePixels
{
    /// <summary>
    /// Cuts the page rectangle of a <c>ROTATED_90_CLOCKWISE</c> image out of the page and turns it counter-clockwise so that it
    /// is upright again: the packer stored the image rotated clockwise, so screen pixel (x, y) comes from page pixel
    /// (<c>pageWidth - 1 - y</c>, <c>x</c>) of the stored rectangle. The result is
    /// <see cref="MappedImage.Width"/> x <see cref="MappedImage.Height"/> (the stored rectangle transposed).
    /// </summary>
    public static byte[] UprightRotated(MappedImage image, int textureWidth, int textureHeight, byte[] pageRgba)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(pageRgba);
        if (pageRgba.Length != textureWidth * textureHeight * 4)
            throw new ArgumentException("Page pixels do not match the page size.", nameof(pageRgba));
        if (image.Right > textureWidth || image.Bottom > textureHeight)
            throw new ArgumentException($"Image '{image.Name}' lies outside the {textureWidth}x{textureHeight} page it was decoded from.", nameof(image));
        var storedWidth = image.Right - image.Left;
        var width = image.Width;
        var height = image.Height;
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = ((image.Top + x) * textureWidth + image.Left + storedWidth - 1 - y) * 4;
                Array.Copy(pageRgba, source, result, (y * width + x) * 4, 4);
            }
        }
        return result;
    }

    /// <summary>The texel rectangle of an unrotated image on its page.</summary>
    public static (int X, int Y, int Width, int Height) SourceRect(MappedImage image)
        => (image.Left, image.Top, image.Right - image.Left, image.Bottom - image.Top);
}
