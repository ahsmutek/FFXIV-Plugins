using System.Collections.Generic;
using System.Linq;

using Shortcuts;

namespace FlickWheel;

// The combat and crafting & gathering wheels: every job in a fixed, role-grouped spot,
// plus the logs you pick (Crafting, Gathering, Fishing Log, Fish Guide).
internal sealed class JobsModule : IWheelModule
{
    public static readonly WheelKind[] Kinds = [WheelKind.Combat, WheelKind.Crafting];

    private readonly Configuration config;
    private readonly JobCatalog catalog;
    private readonly JobSwitcher switcher;
    private readonly JobsSettings settings;

    public JobsModule(Configuration config, JobCatalog catalog, JobSwitcher switcher, IconCache icons, WheelOverlay overlay)
    {
        this.config = config;
        this.catalog = catalog;
        this.switcher = switcher;
        settings = new JobsSettings(config, catalog, icons, overlay);
        Wheels = Kinds.Select(kind => (IWheel)new JobsWheel(this, kind)).ToArray();
        SettingsTabs = [new("Job layout", settings.DrawLayout), new("Gear sets", settings.DrawGearsets)];
    }

    public string Id => "jobs";
    public string Name => "Jobs";
    public IReadOnlyList<IWheel> Wheels { get; }
    public IReadOnlyList<SettingsTab> SettingsTabs { get; }

    public static string WheelId(WheelKind kind) => kind == WheelKind.Combat ? "combat" : "crafting";

    public static string Title(WheelKind kind) =>
        kind == WheelKind.Combat ? "Combat wheel" : "Crafting & gathering wheel";

    public void OnSettingsOpen() => settings.Refresh();

    // Jobs without a gear set are faded. Log entries never are.
    private WheelEntry ToEntry(WheelJob job) =>
        new(job.Slot, job.Name, job.IconId, () => switcher.Switch(job),
            KeyOutIcon: job.CommandId != 0,
            Faded: job.CommandId == 0 && job.GearsetIndex < 0,
            IsCurrent: job.IsCurrent);

    private sealed class JobsWheel(JobsModule module, WheelKind kind) : IWheel
    {
        public string Id => WheelId(kind);
        public string Title => JobsModule.Title(kind);
        public WheelRing Layout() => JobLayout.Build(kind, module.config);
        public List<WheelEntry> Entries(WheelRing ring) => module.catalog.Build(ring).Select(module.ToEntry).ToList();
    }
}
