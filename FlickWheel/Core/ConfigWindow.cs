using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Controller;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
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
    private static readonly Vector4 Warning = new(1f, .75f, .3f, 1f);

    private readonly Configuration config;
    private readonly Action save;
    private readonly WheelSet wheels;
    private readonly ShortcutsModule shortcuts;
    private readonly WheelOverlay overlay;
    private readonly WheelInput input;
    private readonly VirtualKey[] keys;
    private readonly string[] keyNames;
    private bool dirty;
    private string? selectedWheel;
    private string? editingHotkey;
    private string? recording;
    private bool recordingDrawn;
    private readonly PadTabs tabs = new();

    public ConfigWindow(Configuration config, Action save, WheelSet wheels, ShortcutsModule shortcuts, WheelOverlay overlay,
                        WheelInput input, IKeyState keyState)
        : base("Flick Wheel Settings###FlickWheelConfig")
    {
        this.config = config;
        this.save = save;
        this.wheels = wheels;
        this.shortcuts = shortcuts;
        this.overlay = overlay;
        this.input = input;
        keys = keyState.GetValidVirtualKeys()
                       .Where(k => k is not (VirtualKey.CONTROL or VirtualKey.SHIFT or VirtualKey.MENU or
                                             VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON))
                       .ToArray();
        keyNames = ["None", .. keys.Select(k => k.GetFancyName())];
        Size = new Vector2(700, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(560, 400), MaximumSize = new Vector2(float.MaxValue) };
    }

    public override void OnOpen()
    {
        overlay.PreviewWheel = config.WheelCycles.FirstOrDefault()?.LastWheel ?? string.Empty;
        foreach (var module in wheels.Modules)
            module.OnSettingsOpen();
    }

    public override void OnClose()
    {
        if (dirty)
            save();
        dirty = false;
        overlay.Preview = false;
        StartRecording(null);
    }

    public override void Draw()
    {
        var changed = false;
        recordingDrawn = false;

        if (ImGui.BeginTabBar("tabs"))
        {
            if (tabs.Item("Wheels"))
            {
                changed |= DrawWheels();
                ImGui.EndTabItem();
            }

            if (tabs.Item("Hotkeys"))
            {
                changed |= DrawHotkeys();
                ImGui.EndTabItem();
            }

            if (tabs.Item("Look & feel"))
            {
                changed |= DrawLookAndFeel();
                ImGui.EndTabItem();
            }

            tabs.End();
            ImGui.EndTabBar();
        }

        // Switching tab or closing the editor hides the hotkey being recorded.
        if (recording != null && !recordingDrawn)
            StartRecording(null);

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

    private bool DrawWheels()
    {
        var changed = false;
        var all = wheels.Wheels;
        var wheel = all.FirstOrDefault(w => w.Id == selectedWheel) ?? all[0];

        var listWidth = 180f * ImGuiHelpers.GlobalScale;
        if (ImGui.BeginChild("##wheels", new Vector2(listWidth, 0), true))
        {
            foreach (var other in all)
            {
                if (ImGui.Selectable($"{other.Title}##{other.Id}", other == wheel))
                {
                    selectedWheel = other.Id;
                    overlay.PreviewWheel = other.Id;
                }
            }

            ImGui.Spacing();
            if (ImGui.Button("+ New wheel"))
            {
                selectedWheel = overlay.PreviewWheel = shortcuts.NewWheel();
                changed = true;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("##wheel", Vector2.Zero, true))
        {
            DrawOpenedBy(wheel);
            var preview = overlay.Preview;
            if (ImGui.Checkbox("Show wheel preview", ref preview))
            {
                overlay.Preview = preview;
                overlay.PreviewWheel = wheel.Id;
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            changed |= wheel.DrawSettings();
        }
        ImGui.EndChild();

        return changed;
    }

    private void DrawOpenedBy(IWheel wheel)
    {
        var by = config.WheelCycles.Where(h => h.Wheels.Contains(wheel.Id)).ToList();
        if (by.Count == 0)
        {
            ImGui.TextColored(Warning, "No hotkey opens this wheel; add it to one on the Hotkeys tab.");
            return;
        }

        ImGui.TextDisabled("Opened by:");
        ImGui.SameLine();
        ImGui.TextUnformatted(string.Join(" · ", by.Select(h => $"{KeyLabel(h)} ({TitleOf(h)})")));
    }

    private bool DrawHotkeys()
    {
        var changed = false;
        ImGui.TextWrapped("Hold a hotkey to open its wheels, flick toward an entry and let go. While it's open, tap the next wheel button to move through its wheels.");
        ImGui.Spacing();

        var buttonIndex = Math.Max(0, Array.IndexOf(SwapButtons, config.SwapButton));
        ImGui.SetNextItemWidth(100f);
        if (ImGui.Combo("Next wheel button", ref buttonIndex, SwapButtonNames, SwapButtonNames.Length))
        {
            config.SwapButton = SwapButtons[buttonIndex];
            changed = true;
        }

        WheelCycle? deleted = null;
        foreach (var hotkey in config.WheelCycles)
        {
            ImGui.PushID(hotkey.Uid);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var editing = editingHotkey == hotkey.Uid;
            if (ImGui.SmallButton(editing ? "Done" : "Edit"))
                editingHotkey = editing ? null : hotkey.Uid;
            ImGui.SameLine();
            ImGui.TextUnformatted(KeyLabel(hotkey));
            ImGui.SameLine();
            ImGui.TextDisabled(TitleOf(hotkey));

            var opens = wheels.CycleOf(hotkey);
            if (opens.Count > 0)
                ImGui.TextWrapped(string.Join(" › ", opens.Select(w => w.Title)));
            foreach (var problem in Problems(hotkey, opens))
                ImGui.TextColored(Warning, problem);

            if (editing)
            {
                ImGui.Indent();
                ImGui.Spacing();
                var (editChanged, delete) = DrawHotkeyEditor(hotkey, opens);
                changed |= editChanged;
                if (delete)
                    deleted = hotkey;
                ImGui.Unindent();
            }
            ImGui.PopID();
        }

        if (deleted != null)
        {
            config.WheelCycles.Remove(deleted);
            editingHotkey = null;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("+ Add hotkey"))
        {
            var added = new WheelCycle { Name = $"Hotkey {config.WheelCycles.Count + 1}" };
            config.WheelCycles.Add(added);
            editingHotkey = added.Uid;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Tip: in Job Profiles, set a button like L3 to Open Flick Wheel with a different");
        ImGui.TextDisabled("hotkey on each job. Those hotkeys don't need a key of their own.");

        return changed;
    }

    private IEnumerable<string> Problems(WheelCycle hotkey, List<IWheel> opens)
    {
        if (opens.Count == 0)
            yield return "No wheels yet.";
        var earlier = config.WheelCycles.TakeWhile(h => h != hotkey)
                            .FirstOrDefault(h => h.Key != 0 && h.Key == hotkey.Key && h.Modifier == hotkey.Modifier);
        if (earlier != null)
            yield return $"Same key as {TitleOf(earlier)}, which wins.";
    }

    private (bool Changed, bool Delete) DrawHotkeyEditor(WheelCycle hotkey, List<IWheel> opens)
    {
        var changed = false;

        var name = hotkey.Name;
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("Name", "e.g. Healer", ref name, 32))
        {
            hotkey.Name = name;
            changed = true;
        }

        changed |= DrawKey(hotkey);

        ImGui.Spacing();
        ImGui.TextDisabled("Wheels, in the order the next wheel button goes through them");
        for (var i = 0; i < opens.Count; i++)
        {
            ImGui.PushID(i);
            if (GroupEditor.MoveArrows(i, opens.Count) is var step and not 0)
            {
                hotkey.Wheels = opens.Select(w => w.Id).ToList();
                (hotkey.Wheels[i], hotkey.Wheels[i + step]) = (hotkey.Wheels[i + step], hotkey.Wheels[i]);
                changed = true;
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(opens[i].Title);
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                hotkey.Wheels.Remove(opens[i].Id);
                changed = true;
            }
            ImGui.PopID();
        }

        var addable = wheels.Wheels.Where(w => !hotkey.Wheels.Contains(w.Id)).ToList();
        if (addable.Count > 0)
        {
            ImGui.SetNextItemWidth(200f);
            if (ImGui.BeginCombo("##add", "Add wheel…"))
            {
                foreach (var wheel in addable)
                {
                    if (ImGui.Selectable($"{wheel.Title}##{wheel.Id}"))
                    {
                        hotkey.Wheels.Add(wheel.Id);
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();
        var last = config.WheelCycles.Count == 1;
        var canDelete = !last && (opens.Count == 0 || ImGui.GetIO().KeyCtrl);
        ImGui.BeginDisabled(!canDelete);
        var delete = ImGui.SmallButton("Delete hotkey");
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(last ? "You need at least one hotkey to open the wheels." : "Hold Ctrl to delete a hotkey that has wheels.");

        return (changed, delete);
    }

    private bool DrawKey(WheelCycle hotkey)
    {
        var changed = false;
        var isRecording = recording == hotkey.Uid;
        recordingDrawn |= isRecording;

        var label = isRecording ? "Press the key… (Esc cancels)"
                    : hotkey.Key != 0 ? KeyLabel(hotkey) : "No key: click, then press one";
        if (ImGui.Button($"{label}###record"))
            StartRecording(isRecording ? null : hotkey.Uid);
        if (!isRecording && ImGui.IsItemHovered())
            ImGui.SetTooltip("Click, then press the key with its modifier, e.g. Ctrl + ,\nNot needed if only a Job Profiles button opens this.");
        if (hotkey.Key != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
            {
                hotkey.Key = 0;
                hotkey.Modifier = HotkeyModifier.None;
                changed = true;
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Choose from list"))
            ImGui.OpenPopup("##keys");
        if (ImGui.BeginPopup("##keys"))
        {
            var modifierIndex = (int)hotkey.Modifier;
            ImGui.SetNextItemWidth(100f);
            if (ImGui.Combo("##modifier", ref modifierIndex, ModifierNames, ModifierNames.Length))
            {
                hotkey.Modifier = (HotkeyModifier)modifierIndex;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted("+");
            ImGui.SameLine();
            var keyIndex = Array.IndexOf(keys, hotkey.Key) + 1;
            ImGui.SetNextItemWidth(140f);
            if (ImGui.Combo("##key", ref keyIndex, keyNames, keyNames.Length))
            {
                hotkey.Key = keyIndex == 0 ? 0 : keys[keyIndex - 1];
                changed = true;
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Key");

        if (isRecording && RecordKey() is { } recorded)
        {
            (hotkey.Modifier, hotkey.Key) = recorded;
            changed = true;
        }

        return changed;
    }

    private void StartRecording(string? id)
    {
        recording = id;
        input.Paused = id != null;
    }

    private (HotkeyModifier, VirtualKey)? RecordKey()
    {
        if (WheelInput.IsDown(VirtualKey.ESCAPE))
        {
            StartRecording(null);
            return null;
        }

        foreach (var key in keys)
        {
            if (key == VirtualKey.ESCAPE || !WheelInput.IsDown(key))
                continue;
            var modifier = WheelInput.IsDown(VirtualKey.CONTROL) ? HotkeyModifier.Ctrl
                         : WheelInput.IsDown(VirtualKey.SHIFT) ? HotkeyModifier.Shift
                         : WheelInput.IsDown(VirtualKey.MENU) ? HotkeyModifier.Alt
                         : HotkeyModifier.None;
            StartRecording(null);
            return (modifier, key);
        }

        return null;
    }

    private static string TitleOf(WheelCycle hotkey) => hotkey.Name.Length > 0 ? hotkey.Name : "Unnamed";

    private static string KeyLabel(WheelCycle hotkey) =>
        hotkey.Key == 0 ? "No key"
        : hotkey.Modifier == HotkeyModifier.None
            ? hotkey.Key.GetFancyName()
            : $"{ModifierNames[(int)hotkey.Modifier]} + {hotkey.Key.GetFancyName()}";

    private bool DrawLookAndFeel()
    {
        var changed = false;

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
        ImGui.TextDisabled("Picking");
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

        return changed;
    }
}
