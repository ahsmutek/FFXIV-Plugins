using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Plugin.Services;

namespace JobProfiles;

// The Buttons tab: each binding's combo (recorded by pressing it) and its ordered rules.
internal sealed class ButtonsTab
{
    // Xbox names, as printed on most pads people map with reWASD or Steam Input.
    private static readonly Dictionary<GamepadButtons, string> PadNames = new()
    {
        [GamepadButtons.L2] = "LT",
        [GamepadButtons.L1] = "LB",
        [GamepadButtons.R2] = "RT",
        [GamepadButtons.R1] = "RB",
        [GamepadButtons.Select] = "Back",
        [GamepadButtons.Start] = "Start",
        [GamepadButtons.L3] = "L3",
        [GamepadButtons.R3] = "R3",
        [GamepadButtons.DpadUp] = "D-pad Up",
        [GamepadButtons.DpadDown] = "D-pad Down",
        [GamepadButtons.DpadLeft] = "D-pad Left",
        [GamepadButtons.DpadRight] = "D-pad Right",
        [GamepadButtons.North] = "Y",
        [GamepadButtons.South] = "A",
        [GamepadButtons.West] = "X",
        [GamepadButtons.East] = "B",
    };

    private readonly Configuration config;
    private readonly JobGroups groups;
    private readonly Duties duties;
    private readonly BindingWatcher watcher;
    private readonly Widgets widgets;
    private readonly VirtualKey[] keys;

    // The binding being recorded, and every pad button held since recording started.
    private Binding? recording;
    private GamepadButtons recordedButtons;
    // A rule whose key (for "act as a key") is being recorded.
    private Rule? recordingKey;

    public ButtonsTab(Configuration config, JobGroups groups, Duties duties, BindingWatcher watcher, Widgets widgets,
                      IKeyState keyState)
    {
        this.config = config;
        this.groups = groups;
        this.duties = duties;
        this.watcher = watcher;
        this.widgets = widgets;
        keys = keyState.GetValidVirtualKeys()
                       .Where(k => k is not (VirtualKey.CONTROL or VirtualKey.SHIFT or VirtualKey.MENU or
                                             VirtualKey.LCONTROL or VirtualKey.RCONTROL or VirtualKey.LSHIFT or
                                             VirtualKey.RSHIFT or VirtualKey.LMENU or VirtualKey.RMENU or
                                             VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON or
                                             VirtualKey.XBUTTON1 or VirtualKey.XBUTTON2))
                       .ToArray();
    }

    public void StopRecording()
    {
        recording = null;
        recordingKey = null;
        recordedButtons = GamepadButtons.None;
        watcher.Paused = false;
    }

    public bool Draw()
    {
        var changed = false;

        var enabled = config.BindingsEnabled;
        if (ImGui.Checkbox("Use controller buttons and keys", ref enabled))
        {
            config.BindingsEnabled = enabled;
            changed = true;
        }

        ImGui.TextWrapped("Make a controller button, a button combo or a keyboard key do different things depending on " +
                          "your job and where you are. Each one has rules, checked from the top; the first that fits is used.");
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextWrapped("Tip: unbind these buttons in the game's controller settings, or the game reacts to them too. " +
                          "For paddles, map each one to a key the game doesn't use (F13–F24) in reWASD or Steam Input, " +
                          "then record that key here.");
        ImGui.PopStyleColor();

        changed |= Record();

        Action? pending = null;
        foreach (var binding in config.Bindings)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushID(binding.Uid);
            changed |= DrawBinding(binding, ref pending);
            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Add button or key"))
        {
            var binding = new Binding { Rules = [new Rule()] };
            config.Bindings.Add(binding);
            StartRecording(binding);
            changed = true;
        }

        if (pending != null)
        {
            pending();
            changed = true;
        }

        return changed;
    }

    private bool DrawBinding(Binding binding, ref Action? pending)
    {
        var changed = false;

        // The combo, as a button that records a new one.
        var label = recording == binding ? "Press the button, combo or key… (Esc cancels)" :
                    binding.IsSet ? Describe(binding) : "Not set: click, then press it";
        if (ImGui.Button($"{label}###record"))
        {
            if (recording == binding)
                StopRecording();
            else
                StartRecording(binding);
        }
        if (ImGui.IsItemHovered() && recording != binding)
            ImGui.SetTooltip("Click, then press it. For a combo, hold the buttons together and let go. A key can include Ctrl, Shift or Alt.");

        // A binding with rules in it needs Ctrl held, so it can't go by accident.
        ImGui.SameLine();
        var canDelete = binding.Rules.All(r => r.Steps.Count == 0 && !r.ActsAsKey) || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDelete);
        if (ImGui.SmallButton("Delete"))
        {
            pending = () => config.Bindings.Remove(binding);
            if (recording == binding)
                StopRecording();
        }
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete: this button has rules set up.");

