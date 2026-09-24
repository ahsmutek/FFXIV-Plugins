using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace RetainerCooldown;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Retainer Cooldown";

    private readonly IDalamudPluginInterface pi;
    private readonly ITextureProvider textures;
    private readonly WindowSystem windows = new("RetainerCooldown");
    private readonly HudWindow hud;
    private readonly ConfigWindow configWindow;

    internal Configuration Config { get; }

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, ITextureProvider textures)
    {
        this.pi = pi;
        this.textures = textures;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        hud = new HudWindow(this);
        configWindow = new ConfigWindow(this);
        windows.AddWindow(hud);
        windows.AddWindow(configWindow);
        pi.UiBuilder.Draw += windows.Draw;
        pi.UiBuilder.OpenConfigUi += ToggleConfig;
        // Dalamud expects a main UI callback for the installer's "open" button.
        pi.UiBuilder.OpenMainUi += ToggleConfig;
        commands.AddHandler("/retcd", new CommandInfo(OnCommand) { HelpMessage = "Open Retainer Cooldown settings." });
        CommandManager = commands;
    }

    private ICommandManager CommandManager { get; }
    internal void Save() => pi.SavePluginConfig(Config);

    // Resolve a game icon texture; false until loaded or if the ID is invalid.
    // TryGetIconPath avoids the IconNotFoundException that GetFromGameIcon throws.
    internal bool TryGetIcon(uint iconId, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        GameIconLookup lookup = iconId;
        if (!textures.TryGetIconPath(lookup, out _)) return false;
        try
        {
            return textures.GetFromGameIcon(lookup).TryGetWrap(out wrap, out _);
        }
        catch (Exception)
        {
            return false;
        }
    }
    private void ToggleConfig() => configWindow.IsOpen = !configWindow.IsOpen;
    internal void OpenConfig() => configWindow.IsOpen = true;
    private void OnCommand(string _, string __) => ToggleConfig();

    public void Dispose()
    {
        CommandManager.RemoveHandler("/retcd");
        pi.UiBuilder.Draw -= windows.Draw;
        pi.UiBuilder.OpenConfigUi -= ToggleConfig;
        pi.UiBuilder.OpenMainUi -= ToggleConfig;
        windows.RemoveAllWindows();
    }

    internal unsafe RetainerSnapshot[] ReadRetainers()
    {
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady) return [];

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = new List<RetainerSnapshot>();
        var retainers = manager->Retainers;
        for (var i = 0; i < retainers.Length; i++)
        {
            ref var r = ref retainers[i];
            if (r.RetainerId == 0 || !r.Available) continue;

            result.Add(new RetainerSnapshot(
                r.NameString,
                r.VentureId,
                r.VentureComplete,
                r.VentureId == 0 ? null : TimeSpan.FromSeconds(Math.Max(0, (long)r.VentureComplete - now)),
                r.VentureId != 0 && r.VentureComplete <= now));
        }

        return result.ToArray();
    }
}

internal readonly record struct RetainerSnapshot(
    string Name,
    ushort VentureId,
    uint CompleteAt,
    TimeSpan? Remaining,
    bool Complete);
