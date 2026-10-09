using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Keys;

namespace JobProfiles;

// SendInput, so plugins watching the keyboard (e.g. Flick Wheel) see the keys.
internal static class KeyPresser
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    public static void Down(Rule rule) => Send(Keys(rule), up: false);

    public static void Up(Rule rule)
    {
        var keys = Keys(rule);
        keys.Reverse();
        Send(keys, up: true);
    }

    public static string Describe(Rule rule)
    {
        var parts = new List<string>();
        if (rule.HoldCtrl)
            parts.Add("Ctrl");
        if (rule.HoldShift)
            parts.Add("Shift");
        if (rule.HoldAlt)
            parts.Add("Alt");
        parts.Add(rule.HoldKey.GetFancyName());
        return string.Join(" + ", parts);
    }

    private static List<VirtualKey> Keys(Rule rule)
    {
        var keys = new List<VirtualKey>();
        if (rule.HoldCtrl)
            keys.Add(VirtualKey.CONTROL);
        if (rule.HoldShift)
            keys.Add(VirtualKey.SHIFT);
        if (rule.HoldAlt)
            keys.Add(VirtualKey.MENU);
        keys.Add(rule.HoldKey);
        return keys;
    }

    private static void Send(List<VirtualKey> keys, bool up)
    {
        // No key, or a mouse button, which sent as keyboard input does nothing predictable.
        if (keys[^1] <= VirtualKey.XBUTTON2)
            return;

        var inputs = new Input[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            inputs[i].Type = InputKeyboard;
            inputs[i].Keyboard.VirtualKey = (ushort)keys[i];
            inputs[i].Keyboard.ScanCode = (ushort)MapVirtualKey((uint)keys[i], 0);
            inputs[i].Keyboard.Flags = up ? KeyUp : 0;
        }

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    // INPUT: the union's largest member is the mouse one, so the keyboard part is padded to it.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
