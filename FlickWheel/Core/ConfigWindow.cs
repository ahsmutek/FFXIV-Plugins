using System;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace FlickWheel;

internal sealed class ConfigWindow : Window
{
    private static readonly GamepadButtons[] SwapButtons =
        [GamepadButtons.L1, GamepadButtons.R1, GamepadButtons.L2, GamepadButtons.R2];
    private static readonly string[] SwapButtonNames = ["LB", "RB", "LT", "RT"];

    private static readonly string[] ConfirmNames = ["I let go of the hotkey", "The stick returns to center"];
    private static readonly string[] ModifierNames = ["None", "Ctrl", "Shift", "Alt"];

    private readonly Configuration config;
    private readonly Action save;
    private readonly WheelSet wheels;
    private readonly WheelOverlay overlay;
    private readonly VirtualKey[] keys;
    private readonly string[] keyNames;
    private readonly string[] optionalKeyNames;
    private bool dirty;
    private readonly PadTabs tabs = new();

    public ConfigWindow(Configuration config, Action save, WheelSet wheels, WheelOverlay overlay, IKeyState keyState)
        : base("Flick Wheel Settings###FlickWheelConfig")
    {
        this.config = config;
        this.save = save;
        this.wheels = wheels;
        this.overlay = overlay;
        keys = keyState.GetValidVirtualKeys()
                       .Where(k => k is not (VirtualKey.CONTROL or VirtualKey.SHIFT or VirtualKey.MENU or
                                             VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON))
                       .ToArray();
        keyNames = keys.Select(k => k.GetFancyName()).ToArray();
        optionalKeyNames = ["None", .. keyNames];
        Size = new Vector2(460, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        overlay.PreviewWheel = config.LastWheel;
        foreach (var module in wheels.Modules)
            module.OnSettingsOpen();
    }

    public override void OnClose()
    {
        if (dirty)
            save();
        dirty = false;
        overlay.Preview = false;
    }

    public override void Draw()
    {
        var changed = false;

        if (ImGui.BeginTabBar("tabs"))
        {
            if (tabs.Item("General"))
            {
                changed |= DrawGeneral();
                ImGui.EndTabItem();
            }

            foreach (var module in wheels.Modules.Where(wheels.IsEnabled))
            {
                foreach (var tab in module.SettingsTabs)
                {
                    if (tabs.Item(tab.Title))
                    {
                        changed |= tab.Draw();
                        ImGui.EndTabItem();
                    }
                }
            }

            tabs.End();
            ImGui.EndTabBar();
        }

        if (changed)
            overlay.InvalidatePreview();

        // Sliders change the config every frame; save once they're released.
        dirty |= changed;
        if (dirty && !ImGui.IsAnyItemActive())
        {
            save();
            dirty = false;
        }
    }

    private bool DrawGeneral()
    {
        var changed = false;

        ImGui.TextDisabled("Wheels to use");
        foreach (var module in wheels.Modules)
        {
            var on = wheels.IsEnabled(module);
            if (ImGui.Checkbox(module.Name, ref on))
            {
                config.Modules[module.Id] = on;
                changed = true;
            }

            if (module.Wheels.Count < 2)
                continue;
            ImGui.Indent();
            ImGui.BeginDisabled(!on);
            foreach (var wheel in module.Wheels)
            {
                var wheelOn = wheels.IsEnabled(wheel);
                if (ImGui.Checkbox($"{wheel.Title}##{wheel.Id}", ref wheelOn))
                {
                    config.WheelsOn[wheel.Id] = wheelOn;
                    changed = true;
                }
            }
            ImGui.EndDisabled();
            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Size and position");
        var scale = config.Scale;
        if (ImGui.SliderFloat("Wheel scale", ref scale, 0.5f, 2f, "%.2fx")) { config.Scale = scale; changed = true; }
        var iconSize = config.IconSize;
        if (ImGui.SliderFloat("Icon size", ref iconSize, 24f, 64f, "%.0fpx")) { config.IconSize = iconSize; changed = true; }
        var offset = new Vector2(config.OffsetX, config.OffsetY);
        if (ImGui.DragFloat2("Offset from center", ref offset, 1f, -2000f, 2000f, "%.0f"))
        {
            config.OffsetX = offset.X;
            config.OffsetY = offset.Y;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Center"))
        {
            config.OffsetX = 0;
            config.OffsetY = 0;
            changed = true;
        }

        overlay.DrawPreviewControls();

        ImGui.Spacing();
        ImGui.TextDisabled("Stick");
        var threshold = config.SelectThreshold;
        if (ImGui.SliderFloat("Select threshold", ref threshold, 0.35f, 0.95f, "%.2f"))
        {
            config.SelectThreshold = threshold;
            changed = true;
        }
        ImGui.TextWrapped("How far the stick has to tilt to target an entry.");

        var confirm = (int)config.Confirm;
        if (ImGui.Combo("Pick when", ref confirm, ConfirmNames, ConfirmNames.Length))
        {
            config.Confirm = (ConfirmMode)confirm;
            changed = true;
        }
        ImGui.TextWrapped(config.Confirm == ConfirmMode.ShortcutRelease
            ? "The target stays when the stick springs back, so you can flick and then let go of the hotkey."
            : "Flick toward an entry and let the stick spring back. Letting go of the hotkey with an entry targeted also picks it.");

        ImGui.Spacing();
        ImGui.TextDisabled("Hotkey (hold)");
        var modifier = config.Modifier;
        var key = config.Key;
        if (DrawHotkey("shared", ref modifier, ref key, false))
        {
            config.Modifier = modifier;
            config.Key = key;
            changed = true;
        }

        var buttonIndex = Math.Max(0, Array.IndexOf(SwapButtons, config.SwapButton));
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("Tap for the next wheel", ref buttonIndex, SwapButtonNames, SwapButtonNames.Length))
        {
            config.SwapButton = SwapButtons[buttonIndex];
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Hotkey that opens one wheel directly (optional)");
        foreach (var wheel in wheels.Wheels)
        {
            ImGui.PushID(wheel.Id);
            config.DirectHotkeys.TryGetValue(wheel.Id, out var direct);
            var directModifier = direct?.Modifier ?? HotkeyModifier.Ctrl;
            var directKey = direct?.Key ?? 0;
            if (DrawHotkey(wheel.Id, ref directModifier, ref directKey, true))
            {
                if (directKey == 0)
                    config.DirectHotkeys.Remove(wheel.Id);
                else
                    config.DirectHotkeys[wheel.Id] = new Hotkey { Modifier = directModifier, Key = directKey };
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(wheel.Title);
            ImGui.PopID();
        }

        return changed;
    }

    private bool DrawHotkey(string id, ref HotkeyModifier modifier, ref VirtualKey key, bool allowNone)
    {
        var changed = false;
        var modifierIndex = (int)modifier;
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo($"##modifier-{id}", ref modifierIndex, ModifierNames, ModifierNames.Length))
        {
            modifier = (HotkeyModifier)modifierIndex;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("+");
        ImGui.SameLine();

        var names = allowNone ? optionalKeyNames : keyNames;
        var keyIndex = Array.IndexOf(keys, key);
        keyIndex = allowNone ? keyIndex + 1 : Math.Max(0, keyIndex);
        ImGui.SetNextItemWidth(140f);
        if (ImGui.Combo($"##key-{id}", ref keyIndex, names, names.Length))
        {
            key = allowNone
                ? keyIndex == 0 ? 0 : keys[keyIndex - 1]
                : keys[keyIndex];
            changed = true;
        }

        return changed;
    }
}
