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
    bool DrawSettings();
}

internal interface IWheelModule
{
    IReadOnlyList<IWheel> Wheels { get; }
    void OnSettingsOpen();
}

internal sealed class WheelSet
{
    private readonly List<IWheelModule> modules = [];

    public IReadOnlyList<IWheelModule> Modules => modules;

    public void Add(IWheelModule module) => modules.Add(module);

    public List<IWheel> Wheels => modules.SelectMany(m => m.Wheels).ToList();

    public List<IWheel> CycleOf(WheelCycle cycle) =>
        cycle.Wheels.Select(Find).OfType<IWheel>().ToList();

    public IWheel? Find(string id) => Wheels.FirstOrDefault(w => w.Id == id);
}
