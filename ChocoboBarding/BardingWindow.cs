using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace ChocoboBarding;

internal sealed class BardingWindow : Window
{
    private const float IconSize = 48;

    private readonly IDataManager data;
    private readonly IUnlockState unlocks;
    private readonly ITextureProvider textures;
    private (string Name, uint[] Icons)[] owned = [];
    private string filter = string.Empty;

    public BardingWindow(IDataManager data, IUnlockState unlocks, ITextureProvider textures)
        : base("Chocobo Barding###ChocoboBardingMain")
    {
        this.data = data;
        this.unlocks = unlocks;
        this.textures = textures;
        Size = new Vector2(580, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Refresh on open so newly unlocked barding shows up.
    public override void OnOpen()
    {
        owned = Barding.Owned(data, unlocks)
                       .Select(row => (Name: row.Name.ExtractText(), Icons: new uint[] { row.IconHead, row.IconBody, row.IconLegs }))
                       .DistinctBy(entry => entry.Name)
                       .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                       .ToArray();
    }

    private void DrawIcons(uint[] icons)
    {
        for (var i = 0; i < icons.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 2);

            if (icons[i] == 0)
            {
                ImGui.Dummy(new Vector2(IconSize));
                continue;
            }

            var wrap = textures.GetFromGameIcon(new GameIconLookup(icons[i])).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, new Vector2(IconSize));
        }
    }

    public override void Draw()
    {
        ImGui.TextWrapped("Copy a line into a macro to put that full set on your chocobo.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Filter owned barding", ref filter, 64);
        ImGui.Spacing();

        if (owned.Length == 0)
        {
            ImGui.TextDisabled("You don't own any barding yet.");
            return;
        }

        if (!ImGui.BeginTable("barding", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Macro", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Copy", ImGuiTableColumnFlags.WidthFixed);

        foreach (var (name, icons) in owned)
        {
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            var macro = $"/cbarding \"{name}\"";
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawIcons(icons);

            // Center the text and button vertically against the icons.
            var textOffset = (IconSize - ImGui.GetFrameHeight()) / 2;
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textOffset);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(macro);

            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textOffset);
            if (ImGui.Button($"Copy##{name}"))
                ImGui.SetClipboardText(macro);
        }

        ImGui.EndTable();
    }
}
