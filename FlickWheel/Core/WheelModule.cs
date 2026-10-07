using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickWheel;

internal sealed record WheelEntry(
    int Slot,
    string Name,
    uint IconId,
    Action Run,
    bool KeyOutIcon = false,
    bool Faded = false,
    bool IsCurrent = false,
    int? Count = null);

internal interface IWheel
{
    string Id { get; }
    string Title { get; }
    WheelRing Layout();
    List<WheelEntry> Entries(WheelRing ring);
    bool InCycle => true;
}

internal readonly record struct SettingsTab(string Title, Func<bool> Draw);

internal interface IWheelModule
{
    string Id { get; }
    string Name { get; }
    IReadOnlyList<IWheel> Wheels { get; }
    IReadOnlyList<SettingsTab> SettingsTabs { get; }
    void OnSettingsOpen();
}

internal sealed class WheelSet(Configuration config)
{
    private readonly List<IWheelModule> modules = [];

    public IReadOnlyList<IWheelModule> Modules => modules;

    public void Add(IWheelModule module) => modules.Add(module);

    public bool IsEnabled(IWheelModule module) =>
        !config.Modules.TryGetValue(module.Id, out var on) || on;

    public bool IsEnabled(IWheel wheel) =>
        !config.WheelsOn.TryGetValue(wheel.Id, out var on) || on;

    public List<IWheel> Wheels => modules.Where(IsEnabled).SelectMany(m => m.Wheels).Where(IsEnabled).ToList();

    public List<IWheel> CycleWheels => Wheels.Where(w => w.InCycle).ToList();

    public IWheel? Find(string id) => Wheels.FirstOrDefault(w => w.Id == id);
}
