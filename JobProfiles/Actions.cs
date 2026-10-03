using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace JobProfiles;

internal readonly record struct MacroChoice(uint Id, string Name);

// Switches the HUD layout and runs macros the way the game's own /hudlayout and macro
// buttons do.
internal sealed class Actions(IToastGui toasts)
{
    public const int HudLayouts = 4;
    private const uint MacroSlots = 100;

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

    public unsafe void RunMacro(uint id)
    {
        var macro = MacroOf(id);
        var shell = RaptureShellModule.Instance();
        if (macro == null || !macro->IsNotEmpty())
        {
            toasts.ShowError($"{MacroName(null, id)} is empty.");
            return;
        }

        if (shell != null)
            shell->ExecuteMacro(macro);
    }

    // Every macro slot with something in it, individual first.
    public static unsafe List<MacroChoice> Macros()
    {
        var list = new List<MacroChoice>();
        for (uint id = 0; id < MacroSlots * 2; id++)
        {
            var macro = MacroOf(id);
            if (macro != null && macro->IsNotEmpty())
                list.Add(new MacroChoice(id, MacroName(macro, id)));
        }

        return list;
    }

    // The macro's own name, or e.g. "Shared macro 12" when it has none or is gone.
    public static unsafe string MacroName(uint id) => MacroName(MacroOf(id), id);

    // Id 0–99 is individual macro slot N, 100–199 shared slot N − 100.
    private static unsafe RaptureMacroModule.Macro* MacroOf(uint id)
    {
        var module = RaptureMacroModule.Instance();
        return module == null || id >= MacroSlots * 2 ? null : module->GetMacro(id / MacroSlots, id % MacroSlots);
    }

    private static unsafe string MacroName(RaptureMacroModule.Macro* macro, uint id)
    {
        var name = macro != null && macro->IsNotEmpty() ? macro->Name.ToString().Trim() : string.Empty;
        return name.Length > 0 ? name : $"{(id < MacroSlots ? "Individual" : "Shared")} macro {id % MacroSlots}";
    }
}
