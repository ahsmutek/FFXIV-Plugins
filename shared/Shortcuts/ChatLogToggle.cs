using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Shortcuts;

// The game re-shows the chat log on its own, so it's re-hidden every frame.
internal sealed class ChatLogToggle : IDisposable
{
    private static readonly string[] Addons = ["ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3"];

    private readonly IFramework framework;
    // So showing the log again doesn't show one the game hid itself.
    private readonly HashSet<string> hiddenByUs = [];

    public ChatLogToggle(IFramework framework)
    {
        this.framework = framework;
        framework.Update += Update;
    }

    public bool Hidden { get; private set; }

    public void Toggle()
    {
        Hidden = !Hidden;
        if (!Hidden)
            ShowAgain();
    }

    private unsafe void Update(IFramework _)
    {
        if (!Hidden)
            return;

        var atk = RaptureAtkModule.Instance();
        if (atk != null && atk->IsTextInputActive())
        {
            ShowAgain();
            return;
        }

        foreach (var name in Addons)
        {
            var addon = AddonOf(name);
            if (addon != null && addon->IsVisible)
            {
                addon->IsVisible = false;
                hiddenByUs.Add(name);
            }
        }
    }

    private unsafe void ShowAgain()
    {
        foreach (var name in hiddenByUs)
        {
            var addon = AddonOf(name);
            if (addon != null)
                addon->IsVisible = true;
        }
        hiddenByUs.Clear();
    }

    private static unsafe AtkUnitBase* AddonOf(string name)
    {
        var manager = RaptureAtkUnitManager.Instance();
        return manager == null ? null : manager->GetAddonByName(name);
    }

    public void Dispose()
    {
        framework.Update -= Update;
        ShowAgain();
    }
}
