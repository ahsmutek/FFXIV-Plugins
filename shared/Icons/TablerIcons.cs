using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace Icons;

// Each is a white PNG from render-icons.ps1, named as the icon is on tabler.io.
internal enum TablerIcon
{
    ArrowBackUp,
    MoneybagMinus,
    MoneybagPlus,
    Settings,
    Trash,
    X,
}

// Tabler Icons (MIT), drawn as embedded PNGs tinted like text.
internal static partial class TablerIcons
{
    private static readonly string[] Resources = Enum.GetValues<TablerIcon>()
                                                     .Select(i => $"Icons.{Kebab().Replace(i.ToString(), "-$1").ToLowerInvariant()}.png")
                                                     .ToArray();

    private static ITextureProvider? textures;

    public static void Init(ITextureProvider textureProvider) => textures = textureProvider;

    public static float Width => ImGui.GetFontSize();

    public static void Draw(TablerIcon icon, Vector4? color = null)
    {
        var tint = color ?? ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.Text));
        ImGui.Image(Texture(icon), new Vector2(Width), Vector2.Zero, Vector2.One, tint);
    }

    public static bool Button(TablerIcon icon, string id = "")
    {
        var size = ImGui.GetFrameHeight();
        var clicked = ImGui.Button($"##{id}{icon}", new Vector2(size));
        var min = ImGui.GetItemRectMin() + new Vector2((size - Width) / 2);
        ImGui.GetWindowDrawList().AddImage(Texture(icon), min, min + new Vector2(Width), Vector2.Zero, Vector2.One,
                                           ImGui.GetColorU32(ImGuiCol.Text));
        return clicked;
    }

    private static ImTextureID Texture(TablerIcon icon) =>
        textures?.GetFromManifestResource(typeof(TablerIcons).Assembly, Resources[(int)icon]).GetWrapOrEmpty().Handle ??
        default;

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex Kebab();
}
