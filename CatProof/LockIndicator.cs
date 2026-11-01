using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

namespace CatProof;

internal sealed class LockIndicator : Window
{
    private static readonly string[] Messages =
    [
        "CatProof: keyboard locked",
        "Keyboard locked. Paws off!",
        "Keyboard locked. Nap on it all you like.",
        "Cat-proofed: the keys are asleep",
        "Keyboard locked. Mrrp denied.",
        "Keyboard locked. No typing, kitty.",
        "Keyboard locked. Walk across it freely.",
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly KeyLock keyLock;
    private bool placeDefault;
    private bool positionDirty;
    private string? message;

    public LockIndicator(Configuration config, Action save, KeyLock keyLock)
        : base("CatProof indicator###CatProofIndicator",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings)
    {
        this.config = config;
        this.save = save;
        this.keyLock = keyLock;
        IsOpen = true;
        RespectCloseHotkey = false;

        if (config.IndicatorX >= 0 && config.IndicatorY >= 0)
        {
            Position = new Vector2(config.IndicatorX, config.IndicatorY);
            PositionCondition = ImGuiCond.Once;
        }
        else
        {
            placeDefault = true;
        }
    }

    public void ResetPosition()
    {
        config.IndicatorX = config.IndicatorY = -1;
        save();
        placeDefault = true;
    }

    // Dalamud closes focused windows on a controller's B regardless of RespectCloseHotkey; reopen.
    public override void OnClose() => IsOpen = true;

    public override bool DrawConditions()
    {
        if (!keyLock.Locked)
            message = null;
        return keyLock.Locked || config.IconOnly;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;
        if (config.IconOnly)
            Flags |= ImGuiWindowFlags.NoBackground;

        if (placeDefault)
        {
            var viewport = ImGui.GetMainViewport();
            Position = viewport.Pos + new Vector2(viewport.Size.X / 2 - 130, 60);
            PositionCondition = ImGuiCond.Always;
            placeDefault = false;
        }
        else if (PositionCondition == ImGuiCond.Always)
        {
            Position = null;
        }
    }

    public override void Draw()
    {
        if (config.IconOnly)
            DrawIcon();
        else
            DrawMessage();

        var pos = ImGui.GetWindowPos();
        if (Math.Abs(pos.X - config.IndicatorX) >= 0.5f || Math.Abs(pos.Y - config.IndicatorY) >= 0.5f)
        {
            config.IndicatorX = pos.X;
            config.IndicatorY = pos.Y;
            positionDirty = true;
        }

        if (positionDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            save();
            positionDirty = false;
        }
    }

    private void DrawMessage()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudOrange, message ??= Messages[Random.Shared.Next(Messages.Length)]);
        ImGui.SameLine();
        if (ImGui.Button("Unlock"))
            keyLock.Locked = false;
    }

    // Text, not a button, so the icon can also be dragged; a click without a drag toggles.
    private void DrawIcon()
    {
        var locked = keyLock.Locked;
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(locked ? ImGuiColors.DalamudOrange : ImGuiColors.DalamudGrey3, config.Icon.ToIconString());
        ImGui.PopFont();
        if (!ImGui.IsItemHovered())
            return;
        ImGui.SetTooltip(locked ? "CatProof: keyboard locked\nClick to unlock" : "CatProof: keyboard unlocked\nClick to lock");
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
            ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).LengthSquared() < 25f)
            keyLock.Locked = !locked;
    }
}
