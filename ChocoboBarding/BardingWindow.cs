using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ClassJob = Lumina.Excel.Sheets.ClassJob;

namespace ChocoboBarding;

internal sealed partial class BardingWindow : Window
{
    private const float IconSize = 48;
    private const float SmallIconSize = 24;
    private const float MixIconSize = 80;

    private const uint JobIconBase = 62000;
    private const string JobMacro = "/cbarding job";

    private readonly IDataManager data;
    private readonly IUnlockState unlocks;
    private readonly ITextureProvider textures;
    private readonly IPlayerState player;
    private readonly Configuration config;
    private readonly Action save;
    private (string Name, uint RowId, uint[] Icons)[] owned = [];
    private List<ClassJob> jobs = [];
    private List<PetGlamour.Pet> pets = [];
    private List<PetGlamour.Resize> resizes = [];
    private string filter = string.Empty;
    private readonly PadTabs tabs = new();

    private readonly SlotChoice[] mix = [SlotChoice.Keep, SlotChoice.Keep, SlotChoice.Keep];

    public BardingWindow(IDataManager data, IUnlockState unlocks, ITextureProvider textures,
                         IPlayerState player, Configuration config, Action save)
        : base("Chocobo Barding###ChocoboBardingMain")
    {
        this.data = data;
        this.unlocks = unlocks;
        this.textures = textures;
        this.player = player;
        this.config = config;
        this.save = save;
        Size = new Vector2(720, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        owned = Barding.Owned(data, unlocks)
                       .Select(row => (Name: Barding.ShortName(row.Name.ExtractText()), row.RowId, Icons: new uint[] { row.IconHead, row.IconBody, row.IconLegs }))
                       .DistinctBy(entry => entry.Name)
                       .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                       .ToArray();
        jobs = Barding.Jobs(data);
        pets = PetGlamour.Glamourable(data, player, unlocks);
        resizes = PetGlamour.Resizable(data, player, unlocks);
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

    private static void CopyButton(string id, string? text)
    {
        ImGui.BeginDisabled(text == null);
        if (ImGui.Button($"Copy##{id}") && text != null)
            ImGui.SetClipboardText(text);
        ImGui.EndDisabled();
    }

    private int IndexOf(uint rowId) => Array.FindIndex(owned, entry => entry.RowId == rowId);

    private string Label(SlotChoice choice) => choice.Action switch
    {
        SlotAction.Remove => "None",
        SlotAction.Set when IndexOf(choice.RowId) is var i and >= 0 => owned[i].Name,
        SlotAction.Set => $"#{choice.RowId}",
        _ => "Keep current",
    };

    private bool DrawSlotCombo(string id, int slot, ref SlotChoice choice)
    {
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##{id}", Label(choice)))
            return false;

        var picked = choice;
        if (ImGui.Selectable("Keep current", choice.Action == SlotAction.Keep))
            picked = SlotChoice.Keep;
        if (ImGui.Selectable("None", choice.Action == SlotAction.Remove))
            picked = SlotChoice.Remove;

        foreach (var (name, rowId, icons) in owned)
        {
            DrawIcon(icons[slot], SmallIconSize);
            ImGui.SameLine();
            if (ImGui.Selectable($"{name}##{id}-{rowId}", choice.Action == SlotAction.Set && choice.RowId == rowId))
                picked = SlotChoice.Set(rowId);
        }

        ImGui.EndCombo();

        if (picked == choice)
            return false;

        choice = picked;
        return true;
    }

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
                var min = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRect(min, min + new Vector2(MixIconSize), ImGui.GetColorU32(ImGuiCol.Border), 6);
                ImGui.Dummy(new Vector2(MixIconSize));
            }

            DrawSlotCombo($"mix{slot}", slot, ref mix[slot]);
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
        CopyButton("mix", macro);
    }

    private void DrawFullSets()
    {
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

            var textOffset = (IconSize - ImGui.GetFrameHeight()) / 2;
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textOffset);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(macro);

            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + textOffset);
            CopyButton(name, macro);
        }

        ImGui.EndTable();
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("tabs"))
            return;

        if (tabs.Item("Barding"))
        {
            if (owned.Length == 0)
            {
                ImGui.TextDisabled("You don't own any barding yet.");
            }
            else
            {
                DrawMixAndMatch();
                ImGui.Spacing();
                DrawFullSets();
            }
            ImGui.EndTabItem();
        }

        if (owned.Length > 0 && tabs.Item("Jobs"))
        {
            DrawJobs();
            ImGui.EndTabItem();
        }

        if ((pets.Count > 0 || resizes.Count > 0) && tabs.Item("Pets"))
        {
            DrawPets();
            ImGui.EndTabItem();
        }

        tabs.End();
        ImGui.EndTabBar();
    }
}
