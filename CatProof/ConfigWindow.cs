using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

namespace CatProof;

internal sealed class ConfigWindow : Window
{
    private static readonly (FontAwesomeIcon Icon, string Name)[] Icons =
    [
        (FontAwesomeIcon.Paw, "Paw"),
        (FontAwesomeIcon.Cat, "Cat"),
        (FontAwesomeIcon.ShieldCat, "Cat shield"),
        (FontAwesomeIcon.Fish, "Fish"),
        (FontAwesomeIcon.BoxOpen, "Box"),
        (FontAwesomeIcon.Moon, "Nap"),
        (FontAwesomeIcon.Heart, "Heart"),
        (FontAwesomeIcon.Lock, "Lock"),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly KeyLock keyLock;
    private readonly LockIndicator indicator;

    public ConfigWindow(Configuration config, Action save, KeyLock keyLock, LockIndicator indicator)
        : base("CatProof###CatProofConfig")
    {
        this.config = config;
        this.save = save;
        this.keyLock = keyLock;
        this.indicator = indicator;
        Size = new Vector2(440, 260);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (keyLock.Locked)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, "The keyboard is locked.");
            if (ImGui.Button("Unlock"))
                keyLock.Locked = false;
        }
        else
        {
            ImGui.TextUnformatted("The keyboard is unlocked.");
            if (ImGui.Button("Lock the keyboard"))
                keyLock.Locked = true;
        }

        ImGui.Spacing();
        ImGui.TextWrapped("While locked, key presses from physical keyboards don't reach the game, Dalamud or plugin " +
                          "windows. Controller, mouse and keys sent by software (Steam Input, Job Profiles) still work.");
        ImGui.TextDisabled("/catproof toggles the lock, also from a macro or Flick Wheel.");

        ImGui.Spacing();
        ImGui.TextUnformatted("While locked, show");
        if (ImGui.RadioButton("A message", !config.IconOnly))
        {
            config.IconOnly = false;
            save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Just an icon", config.IconOnly))
        {
            config.IconOnly = true;
            save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Always on screen, dimmed while unlocked.\nClick it to lock or unlock.");

        ImGui.TextUnformatted("Icon, also on Flick Wheel's /catproof shortcuts");
        DrawIconChoice();

        if (ImGui.Button("Reset indicator position"))
            indicator.ResetPosition();
    }

    private void DrawIconChoice()
    {
        var size = new Vector2(ImGui.GetFrameHeight() * 1.4f);
        for (var i = 0; i < Icons.Length; i++)
        {
            var (icon, name) = Icons[i];
            if (i > 0)
                ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            var picked = ImGui.Selectable($"{icon.ToIconString()}##{name}", config.Icon == icon,
                ImGuiSelectableFlags.None, size);
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(name);
            if (picked)
            {
                config.Icon = icon;
                save();
            }
        }
    }
}
