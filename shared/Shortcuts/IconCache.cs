using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Lumina.Data.Files;

namespace Shortcuts;

// Textures for wheel icons. Job glyphs come straight from the game. Main-menu icons (logs,
// Duty Finder…) only exist as a gold glyph on a red tile, so the tile and its frame are keyed out
// once, leaving a transparent gold glyph that matches the job icons.
internal sealed class IconCache(IDataManager data, ITextureProvider textures) : IDisposable
{
    private readonly Dictionary<uint, IDalamudTextureWrap?> keyed = new();

    public ImTextureID Handle(uint iconId, bool keyOutTile)
    {
        if (keyOutTile && Keyed(iconId) is { } wrap)
            return wrap.Handle;
        return textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty().Handle;
    }

    public void Dispose()
    {
        foreach (var wrap in keyed.Values)
            wrap?.Dispose();
        keyed.Clear();
    }

    private IDalamudTextureWrap? Keyed(uint iconId)
    {
        if (keyed.TryGetValue(iconId, out var wrap))
            return wrap;

        var folder = iconId / 1000 * 1000;
        var tex = data.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}_hr1.tex")
                  ?? data.GetFile<TexFile>($"ui/icon/{folder:D6}/{iconId:D6}.tex");
        if (tex != null)
        {
            var pixels = (byte[])tex.ImageData.Clone();
            KeyOutTile(pixels, tex.Header.Width, tex.Header.Height);
            wrap = textures.CreateFromRaw(RawImageSpecification.Bgra32(tex.Header.Width, tex.Header.Height), pixels,
                                          $"Keyed icon {iconId}");
        }

        keyed[iconId] = wrap;
        return wrap;
    }

    // BGRA pixels. Gold and cream keep green close to red; the tile's reds and
    // browns don't, so that ratio becomes the alpha. The frame around the edge is
    // gold too, so a margin is faded out as well.
    private static void KeyOutTile(byte[] pixels, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                var g = pixels[i + 1] / 255f;
                var r = pixels[i + 2] / 255f;
                var gold = Math.Clamp((g / Math.Max(r, 0.01f) - 0.45f) / 0.25f, 0f, 1f) *
                           Math.Clamp((r - 0.25f) / 0.2f, 0f, 1f);

                var margin = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y)) / (float)width;
                var edge = Math.Clamp((margin - 0.10f) / 0.04f, 0f, 1f);

                pixels[i + 3] = (byte)(pixels[i + 3] * gold * edge);
            }
        }

        AddShadow(pixels, width, height);
    }

    // Darken the glyph slightly and put a soft dark outline under it, offset down
    // and right, so it reads like the job glyphs' drop shadow.
    private static void AddShadow(byte[] pixels, int width, int height)
    {
        const float darken = 0.82f;
        const float shadowStrength = 0.8f;
        var radius = Math.Max(1, width / 40);
        var offset = Math.Max(1, width / 64);

        var alpha = new float[width * height];
        for (var p = 0; p < alpha.Length; p++)
            alpha[p] = pixels[p * 4 + 3] / 255f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Shadow: the glyph's alpha spread by the radius and shifted by the offset.
                var shadow = 0f;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = x - offset + dx;
                        var sy = y - offset + dy;
                        if (sx >= 0 && sy >= 0 && sx < width && sy < height)
                            shadow = Math.Max(shadow, alpha[sy * width + sx]);
                    }
                }
                shadow *= shadowStrength;

                // Glyph over a black shadow, in straight (non-premultiplied) alpha.
                var i = (y * width + x) * 4;
                var a = alpha[y * width + x];
                var outA = a + shadow * (1 - a);
                var scale = outA > 0 ? a * darken / outA : 0f;
                pixels[i] = (byte)(pixels[i] * scale);
                pixels[i + 1] = (byte)(pixels[i + 1] * scale);
                pixels[i + 2] = (byte)(pixels[i + 2] * scale);
                pixels[i + 3] = (byte)(outA * 255);
            }
        }
    }
}
