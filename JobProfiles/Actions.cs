using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace JobProfiles;

// Switches the HUD layout the way the game's own /hudlayout does, and tells when a macro is
// still running. Steps themselves run through the shared ShortcutCatalog.
internal static class Actions
{
    public const int HudLayouts = 4;

    // The 0-based HUD layout in use, or -1 before the game has loaded one.
    public static unsafe int CurrentHudLayout()
    {
        var config = AddonConfig.Instance();
        return config == null || config->ActiveDataSet == null ? -1 : (int)config->ActiveDataSet->CurrentHudLayout;
    }

    public static unsafe void ChangeHudLayout(int layout)
    {
        var config = AddonConfig.Instance();
        if (config != null && layout is >= 0 and < HudLayouts && layout != CurrentHudLayout())
            config->ChangeHudLayout((uint)layout, false, true);
    }

    // A macro is still working through its lines (or waiting on a /wait).
    public static unsafe bool MacroRunning()
    {
        var shell = RaptureShellModule.Instance();
        return shell != null && shell->MacroCurrentLine >= 0;
    }
}
