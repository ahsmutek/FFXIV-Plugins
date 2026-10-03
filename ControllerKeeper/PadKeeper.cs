using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace ControllerKeeper;

internal readonly record struct Controller(int Index, Guid Instance, Guid Product, string Name, string InstanceName, bool Active);

/// <summary>
/// Switches the game back to the controller you chose whenever it's using another one
/// (after a rescan, a loading screen, or the device being plugged back in). Checks twice a
/// second; does nothing while your controller isn't connected.
/// </summary>
internal sealed class PadKeeper : IDisposable
{
    private const long CheckMs = 500;

    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private long nextCheck;

    public PadKeeper(IFramework framework, IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.log = log;
        this.config = config;
        framework.Update += OnUpdate;
    }

    public void Dispose() => framework.Update -= OnUpdate;

    /// <summary>Times the game was switched back this session.</summary>
    public int Switches { get; private set; }

    /// <summary>The controllers the game knows about right now.</summary>
    public static unsafe List<Controller> Controllers()
    {
        var list = new List<Controller>();
        var pad = Pad();
        if (pad == null)
            return list;

        var active = pad->GetActiveDeviceIndex();
        var devices = pad->GamepadDevices.AsSpan();
        for (var i = 0; i < devices.Length; i++)
        {
            ref var info = ref devices[i].DeviceInfo;
            list.Add(new Controller(i, info.Instance, info.Product, info.ProductNameString.Trim(),
                                    info.InstanceNameString.Trim(), i == active));
        }

        return list;
    }

    /// <summary>The chosen controller's index among the connected ones, or null.</summary>
    public int? ChosenIndex(List<Controller> controllers)
    {
        if (config.Instance is not { } instance)
            return null;

        // The same device first; failing that, the same model and name (a new instance
        // after being plugged into another port).
        foreach (var c in controllers)
        {
            if (c.Instance == instance)
                return c.Index;
        }
        foreach (var c in controllers)
        {
            if (c.Product == config.Product && c.Name == config.Name)
                return c.Index;
        }

        return null;
    }

    public void Choose(Controller controller)
    {
        config.Instance = controller.Instance;
        config.Product = controller.Product;
        config.Name = controller.Name;
    }

    private static unsafe PadDevice* Pad()
    {
        var manager = InputDeviceManager.Instance();
        return manager == null ? null : manager->PadDevice;
    }

    private unsafe void OnUpdate(IFramework _)
    {
        var now = Environment.TickCount64;
        if (!config.KeepChosen || config.Instance == null || now < nextCheck)
            return;
        nextCheck = now + CheckMs;

        var controllers = Controllers();
        if (ChosenIndex(controllers) is not { } index || controllers[index].Active)
            return;

        var pad = Pad();
        if (pad == null)
            return;

        pad->SetActiveDevice(index);
        Switches++;
        log.Information($"Switched back to {controllers[index].Name} (controller {index + 1} of {controllers.Count}).");
    }
}
