using System;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace CatProof;

// Turns key presses from physical keyboards into WM_NULL as the game's message loop takes them from the queue,
// before TranslateMessage makes text of them and before the game, Dalamud or any window subclass sees them.
// GetMessageW isn't hooked: threads blocked inside it would keep a frame in this plugin, so it couldn't unload safely.
internal sealed unsafe class KeyLock : IDisposable
{
    private const uint WmNull = 0x0000;
    private const uint WmInput = 0x00FF;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const int ImoHardware = 0x0001;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 0x0001;

    private delegate int PeekMessageDelegate(Msg* msg, nint hwnd, uint min, uint max, uint remove);

    private readonly IPluginLog log;
    private readonly Hook<PeekMessageDelegate> peek;
    private volatile bool locked;
    private volatile bool rawKeyboard;
    private bool? loggedRaw;

    public KeyLock(IGameInteropProvider interop, IPluginLog log)
    {
        this.log = log;
        peek = interop.HookFromSymbol<PeekMessageDelegate>("user32.dll", "PeekMessageW", OnPeek);
        peek.Enable();
    }

    public void Dispose()
    {
        locked = false;
        peek.Dispose();
    }

    public bool Locked
    {
        get => locked;
        set
        {
            if (value == locked)
                return;
            if (value)
                CheckRawInput();
            locked = value;
            log.Information(value ? "Keyboard locked." : "Keyboard unlocked.");
        }
    }

    private int OnPeek(Msg* msg, nint hwnd, uint min, uint max, uint remove)
    {
        var result = peek.Original(msg, hwnd, min, max, remove);
        if (result != 0 && locked && Blocked(msg))
            msg->Message = WmNull;
        return result;
    }

    // Releases always pass, so keys held while locking come back up.
    private bool Blocked(Msg* msg) => msg->Message switch
    {
        WmKeyDown or WmSysKeyDown => GetCurrentInputMessageSource(out var source) && source.Origin == ImoHardware,
        WmInput => rawKeyboard && RawKeyPress(msg->LParam),
        _ => false,
    };

    // SendInput's raw input has no device handle.
    private static bool RawKeyPress(nint handle)
    {
        RawKeyboard data;
        var size = (uint)sizeof(RawKeyboard);
        if (GetRawInputData(handle, RidInput, &data, ref size, (uint)sizeof(RawInputHeader)) == uint.MaxValue)
            return false;
        return data.Header.Type == RimTypeKeyboard && data.Header.Device != 0 && (data.Flags & RiKeyBreak) == 0;
    }

    private void CheckRawInput()
    {
        uint count = 0;
        var size = (uint)sizeof(RawInputDevice);
        GetRegisteredRawInputDevices(null, ref count, size);
        var devices = new RawInputDevice[count];
        if (count > 0 && GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue)
            devices = [];
        rawKeyboard = devices.Any(d => d.UsagePage == 1 && d.Usage == 6);
        if (loggedRaw != rawKeyboard)
            log.Information(rawKeyboard
                ? "The game reads the keyboard through Raw Input too; blocking those presses as well."
                : "The game doesn't read the keyboard through Raw Input.");
        loggedRaw = rawKeyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputMessageSource
    {
        public int DeviceType;
        public int Origin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public RawInputHeader Header;
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCurrentInputMessageSource(out InputMessageSource source);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint handle, uint command, void* data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern uint GetRegisteredRawInputDevices(RawInputDevice[]? devices, ref uint count, uint size);
}
