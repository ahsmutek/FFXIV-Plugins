using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace Commendable;

internal sealed class SettingsWindow : Window
{
    private const int MostSavedDuties = 10;

    private static readonly (Category Value, string Label)[] Relationships =
    [
        (Category.DancePartner, "Dance Partner"),
        (Category.Kardia, "Kardia"),
        (Category.Synastry, "Synastry"),
        (Category.Aetherpact, "Aetherpact (Fey Union)"),
    ];

    private static readonly (Category Value, string Label)[] Assistance =
    [
        (Category.Resurrection, "Raises"),
        (Category.LimitBreakRevival, "Healer limit break revivals"),
        (Category.Protection, "Protection (Cover, The Blackest Night, Aquaveil...)"),
        (Category.Cards, "Astrologian cards"),
        (Category.Cleanse, "Esuna and The Warden's Paean"),
        (Category.Rescue, "Rescue"),
    ];

    private static readonly (HealingShown Value, string Label)[] HealingOptions =
    [
        (HealingShown.Off, "Off"),
        (HealingShown.NonHealers, "From non-healers"),
        (HealingShown.All, "From everyone"),
    ];

    private readonly Configuration config;
    private readonly Action save;

    public SettingsWindow(Configuration config, Action save)
        : base("Commendable settings###CommendableSettings")
    {
        this.config = config;
        this.save = save;
        Size = new Vector2(400, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextDisabled("Summary");
        Checkbox("Open when the duty is complete", config.AutoOpen, v => config.AutoOpen = v,
                 "Opens without taking the controller or keyboard from the game, and only if there's someone to show.");
        using (ImRaii.Disabled(!config.AutoOpen))
        {
            ImGui.Indent();
            Checkbox("Wait for the commendation window", config.WaitForCommendations, v => config.WaitForCommendations = v,
                     "Opens when you open the game's commendation window instead.");
            ImGui.Unindent();
        }
        Checkbox("Hide players with nothing recorded", config.HideQuiet, v => config.HideQuiet = v);
        Checkbox("Hide players who left", config.HideLeft, v => config.HideLeft = v,
                 "Otherwise they're dimmed and marked as left.");
        Checkbox("Compact", config.Compact, v => config.Compact = v, "Each player's lines on one line, without action icons.");
        Checkbox("Keep the previous duty after leaving", config.KeepLastDuty, v => config.KeepLastDuty = v,
                 "Shown under Previous duty and with /commendable last.");
        Checkbox("Remember where the window was", config.RememberPosition, v => config.RememberPosition = v,
                 "When off, it opens beside the game's commendation window if that's open.");

        ImGui.Spacing();
        ImGui.TextDisabled("Relationships");
        Categories(Relationships);

        ImGui.Spacing();
        ImGui.TextDisabled("Personal assistance");
        Categories(Assistance);
        Checkbox("Bookmarks", config.Bookmarks, v => config.Bookmarks = v,
                 "Bookmark a party member from their right-click menu, the summary, or /commendable mark on your target.");

        ImGui.Spacing();
        ImGui.TextDisabled("Party");
        Categories([(Category.LimitBreakUsed, "Limit breaks, whoever they hit")]);

        ImGui.Spacing();
        ImGui.TextDisabled("Routine support");
        ImGui.SetNextItemWidth(160f * ImGuiHelpers.GlobalScale);
        var shown = Array.Find(HealingOptions, o => o.Value == config.Healing).Label ?? config.Healing.ToString();
        if (ImGui.BeginCombo("Single-target healing", shown))
        {
            foreach (var (value, label) in HealingOptions)
            {
                if (ImGui.Selectable(label, value == config.Healing))
                {
                    config.Healing = value;
                    save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Cure II, Benefic, Physick and the like. Healers heal all the time, so their heals can crowd the summary;\n" +
                             "heals from other jobs are rarer and shown in green.");

        ImGui.Spacing();
        ImGui.TextDisabled("Chat");
        Checkbox("Say when someone raises or selects you", config.ChatNotifications, v => config.ChatNotifications = v,
                 "Printed in your own chat log; nobody else sees it.");

        ImGui.Spacing();
        ImGui.TextDisabled("Saved duties");
        var saved = config.SavedDuties;
        ImGui.SetNextItemWidth(100f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt("Duties to keep", ref saved))
        {
            config.SavedDuties = Math.Clamp(saved, 0, MostSavedDuties);
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("What the plugin saw in your last duties, kept on this PC so /commendable replay can show them again.\n" +
                             "0 keeps nothing new.");
    }

    private void Categories((Category Value, string Label)[] categories)
    {
        foreach (var (category, label) in categories)
        {
            var on = !config.DisabledCategories.Contains(category);
            if (!ImGui.Checkbox(label, ref on))
                continue;
            if (on)
                config.DisabledCategories.Remove(category);
            else
                config.DisabledCategories.Add(category);
            save();
        }
    }

    private void Checkbox(string label, bool value, Action<bool> set, string? tooltip = null)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            save();
        }
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }
}
