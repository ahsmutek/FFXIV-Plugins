using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;

namespace ControllerKeeper;

// The game rescans controllers (freezing pad input) on every WM_DEVICECHANGE, for any hardware; only pass it on when the set of controllers changed.
internal sealed class RescanFilter : IDisposable
{
    private const uint WmDeviceChange = 0x0219;
    private const nint DbtDevNodesChanged = 0x0007;
    private const long RecheckMs = 1500;
    private static readonly nuint SubclassId = 0xC0417;

    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly SubclassProc proc;
    private nint window;
    private bool attachTried;

    private HashSet<string> known;
    private long? recheckAt;
    private bool forceNext;

    public RescanFilter(IFramework framework, IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.log = log;
        this.config = config;
        known = Controllers();
        proc = WndProc; // kept in a field so the delegate outlives the native subclass
        // Must attach from the thread that owns the window.
        framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnUpdate;
        if (window == 0)
            return;
        var hwnd = window;
        window = 0;
        framework.RunOnFrameworkThread(() => RemoveWindowSubclass(hwnd, proc, SubclassId)).Wait();
    }

    private void Attach()
    {
        attachTried = true;
        var hwnd = GameWindow();
        if (hwnd != 0 && SetWindowSubclass(hwnd, proc, SubclassId, 0))
            window = hwnd;
        else
            log.Warning("Couldn't attach to the game window; controller rescans won't be filtered.");
    }

    public int Skipped { get; private set; }
    public int Passed { get; private set; }

    public void Reconnect()
    {
        var hwnd = window != 0 ? window : GameWindow();
        if (hwnd == 0)
            return;
        forceNext = true;
        PostMessage(hwnd, WmDeviceChange, DbtDevNodesChanged, 0);
        log.Information("Asked the game to rescan its controllers.");
    }

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmDeviceChange && forceNext)
        {
            forceNext = false;
            known = Controllers();
            Passed++;
        }
        else if (message == WmDeviceChange && config.SkipRescans)
        {
            var now = Controllers();
            if (now.SetEquals(known))
            {
                Skipped++;
                recheckAt = Environment.TickCount64 + RecheckMs;
                return 1; // Handled; the game never sees it.
            }

            log.Information($"Controllers changed ({known.Count} → {now.Count}); letting the game rescan.");
            known = now;
            Passed++;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    // Windows can list a controller only after its message was skipped.
    private void OnUpdate(IFramework _)
    {
        if (!attachTried)
            Attach();

        if (window == 0 || recheckAt is not { } at || Environment.TickCount64 < at)
            return;
        recheckAt = null;
        if (!Controllers().SetEquals(known))
            PostMessage(window, WmDeviceChange, DbtDevNodesChanged, 0);
    }

    private static HashSet<string> Controllers()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
            return found;

        var devices = new RawInputDeviceList[count];
        if (GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue)
            return found;

        foreach (var device in devices.Take((int)count).Where(d => d.Type == RimTypeHid))
        {
            var info = new RidDeviceInfo { Size = (uint)Marshal.SizeOf<RidDeviceInfo>() };
            var infoSize = info.Size;
            if (GetRawInputDeviceInfo(device.Device, RidiDeviceInfo, ref info, ref infoSize) == uint.MaxValue ||
                info.Hid.UsagePage != 0x01 || info.Hid.Usage is not (0x04 or 0x05 or 0x08))
                continue;

            uint nameLength = 0;
            GetRawInputDeviceInfo(device.Device, RidiDeviceName, null, ref nameLength);
            var name = new char[nameLength];
            if (nameLength > 0 && GetRawInputDeviceInfo(device.Device, RidiDeviceName, name, ref nameLength) != uint.MaxValue)
                found.Add(new string(name).TrimEnd('\0'));
        }

        return found;
    }

    private static nint GameWindow()
    {
        var handle = Process.GetCurrentProcess().MainWindowHandle;
        if (handle != 0)
            return handle;

        nint result = 0;
        var pid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd))
                return true;
            result = hwnd;
            return false;
        }, 0);
        return result;
    }

    private const uint RimTypeHid = 2;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidiDeviceInfo = 0x2000000b;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RidDeviceInfoHid
    {
        public uint VendorId;
        public uint ProductId;
        public uint VersionNumber;
        public ushort UsagePage;
        public ushort Usage;
    }

    // RID_DEVICE_INFO: the union's largest member is the keyboard (6 uints), so the HID
    // part is padded to that size.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RidDeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public RidDeviceInfoHid Hid;
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceList[]? devices, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, ref RidDeviceInfo data, ref uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, [Out] char[]? data, ref uint size);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);
}
