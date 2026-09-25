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
    private const float SmallIconSize = 24;
    private const float MixIconSize = 80;

    private readonly IDataManager data;
    private readonly IUnlockState unlocks;
    private readonly ITextureProvider textures;
    private (string Name, uint RowId, uint[] Icons)[] owned = [];
    private string filter = string.Empty;

    // Mix & match picks per slot (head, body, legs).
    private readonly SlotChoice[] mix = [SlotChoice.Keep, SlotChoice.Keep, SlotChoice.Keep];

    public BardingWindow(IDataManager data, IUnlockState unlocks, ITextureProvider textures)
        : base("Chocobo Barding###ChocoboBardingMain")
    {
        this.data = data;
        this.unlocks = unlocks;
        this.textures = textures;
        Size = new Vector2(580, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    // Refresh on open so newly unlocked barding shows up.
    public override void OnOpen()
    {
        owned = Barding.Owned(data, unlocks)
                       .Select(row => (Name: Barding.ShortName(row.Name.ExtractText()), row.RowId, Icons: new uint[] { row.IconHead, row.IconBody, row.IconLegs }))
                       .DistinctBy(entry => entry.Name)
                       .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                       .ToArray();
    }

    private void DrawIcon(uint icon, float size)
    {
        if (icon == 0)
        {
            ImGui.Dummy(new Vector2(size));
            return;
        }

        var wrap = textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, new Vector2(size));
    }

    private void DrawIcons(uint[] icons)
    {
        for (var i = 0; i < icons.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 2);
            DrawIcon(icons[i], IconSize);
        }
    }

    private static void Heading(string text)
    {
        ImGui.TextUnformatted(text);
        ImGui.Separator();
    }

    // Moves the cursor so an item of this width is centered in the current column.
    private static void CenterNext(float width)
    {
        var offset = (ImGui.GetContentRegionAvail().X - width) / 2;
        if (offset > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
    }

    private static void CenteredText(string text, bool disabled = false)
    {
        CenterNext(ImGui.CalcTextSize(text).X);
        if (disabled)
            ImGui.TextDisabled(text);
        else
            ImGui.TextUnformatted(text);
    }

    private int IndexOf(uint rowId) => Array.FindIndex(owned, entry => entry.RowId == rowId);

    private string Label(SlotChoice choice) => choice.Action switch
    {
        SlotAction.Remove => "None",
        SlotAction.Set when IndexOf(choice.RowId) is var i and >= 0 => owned[i].Name,
        _ => "Keep current",
    };

    /// <summary>The /cbarding line for the current picks, or null when every slot is "Keep current".</summary>
    private string? MixMacro()
    {
        if (mix.All(choice => choice.Action == SlotAction.Keep))
            return null;

        string Token(SlotChoice choice) => Barding.ToToken(choice, Label(choice));

        return mix[0] == mix[1] && mix[1] == mix[2]
            ? $"/cbarding {Token(mix[0])}"
            : $"/cbarding {string.Join(' ', mix.Select(Token))}";
    }

    private void DrawMixAndMatch()
    {
        Heading("Mix & match");

        // One column per slot: slot label, big icon, then its dropdown.
        if (!ImGui.BeginTable("mix", Barding.SlotCount, ImGuiTableFlags.SizingStretchSame))
            return;

        for (var slot = 0; slot < Barding.SlotCount; slot++)
        {
            ImGui.TableNextColumn();
            var current = mix[slot];
            var index = current.Action == SlotAction.Set ? IndexOf(current.RowId) : -1;

            CenteredText(Barding.SlotNames[slot], disabled: true);
            CenterNext(MixIconSize);
            if (index >= 0)
            {
                DrawIcon(owned[index].Icons[slot], MixIconSize);
            }
            else
            {
                // Empty frame for "Keep current" / "None".
                var min = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(min, min + new Vector2(MixIconSize), ImGui.GetColorU32(ImGuiCol.Border), 6);
                ImGui.Dummy(new Vector2(MixIconSize));
            }

            ImGui.SetNextItemWidth(-1);
            if (!ImGui.BeginCombo($"##mix{slot}", Label(current)))
                continue;

            if (ImGui.Selectable("Keep current", current.Action == SlotAction.Keep))
                mix[slot] = SlotChoice.Keep;
            if (ImGui.Selectable("None", current.Action == SlotAction.Remove))
                mix[slot] = SlotChoice.Remove;

            foreach (var (name, rowId, icons) in owned)
            {
                DrawIcon(icons[slot], SmallIconSize);
                ImGui.SameLine();
                if (ImGui.Selectable($"{name}##mix{slot}-{rowId}", current.Action == SlotAction.Set && current.RowId == rowId))
                    mix[slot] = SlotChoice.Set(rowId);
            }

            ImGui.EndCombo();
        }

        ImGui.EndTable();
        ImGui.Spacing();

        var macro = MixMacro();
        ImGui.AlignTextToFramePadding();
        if (macro == null)
            ImGui.TextDisabled("Pick at least one slot to build a macro line.");
        else
            ImGui.TextUnformatted(macro);

        ImGui.SameLine();
        ImGui.BeginDisabled(macro == null);
        if (ImGui.Button("Copy##mix") && macro != null)
            ImGui.SetClipboardText(macro);
        ImGui.EndDisabled();
    }

    public override void Draw()
    {
        if (owned.Length == 0)
        {
            ImGui.TextDisabled("You don't own any barding yet.");
            return;
        }

        DrawMixAndMatch();

        ImGui.Spacing();
        Heading("Full sets");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Filter owned barding", ref filter, 64);
        ImGui.Spacing();

        if (!ImGui.BeginTable("barding", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Macro", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Copy", ImGuiTableColumnFlags.WidthFixed);

        foreach (var (name, _, icons) in owned)
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
