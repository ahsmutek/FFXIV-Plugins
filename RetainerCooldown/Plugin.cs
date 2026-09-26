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
    private const string CommandName = "/retcd";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly ITextureProvider textures;
    private readonly WindowSystem windows = new("RetainerCooldown");
    private readonly HudWindow hud;
    private readonly ConfigWindow configWindow;

    internal Configuration Config { get; }

    // Last list that still had a running venture. The bell clears RetainerManager
    // for a moment while it reloads, which would otherwise drop the shade.
    private RetainerSnapshot[] retainerCache = [];
    private long retainerGapSince;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, ITextureProvider textures)
    {
        this.pi = pi;
        this.commands = commands;
        this.textures = textures;
        Config = pi.GetPluginConfig() as Configuration ?? new Configuration();
        if (MigrateConfig()) Save();
        hud = new HudWindow(this);
        configWindow = new ConfigWindow(this);
        windows.AddWindow(hud);
        windows.AddWindow(configWindow);
        pi.UiBuilder.Draw += windows.Draw;
        pi.UiBuilder.OpenConfigUi += ToggleConfig;
        // Dalamud expects a main UI callback for the installer's "open" button.
        pi.UiBuilder.OpenMainUi += ToggleConfig;
        commands.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = "Open Retainer Cooldown settings." });
    }

    internal void Save() => pi.SavePluginConfig(Config);

    // Saved configs from before the icon and duration presets keep their old values.
    private bool MigrateConfig()
    {
        var changed = false;
        if (Config.IconId is 65049 or 70125) { Config.IconId = Configuration.DefaultIconId; changed = true; }
        if (Config.CompleteIconId is 0 or 65049 or 70126) { Config.CompleteIconId = Configuration.DefaultCompleteIconId; changed = true; }

        var known = false;
        foreach (var (_, minutes) in Configuration.VentureLengthPresets)
            if (minutes == Config.VentureDurationMinutes) { known = true; break; }
        if (!known) { Config.VentureDurationMinutes = 60; changed = true; }

        return changed;
    }

    internal bool IconExists(uint iconId) => textures.TryGetIconPath(new GameIconLookup(iconId), out _);

    // Resolve a game icon texture; false until loaded or if the ID is invalid.
    // TryGetIconPath avoids the IconNotFoundException that GetFromGameIcon throws.
    internal bool TryGetIcon(uint iconId, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        GameIconLookup lookup = iconId;
        if (!IconExists(iconId)) return false;
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
        commands.RemoveHandler(CommandName);
        pi.UiBuilder.Draw -= windows.Draw;
        pi.UiBuilder.OpenConfigUi -= ToggleConfig;
        pi.UiBuilder.OpenMainUi -= ToggleConfig;
        windows.RemoveAllWindows();
    }

    internal unsafe RetainerSnapshot[] ReadRetainers()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var manager = RetainerManager.Instance();
        if (manager == null || !manager->IsReady)
            return RefreshRemaining(retainerCache, now);

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

        var fresh = result.ToArray();
        if (!HasRunning(fresh) && HasRunning(retainerCache))
        {
            if (retainerGapSince == 0) retainerGapSince = now;
            // A real "all done" or empty list sticks; a bell reload does not.
            if (now - retainerGapSince < 1) return RefreshRemaining(retainerCache, now);
        }
        else
        {
            retainerGapSince = 0;
        }

        retainerCache = fresh;
        return fresh;
    }

    private static bool HasRunning(RetainerSnapshot[] rows) =>
        rows.Any(r => r.VentureId != 0 && !r.Complete);

    private static RetainerSnapshot[] RefreshRemaining(RetainerSnapshot[] rows, long now)
    {
        if (rows.Length == 0) return rows;
        var copy = new RetainerSnapshot[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            copy[i] = r with
            {
                Remaining = r.VentureId == 0 ? null : TimeSpan.FromSeconds(Math.Max(0, (long)r.CompleteAt - now)),
                Complete = r.VentureId != 0 && r.CompleteAt <= now,
            };
        }
        return copy;
    }
}

internal readonly record struct RetainerSnapshot(
    string Name,
    ushort VentureId,
    uint CompleteAt,
    TimeSpan? Remaining,
    bool Complete);
