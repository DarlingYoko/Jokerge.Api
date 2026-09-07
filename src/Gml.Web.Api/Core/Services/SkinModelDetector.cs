using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Gml.Web.Api.Core.Services;

/// <summary>
/// Detects whether an uploaded skin texture uses the slim ("Alex", 3px-wide arms) or
/// classic ("Steve", 4px-wide arms) player model, by inspecting the arm-overlay pixel
/// regions that only exist in the classic layout. Same heuristic as skinview-utils'
/// `inferModelType` (bs-community/skinview-utils), the de-facto standard for this.
/// </summary>
public static class SkinModelDetector
{
    public static bool IsSlimModel(Image<Rgba32> skin)
    {
        // The 64x32 legacy format predates the slim/Alex model entirely - always classic.
        if (skin.Height < 64)
            return false;

        var scale = skin.Width / 64.0;

        bool HasTransparency(int x, int y, int w, int h)
        {
            var (sx, sy, sw, sh) = Scale(x, y, w, h, scale);

            for (var dx = 0; dx < sw; dx++)
            for (var dy = 0; dy < sh; dy++)
                if (skin[sx + dx, sy + dy].A != 0xFF)
                    return true;

            return false;
        }

        bool IsUniform(int x, int y, int w, int h, Rgba32 color)
        {
            var (sx, sy, sw, sh) = Scale(x, y, w, h, scale);

            for (var dx = 0; dx < sw; dx++)
            for (var dy = 0; dy < sh; dy++)
                if (!skin[sx + dx, sy + dy].Equals(color))
                    return false;

            return true;
        }

        // Right-arm and left-arm overlay strips: present (opaque) in the classic layout,
        // unused (transparent, or padded black/white) in the slim layout.
        var black = new Rgba32(0, 0, 0, 255);
        var white = new Rgba32(255, 255, 255, 255);

        return HasTransparency(50, 16, 2, 4)
               || HasTransparency(54, 20, 2, 12)
               || HasTransparency(42, 48, 2, 4)
               || HasTransparency(46, 52, 2, 12)
               || (IsUniform(50, 16, 2, 4, black) && IsUniform(54, 20, 2, 12, black) &&
                   IsUniform(42, 48, 2, 4, black) && IsUniform(46, 52, 2, 12, black))
               || (IsUniform(50, 16, 2, 4, white) && IsUniform(54, 20, 2, 12, white) &&
                   IsUniform(42, 48, 2, 4, white) && IsUniform(46, 52, 2, 12, white));
    }

    private static (int X, int Y, int W, int H) Scale(int x, int y, int w, int h, double scale) =>
        ((int)(x * scale), (int)(y * scale), (int)(w * scale), (int)(h * scale));
}
