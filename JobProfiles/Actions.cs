using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace JobProfiles;

internal static class Actions
{
    public const int HudLayouts = 4;

    // -1 before the game has loaded one.
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

    public static unsafe bool MacroRunning()
    {
        var shell = RaptureShellModule.Instance();
        return shell != null && shell->MacroCurrentLine >= 0;
    }
}
