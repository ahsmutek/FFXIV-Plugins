using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;
using Icons;

namespace JobProfiles;

internal sealed class ButtonsTab
{
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
    private readonly Duties duties;
    private readonly BindingWatcher watcher;
    private readonly Widgets widgets;
    private readonly FlickWheelLink flickWheel;
    private readonly VirtualKey[] keys;

    private Binding? recording;
    private GamepadButtons recordedButtons;
    // Held when recording started, like the A that pressed Record; left out until let go.
    private GamepadButtons heldAtStart;
    private readonly HashSet<VirtualKey> recordedKeys = [];

    private static readonly VirtualKey[] Modifiers = [VirtualKey.CONTROL, VirtualKey.SHIFT, VirtualKey.MENU];
    private Rule? recordingKey;
    // The line just added: outlined for a moment, and scrolled to once.
    private (string Uid, long At, bool Scroll) added = (string.Empty, 0, false);

    public ButtonsTab(Configuration config, Duties duties, BindingWatcher watcher, Widgets widgets,
                      FlickWheelLink flickWheel, IKeyState keyState)
    {
        this.config = config;
        this.duties = duties;
        this.watcher = watcher;
        this.widgets = widgets;
        this.flickWheel = flickWheel;
        keys = keyState.GetValidVirtualKeys()
                       .Where(k => k is not (VirtualKey.CONTROL or VirtualKey.SHIFT or VirtualKey.MENU or
                                             VirtualKey.LCONTROL or VirtualKey.RCONTROL or VirtualKey.LSHIFT or
                                             VirtualKey.RSHIFT or VirtualKey.LMENU or VirtualKey.RMENU or
                                             VirtualKey.LBUTTON or VirtualKey.RBUTTON or VirtualKey.MBUTTON or
                                             VirtualKey.XBUTTON1 or VirtualKey.XBUTTON2))
                       .ToArray();
    }

    public bool Recording => recording != null || recordingKey != null;

    public void StopRecording()
    {
        recording = null;
        recordingKey = null;
        recordedButtons = GamepadButtons.None;
        recordedKeys.Clear();
        watcher.Paused = false;
    }

    public bool DrawButtons()
    {
        var changed = Record();

        var enabled = config.BindingsEnabled;
        if (ImGui.Checkbox("Use controller buttons and keys", ref enabled))
        {
            config.BindingsEnabled = enabled;
            changed = true;
        }

        ImGui.TextWrapped("Add the controller buttons, button combos and keyboard keys you want to use. What each one " +
                          "does is set per role or job on the Jobs tab.");
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextWrapped("Tip: controller buttons are hidden from the game while they do something here, so you don't " +
                          "have to unbind them in its settings (untick Hide from the game to let it have them too). " +
                          "For paddles, map each one to a key the game doesn't use (F13–F24) in reWASD or Steam Input, " +
                          "then record that key here.");
        ImGui.PopStyleColor();

        Binding? delete = null;
        foreach (var binding in config.Bindings)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushID(binding.Uid);
            changed |= DrawBinding(binding, ref delete);
            ImGui.PopID();
        }

        if (delete != null)
        {
            config.Bindings.Remove(delete);
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Add button or key"))
        {
            var binding = new Binding();
            config.Bindings.Add(binding);
            StartRecording(binding);
            changed = true;
        }

        return changed;
    }