        // What it would do right now, so the rules can be checked without pressing anything.
        var match = watcher.Match(binding);
        ImGui.TextDisabled(match switch
        {
            { ActsAsKey: true } => $"Here and now: holds {KeyPresser.Describe(match)} (rule: {Who(match)}, {Where(match)})",
            { Steps.Count: > 0 } => $"Here and now: does {widgets.StepNames(match.Steps)} (rule: {Who(match)}, {Where(match)})",
            _ => "Here and now: nothing (no rule fits your job and where you are)",
        });

        int? remove = null;
        (int From, int To)? move = null;
        for (var i = 0; i < binding.Rules.Count; i++)
        {
            ImGui.PushID(binding.Rules[i].Uid);
            changed |= DrawRule(binding.Rules[i], i, binding.Rules.Count, ref remove, ref move);
            ImGui.PopID();
        }

        if (remove is { } index)
        {
            binding.Rules.RemoveAt(index);
            changed = true;
        }
        if (move is var (from, to))
        {
            (binding.Rules[from], binding.Rules[to]) = (binding.Rules[to], binding.Rules[from]);
            changed = true;
        }

        if (ImGui.SmallButton("Add rule"))
        {
            binding.Rules.Add(new Rule());
            changed = true;
        }

        return changed;
    }

    // One rule: arrows to reorder, who, where, remove, then what it runs underneath.
    private bool DrawRule(Rule rule, int index, int count, ref int? remove, ref (int, int)? move)
    {
        var changed = false;
        ImGui.Indent();

        ImGui.BeginDisabled(index == 0);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowUp))
            move = (index, index - 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(index == count - 1);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowDown))
            move = (index, index + 1);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Rules are checked top to bottom; the first that fits is used.\nPut specific rules first and a catch-all (Anyone, Anywhere) last.");

        ImGui.SameLine();
        var who = WhoKey(rule);
        if (widgets.Combo("##who", ref who, WhoOptions(), searchable: true, width: 200f))
        {
            (rule.Job, rule.Group) = ParseWho(who);
            changed = true;
        }

        ImGui.SameLine();
        var where = WhereKey(rule);
        if (widgets.Combo("##where", ref where, WhereOptions(), searchable: true, width: 200f))
        {
            (rule.Where, rule.ContentType) = ParseWhere(where);
            changed = true;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Times))
            remove = index;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove this rule");

        // What the rule does: run its list, or act as a key held while the button is.
        ImGui.Indent();
        var actsAsKey = rule.ActsAsKey || recordingKey == rule;
        if (widgets.Combo("##mode", ref actsAsKey, [(false, "Do something"), (true, "Hold a key")], width: 120f))
        {
            if (actsAsKey)
                StartKeyRecording(rule);
            else
            {
                rule.HoldKey = 0;
                rule.HoldCtrl = rule.HoldShift = rule.HoldAlt = false;
                if (recordingKey == rule)
                    StopRecording();
            }
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Do something: run macros, commands, menus, items, emotes… one after another.\nHold a key: hold a key down while the button is, e.g. a Flick Wheel wheel's own hotkey,\nso the button opens that wheel: hold, flick, let go.");

        if (actsAsKey)
        {
            ImGui.SameLine();
            var label = recordingKey == rule ? "Press the key… (Esc cancels)"
                        : rule.ActsAsKey ? KeyPresser.Describe(rule) : "Not set: click, then press a key";
            if (ImGui.Button($"{label}###key"))
            {
                if (recordingKey == rule)
                    StopRecording();
                else
                    StartKeyRecording(rule);
            }
        }
        else
        {
            changed |= widgets.StepList(rule.Steps, "this rule", rule.Steps.Add);
        }
        ImGui.Unindent();
        ImGui.Unindent();
        return changed;
    }

    private void StartKeyRecording(Rule rule)
    {
        StopRecording();
        recordingKey = rule;
        watcher.Paused = true;
    }

    private void StartRecording(Binding binding)
    {
        recording = binding;
        recordedButtons = GamepadButtons.None;
        watcher.Paused = true;
    }

    // While recording: pad buttons are collected until all are let go; a key (other than a
    // modifier) is taken as soon as it's pressed, with the modifiers held with it.
    private bool Record()
    {
        if (recordingKey is { } keyRule)
            return RecordKey(keyRule);

        if (recording is not { } binding)
            return false;

        if (BindingWatcher.IsDown(VirtualKey.ESCAPE))
        {
            StopRecording();
            return false;
        }

        var held = watcher.HeldButtons();
        if (held != GamepadButtons.None)
        {
            recordedButtons |= held;
            return false;
        }

        if (recordedButtons != GamepadButtons.None)
        {
            binding.Buttons = recordedButtons;
            binding.Key = 0;
            binding.Ctrl = binding.Shift = binding.Alt = false;
            StopRecording();
            return true;
        }

        foreach (var key in keys)
        {
            if (!BindingWatcher.IsDown(key))
                continue;
            binding.Key = key;
            binding.Buttons = GamepadButtons.None;
            binding.Ctrl = BindingWatcher.IsDown(VirtualKey.CONTROL);
            binding.Shift = BindingWatcher.IsDown(VirtualKey.SHIFT);
            binding.Alt = BindingWatcher.IsDown(VirtualKey.MENU);
            StopRecording();
            return true;
        }

        return false;
    }

    // The key a rule acts as: taken as soon as a key (not a modifier) is pressed, with the
    // modifiers held with it.
    private bool RecordKey(Rule rule)
    {
        if (BindingWatcher.IsDown(VirtualKey.ESCAPE))
        {
            StopRecording();
            return false;
        }

        foreach (var key in keys)
        {
            if (!BindingWatcher.IsDown(key))
                continue;
            rule.HoldKey = key;
            rule.HoldCtrl = BindingWatcher.IsDown(VirtualKey.CONTROL);
            rule.HoldShift = BindingWatcher.IsDown(VirtualKey.SHIFT);
            rule.HoldAlt = BindingWatcher.IsDown(VirtualKey.MENU);
            StopRecording();
            return true;
        }

        return false;
    }

    private static string Describe(Binding binding)
    {
        if (binding.IsKeyboard)
        {
            var parts = new List<string>();
            if (binding.Ctrl)
                parts.Add("Ctrl");
            if (binding.Shift)
                parts.Add("Shift");
            if (binding.Alt)
                parts.Add("Alt");
            parts.Add(binding.Key.GetFancyName());
            return string.Join(" + ", parts);
        }

        return string.Join(" + ", PadNames.Where(p => binding.Buttons.HasFlag(p.Key)).Select(p => p.Value));
    }

    // Who: "" anyone, "g:Tank" a role group, "j:WHM" one job.
    private static string WhoKey(Rule rule) =>
        rule.Job != null ? $"j:{rule.Job}" : rule.Group is { } group ? $"g:{group}" : string.Empty;

    private static (string? Job, JobGroup? Group) ParseWho(string key) =>
        key.StartsWith("j:") ? (key[2..], null) :
        key.StartsWith("g:") && Enum.TryParse<JobGroup>(key[2..], out var group) ? (null, group) :
        (null, null);

    private List<(string Value, string Label)> WhoOptions()
    {
        var options = new List<(string, string)> { (string.Empty, "Anyone") };
        options.AddRange(JobGroups.All.Select(g => ($"g:{g.Group}", g.Label)));
        foreach (var (_, _, jobs) in JobGroups.All)
        {
            foreach (var abbreviation in jobs)
            {
                if (groups.Job(abbreviation) is { } job)
                    options.Add(($"j:{abbreviation}", $"{job.Abbreviation.ExtractText()} – {JobGroups.DisplayName(job)}"));
            }
        }

        return options;
    }

    private string Who(Rule rule) =>
        WhoOptions().FirstOrDefault(o => o.Value == WhoKey(rule)).Label ?? "Anyone";

    // Where: "a" anywhere, "o" outside duties, "d" any duty, "t:5" one duty type.
    private static string WhereKey(Rule rule) => rule.Where switch
    {
        DutyScope.OutsideDuty => "o",
        DutyScope.AnyDuty => "d",
        DutyScope.ContentType => $"t:{rule.ContentType}",
        _ => "a",
    };

    private static (DutyScope, uint) ParseWhere(string key) => key switch
    {
        "o" => (DutyScope.OutsideDuty, 0),
        "d" => (DutyScope.AnyDuty, 0),
        _ when key.StartsWith("t:") && uint.TryParse(key[2..], out var id) => (DutyScope.ContentType, id),
        _ => (DutyScope.Anywhere, 0),
    };

    private List<(string Value, string Label)> WhereOptions()
    {
        var options = new List<(string, string)> { ("a", "Anywhere"), ("o", "Outside duties"), ("d", "In any duty") };
        options.AddRange(duties.Kinds().Select(k => ($"t:{k.Id}", $"In {k.Name}")));
        return options;
    }

    private string Where(Rule rule) =>
        WhereOptions().FirstOrDefault(o => o.Value == WhereKey(rule)).Label ?? "Anywhere";
}
