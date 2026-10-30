using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace Shortcuts;

// An icon drawn inside a square, centred and kept to its own shape, since not every game icon is square.
internal readonly record struct GameIcon(ImTextureID Handle, Vector2 Size)
{
    public static GameIcon Of(IDalamudTextureWrap wrap) => new(wrap.Handle, new Vector2(wrap.Width, wrap.Height));

    public void Draw(ImDrawListPtr drawList, Vector2 min, Vector2 max, uint color = uint.MaxValue)
    {
        var box = max - min;
        var fit = Size.X > 0 && Size.Y > 0 ? Math.Min(box.X / Size.X, box.Y / Size.Y) : 0f;
        var size = Size * fit;
        var at = min + (box - size) / 2;
        drawList.AddImage(Handle, at, at + size, Vector2.Zero, Vector2.One, color);
    }

    public void Image(Vector2 box)
    {
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(box);
        Draw(ImGui.GetWindowDrawList(), min, min + box);
    }
}