    private bool DrawBinding(Binding binding, ref Binding? delete)
    {
        var changed = false;

        var label = recording == binding ? "Hold the buttons and keys, then let go… (Esc cancels)" :
                    binding.IsSet ? Describe(binding) : "Not set: click, then press it";
        if (ImGui.Button($"{label}###record"))
        {
            if (recording == binding)
                StopRecording();
            else
                StartRecording(binding);
        }
        if (ImGui.IsItemHovered() && recording != binding)
            ImGui.SetTooltip("Click, then hold everything it takes and let go: a controller button, a key, or any mix,\ne.g. LB + Start, Ctrl + ' + K, or Ctrl + ' (a paddle) + A.");

        ImGui.SameLine();
        var name = binding.Name;
        ImGui.SetNextItemWidth(180f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        if (ImGui.InputTextWithHint("##name", "Name, e.g. Emotes wheel", ref name, 40))
        {
            binding.Name = name;
            changed = true;
        }

        if (binding.Buttons != GamepadButtons.None)
        {
            ImGui.SameLine();
            var hide = binding.HideFromGame;
            if (ImGui.Checkbox("Hide from the game", ref hide))
            {
                binding.HideFromGame = hide;
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The game doesn't see this button while it does something here, so you don't have to\nunbind it in the game's settings. Where nothing is set up for it, the game gets it as usual.\nIn a combo, LB, LT, RB and RT still reach the game (the cross hotbar needs them).");
        }

        ImGui.SameLine();
        var canDelete = binding.Rules.Count == 0 || ImGui.GetIO().KeyCtrl;
        ImGui.BeginDisabled(!canDelete);
        if (ImGui.SmallButton("Delete"))
        {
            delete = binding;
            if (recording == binding)
                StopRecording();
        }
        ImGui.EndDisabled();
        if (!canDelete && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Hold Ctrl to delete: it does something on some of your role or job pages.");

        var match = watcher.Match(binding);
        ImGui.TextDisabled(match switch
        {
            { OpensWheel: true } => $"Here and now: opens Flick Wheel's {flickWheel.NameOf(match.WheelHotkey!)} (from {WhoLabel(match)}, {WhereLabel(match)})",
            { ActsAsKey: true } => $"Here and now: holds {KeyPresser.Describe(match)} (from {WhoLabel(match)}, {WhereLabel(match)})",
            { Steps.Count: > 0 } => $"Here and now: does {widgets.StepNames(match.Steps)} (from {WhoLabel(match)}, {WhereLabel(match)})",
            _ => "Here and now: nothing",
        });

        if (binding.Rules.Count == 0)
        {
            ImGui.TextDisabled("Not set up anywhere yet: pick a role or job on the Jobs tab and add button behaviour.");
        }
        else if (ImGui.TreeNode($"Set up in {binding.Rules.Count} place{(binding.Rules.Count == 1 ? "" : "s")}###setup"))
        {
            var rules = binding.Rules.OrderBy(r => r.Job != null ? 3 : r.Group == JobGroup.Combat ? 1 : r.Group != null ? 2 : 0)
                               .ThenBy(r => (int?)r.Group ?? -1)
                               .ThenBy(r => r.Job)
                               .ThenBy(WhereLabel);
            foreach (var rule in rules)
            {
                var what = rule.OpensWheel ? $"opens Flick Wheel's {flickWheel.NameOf(rule.WheelHotkey!)}"
                         : rule.ActsAsKey ? $"holds {KeyPresser.Describe(rule)}"
                         : rule.Steps.Count > 0 ? $"does {widgets.StepNames(rule.Steps)}"
                         : "nothing yet";
                ImGui.BulletText($"{WhoLabel(rule)} · {WhereLabel(rule)}: {what}");
            }
            ImGui.TreePop();
        }

        return changed;
    }

    public bool DrawBehaviour(string who)
    {
        var changed = Record();

        if (config.Bindings.Count == 0)
        {
            ImGui.TextDisabled("Add a button or key on the Buttons tab first.");
            return changed;
        }

        (Binding Binding, Rule Rule)? remove = null;
        (Binding From, Binding To, Rule Rule)? move = null;
        var rules = config.Bindings.SelectMany(b => b.Rules.Where(r => WhoKey(r) == who).Select(r => (Binding: b, Rule: r)))
                          .OrderBy(x => x.Rule.Created)
                          .ToList();
        foreach (var (binding, rule) in rules)
        {
            ImGui.PushID(rule.Uid);
            ImGui.BeginGroup();
            changed |= DrawRule(binding, rule, ref remove, ref move);
            ImGui.EndGroup();
            if (rule.Uid == added.Uid && Environment.TickCount64 - added.At < 2000)
            {
                ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin() - new Vector2(4f), ImGui.GetItemRectMax() + new Vector2(4f),
                                                  ImGui.GetColorU32(ImGuiCol.CheckMark), 4f, ImDrawFlags.None, 2f);
                if (added.Scroll)
                {
                    ImGui.SetScrollHereY(1f);
                    added.Scroll = false;
                }
            }
            ImGui.PopID();
        }

        if (remove is var (fromBinding, removed))
        {
            fromBinding.Rules.Remove(removed);
            changed = true;
        }
        if (move is var (from, to, moved))
        {
            from.Rules.Remove(moved);
            to.Rules.Add(moved);
            changed = true;
        }

        if (ImGui.SmallButton("+ Button behaviour"))
        {
            var (job, group) = ParseWho(who);
            var rule = new Rule { Job = job, Group = group, Created = DateTime.UtcNow.Ticks };
            config.Bindings[0].Rules.Add(rule);
            added = (rule.Uid, Environment.TickCount64, true);
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Make one of your buttons do something for this page. If several pages set the same button,\n" +
                             "the most specific wins: a job beats its role, a role beats Combat jobs, which beats Everyone,\n" +
                             "and kinds of duty (In Raids) beat other places, which beat Anywhere.");
        return changed;
    }

    private bool DrawRule(Binding binding, Rule rule, ref (Binding, Rule)? remove, ref (Binding, Binding, Rule)? move)
    {
        var changed = false;

        var target = binding.Uid;
        var buttons = config.Bindings.Select(b => (b.Uid, ButtonLabel(b))).ToList();
        if (widgets.Combo("##button", ref target, buttons, width: 120f) && target != binding.Uid)
            move = (binding, config.Bindings.First(b => b.Uid == target), rule);

        ImGui.SameLine();
        changed |= widgets.MultiCombo("##where", rule.Places, PlaceOptions(), "Anywhere", searchable: true, width: 140f);

        ImGui.SameLine();
        var mode = rule.WheelHotkey != null ? Mode.Wheel : rule.ActsAsKey || recordingKey == rule ? Mode.Key : Mode.Steps;
        if (widgets.Combo("##mode", ref mode, [(Mode.Steps, "Do something"), (Mode.Key, "Hold a key"), (Mode.Wheel, "Open Flick Wheel")],
                          width: 140f))
        {
            rule.HoldKey = 0;
            rule.HoldCtrl = rule.HoldShift = rule.HoldAlt = false;
            rule.WheelHotkey = null;
            if (recordingKey == rule)
                StopRecording();
            if (mode == Mode.Key)
                StartKeyRecording(rule);
            else if (mode == Mode.Wheel)
                rule.WheelHotkey = flickWheel.Hotkeys().FirstOrDefault().Uid ?? string.Empty;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Do something: run macros, commands, menus, items, emotes… one after another.\n" +
                             "Hold a key: hold a key down while the button is.\n" +
                             "Open Flick Wheel: open one of Flick Wheel's hotkeys while the button is held: hold, flick, let go.");

        ImGui.SameLine();
        if (TablerIcons.Button(TablerIcon.X))
            remove = (binding, rule);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove");

        ImGui.Indent();
        if (mode == Mode.Wheel)
        {
            changed |= DrawWheelChoice(rule);
        }
        else if (mode == Mode.Key)
        {
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
            changed |= widgets.StepList(rule.Steps, ButtonLabel(binding), rule.Steps.Add);
        }
        ImGui.Unindent();
        ImGui.Spacing();
        return changed;
    }

    private enum Mode
    {
        Steps,
        Key,
        Wheel,
    }

    private bool DrawWheelChoice(Rule rule)
    {
        if (!flickWheel.Available)
        {
            ImGui.TextDisabled("Load Flick Wheel to choose one of its hotkeys.");
            return false;
        }

        var options = flickWheel.Hotkeys().Select(h => (h.Uid, h.Name)).ToList();
        if (rule.WheelHotkey is { Length: > 0 } current && options.All(o => o.Uid != current))
            options.Insert(0, (current, "(deleted hotkey)"));
        if (options.Count == 0)
        {
            ImGui.TextDisabled("Add a hotkey in Flick Wheel first.");
            return false;
        }

        var uid = rule.WheelHotkey ?? string.Empty;
        if (!widgets.Combo("##wheel", ref uid, options, width: 200f))
            return false;
        rule.WheelHotkey = uid;
        return true;
    }

    private void StartKeyRecording(Rule rule)
    {
        StopRecording();
        recordingKey = rule;
        watcher.Paused = true;
    }

    private void StartRecording(Binding binding)
    {
        StopRecording();
        recording = binding;
        heldAtStart = watcher.HeldButtons();
        watcher.Paused = true;
    }

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
        heldAtStart &= held;
        held &= ~heldAtStart;
        var keysHeld = keys.Concat(Modifiers).Where(BindingWatcher.IsDown).ToList();
        if (held != GamepadButtons.None || keysHeld.Count > 0)
        {
            recordedButtons |= held;
            recordedKeys.UnionWith(keysHeld);
            return false;
        }

        if (recordedButtons == GamepadButtons.None && recordedKeys.Count == 0)
            return false;

        binding.Buttons = recordedButtons;
        binding.Keys = recordedKeys.OrderBy(k => Array.IndexOf(Modifiers, k) is var i and >= 0 ? i : Modifiers.Length)
                                   .ThenBy(k => k)
                                   .ToList();
        StopRecording();
        return true;
    }

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

    public static string ButtonLabel(Binding binding) =>
        binding.Name.Trim().Length > 0 ? binding.Name.Trim() : binding.IsSet ? Describe(binding) : "(not set)";

    private static string Describe(Binding binding)
    {
        var parts = binding.Keys.Select(k => k switch
        {
            VirtualKey.CONTROL => "Ctrl",
            VirtualKey.SHIFT => "Shift",
            VirtualKey.MENU => "Alt",
            _ => k.GetFancyName(),
        });
        return string.Join(" + ", parts.Concat(PadNames.Where(p => binding.Buttons.HasFlag(p.Key)).Select(p => p.Value)));
    }

    // Who a rule is for: "" everyone, "g:Tank" a role, "j:WHM" one job.
    public static string WhoKey(Rule rule) =>
        rule.Job != null ? $"j:{rule.Job}" : rule.Group is { } group ? $"g:{group}" : string.Empty;

    public static (string? Job, JobGroup? Group) ParseWho(string key) =>
        key.StartsWith("j:") ? (key[2..], null) :
        key.StartsWith("g:") && Enum.TryParse<JobGroup>(key[2..], out var group) ? (null, group) :
        (null, null);

    private string WhoLabel(Rule rule) =>
        rule.Job != null ? rule.Job :
        rule.Group is { } group ? JobGroups.Label(group) :
        "Everyone";

    private List<(string Value, string Label)> PlaceOptions()
    {
        var options = new List<(string, string)>
        {
            ("o", "Outside duties"), ("d", "In any duty"), ("c", "In combat"), ("p", "In a party"),
        };
        options.AddRange(duties.Kinds().Select(k => ($"t:{k.Id}", $"In {k.Name}")));
        return options;
    }

    private string WhereLabel(Rule rule)
    {
        if (rule.Places.Count == 0)
            return "Anywhere";
        var options = PlaceOptions();
        return string.Join(" or ", rule.Places.Select(p => options.FirstOrDefault(o => o.Value == p).Label ?? p));
    }
}
