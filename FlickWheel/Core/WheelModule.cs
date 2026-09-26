using System;
using System.Collections.Generic;
using System.Linq;

namespace FlickWheel;

// One icon on a wheel. Run does whatever picking it means: switch job, open a window…
internal sealed record WheelEntry(
    int Slot,
    string Name,
    uint IconId,
    Action Run,
    bool KeyOutIcon = false,
    bool Faded = false,
    bool IsCurrent = false,
    int? Count = null);

// A wheel a module offers. Layout gives the fixed slot positions; Entries fills the
// slots that have something to show right now. Both are rebuilt each time it opens.
internal interface IWheel
{
    string Id { get; }
    string Title { get; }
    WheelRing Layout();
    List<WheelEntry> Entries(WheelRing ring);
}

// A settings tab a module adds. Draw returns true when it changed the config.
internal readonly record struct SettingsTab(string Title, Func<bool> Draw);

internal interface IWheelModule
{
    string Id { get; }
    string Name { get; }
    IReadOnlyList<IWheel> Wheels { get; }
    IReadOnlyList<SettingsTab> SettingsTabs { get; }
    // The settings window just opened; refresh anything that shows game state.
    void OnSettingsOpen();
}

// Every module, and which of them are turned on. Modules are added after the core
// pieces exist, since their settings use the overlay's preview.
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

    public IWheel? Find(string id) => Wheels.FirstOrDefault(w => w.Id == id);
}
