using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace GlamKit;

// The game compares with what the current job wears, so another job's gear set piece is swapped in after it reads that.
internal sealed unsafe class CompareAs : IDisposable
{
    private const string Addon = "ItemDetailCompare";
    private const int EquippedEntry = 2;
    private const uint HqModifier = 1_000_000;

    private const ImGuiWindowFlags BarFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    private readonly record struct Capture(uint ItemId, InventoryType Container, short Slot, InventoryItem.ItemFlags Flags,
                                           byte Job);

    private readonly IGameGui gameGui;
    private readonly IAddonLifecycle lifecycle;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly JobLevels jobLevels;
    private readonly Hook<AgentItemComp.Delegates.CompareItem>? hook;
    private readonly ComparePad pad;
    private uint? pickedJob;
    private (InventoryItem Selected, InventoryItem OtherQuality)? keptSelected;
    private JobLevels.CompareJob? shown;
    private Capture handled;
    private int turn;

    // The last comparison and its pages, one per job that can wear the item, for the arrows to flip through.
    private (ushort ParentAddonId, uint ItemId, byte Stain0, byte Stain1) compared;
    private List<JobLevels.CompareJob> pages = [];
    private int page = -1;

    public CompareAs(IGameInteropProvider interop, IGameGui gameGui, IAddonLifecycle lifecycle, IFramework framework,
                     IGamepadState gamepad, IPluginLog log, Configuration config, JobLevels jobLevels)
    {
        pad = new ComparePad(interop, gamepad, log);
        this.gameGui = gameGui;
        this.lifecycle = lifecycle;
        this.framework = framework;
        this.log = log;
        this.config = config;
        this.jobLevels = jobLevels;
        try
        {
            hook = interop.HookFromAddress<AgentItemComp.Delegates.CompareItem>(
                AgentItemComp.MemberFunctionPointers.CompareItem, OnCompareItem);
            hook.Enable();
        }
        catch (Exception e)
        {
            log.Warning(e, "Couldn't find the item comparison, so it won't compare with other jobs.");
        }
        framework.Update += OnFrameworkUpdate;
        lifecycle.RegisterListener(AddonEvent.PreSetup, Addon, Relabel);
        lifecycle.RegisterListener(AddonEvent.PreRefresh, Addon, Relabel);
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        lifecycle.UnregisterListener(AddonEvent.PreSetup, Addon, Relabel);
        lifecycle.UnregisterListener(AddonEvent.PreRefresh, Addon, Relabel);
        hook?.Dispose();
        pad.Dispose();
    }

    private void OnCompareItem(AgentItemComp* agent, ushort parentAddonId, uint itemId, byte stain0, byte stain1)
    {
        var jobId = pickedJob;
        var selected = keptSelected;
        pickedJob = null;
        keptSelected = null;
        shown = null;
        page = -1;
        hook!.Original(agent, parentAddonId, itemId, stain0, stain1);
        if (!config.CompareAsEnabled)
            return;

        try
        {
            var (id, hq) = ItemTooltip.SplitItemId(itemId);
            var jobs = jobLevels.CompareJobs(id, hq);
            var job = jobId is { } picked ? jobs.Find(j => j.Id == picked)
                      : jobLevels.CurrentJobCanWear(id) == false ? AutoPick(jobs) : null;
            log.Debug($"Compare {itemId} from addon {parentAddonId}: {job?.Abbreviation ?? "current job"}");
            Track((parentAddonId, itemId, stain0, stain1), jobs, job);
            var data = agent->ItemData;
            if (data == null)
                return;
            if (selected is { } kept)
            {
                data->Items[0].Item = kept.Selected;
                data->Items[1].Item = kept.OtherQuality;
            }
            if (job != null)
                Swap(data, job);
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't compare with another job's gear set.");
        }
        finally
        {
            if (agent->ItemData != null)
                handled = CaptureOf(agent->ItemData);
        }
    }

    // Picks up comparisons the game opened some other way, e.g. its own Item Comparison entry, and the arrows' presses.
    private void OnFrameworkUpdate(IFramework _)
    {
        var agent = AgentItemComp.Instance();
        if (!config.CompareAsEnabled || hook == null || agent == null || agent->ItemData == null)
            return;

        var data = agent->ItemData;
        if (turn != 0)
        {
            var step = turn;
            turn = 0;
            if (page >= 0 && pages.Count > 0)
                Recompare(data, compared, pages[(page + step + pages.Count) % pages.Count]);
            return;
        }

        // The data outlives the window, so only a comparison that's open or opening is picked up.
        var capture = CaptureOf(data);
        if (capture.ItemId != 0 && capture != handled && (agent->IsAgentActive() || Shown() != null))
            Adopt(data, capture);
    }

    private AtkUnitBase* Shown()
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(Addon).Address;
        return addon != null && addon->IsVisible && addon->RootNode != null ? addon : null;
    }

    // A re-read of the same item keeps the job being shown.
    private void Adopt(AgentItemComp.ComparedItemData* data, Capture capture)
    {
        handled = capture;
        var selected = data->Items[0].Item;
        var hq = selected.Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
        var item = ((ushort)data->ParentAddonId, selected.ItemId + (hq ? HqModifier : 0), selected.GetStain(0),
                    selected.GetStain(1));
        var jobs = jobLevels.CompareJobs(selected.ItemId, hq);
        var sameItem = selected.ItemId == ItemTooltip.SplitItemId(compared.ItemId).Id;
        var job = shown != null && sameItem ? jobs.Find(j => j.Id == shown.Id)
                  : jobLevels.CurrentJobCanWear(selected.ItemId) == false ? AutoPick(jobs) : null;
        if (job == null)
            Track(item, jobs, null);
        else
            Recompare(data, item, job);
    }

    private void Track((ushort ParentAddonId, uint ItemId, byte Stain0, byte Stain1) item, List<JobLevels.CompareJob> jobs,
                       JobLevels.CompareJob? job)
    {
        compared = item;
        pages = jobs;
        shown = job;
        page = job == null ? jobs.FindIndex(IsCurrentJob) : jobs.IndexOf(job);
    }

    private static Capture CaptureOf(AgentItemComp.ComparedItemData* data)
    {
        ref var selected = ref data->Items[0];
        return new Capture(selected.Item.ItemId, selected.Item.Container, selected.Item.Slot, selected.Item.Flags,
                           selected.CurrentClassJobId);
    }

    private static JobLevels.CompareJob? AutoPick(List<JobLevels.CompareJob> jobs) =>
        jobs.OrderByDescending(j => j.Upgrade).ThenByDescending(j => j.Level).FirstOrDefault();

    private static bool IsCurrentJob(JobLevels.CompareJob job)
    {
        var player = PlayerState.Instance();
        return player != null && (job.Id == player->CurrentClassJobId || job.ClassId == player->CurrentClassJobId);
    }

    // Compares by item ID, so the shown item's own copy (materia, dye, spiritbond) is put back afterwards.
    private void Recompare(AgentItemComp.ComparedItemData* data,
                           (ushort ParentAddonId, uint ItemId, byte Stain0, byte Stain1) item, JobLevels.CompareJob job)
    {
        keptSelected = (data->Items[0].Item, data->Items[1].Item);
        Compare(item, job);
        keptSelected = null;
    }

    // The current job needs no pick: the game compares with what it's wearing.
    private void Compare((ushort ParentAddonId, uint ItemId, byte Stain0, byte Stain1) item, JobLevels.CompareJob job)
    {
        var agent = AgentItemComp.Instance();
        if (agent == null)
            return;
        pickedJob = IsCurrentJob(job) ? null : job.Id;
        agent->CompareItem(item.ParentAddonId, item.ItemId, item.Stain0, item.Stain1);
        pickedJob = null;
    }

    // A piece that isn't on hand, e.g. with a retainer, leaves the slot empty rather than the current job's.
    private static void Swap(AgentItemComp.ComparedItemData* data, JobLevels.CompareJob job)
    {
        var entries = data->Items;
        for (var i = 0; i < job.Pieces.Length && EquippedEntry + i < entries.Length; i++)
        {
            var piece = job.Pieces[i];
            var held = piece.ItemId == 0 ? null : ItemLists.HeldCopy(piece.ItemId, piece.Hq);
            entries[EquippedEntry + i].Item = held == null ? default : *held;
        }
        foreach (ref var entry in entries)
        {
            entry.CurrentClassJobId = (byte)job.Id;
            entry.CurrentJobLevel = (byte)job.Level;
            entry.CurrentSyncedJobLevel = (byte)job.Level;
        }
    }

    // The slot name gets the job, e.g. "Body · WAR"; a refresh that read the current job's gear again is left alone.
    private void Relabel(AddonEvent type, AddonArgs args)
    {
        var agent = AgentItemComp.Instance();
        if (shown == null || agent == null || agent->ItemData == null ||
            agent->ItemData->Items[0].CurrentClassJobId != shown.Id)
            return;

        var (values, count) = args switch
        {
            AddonSetupArgs setup => (setup.AtkValues, setup.AtkValueCount),
            AddonRefreshArgs refresh => (refresh.AtkValues, refresh.AtkValueCount),
            _ => ((nint)0, 0u),
        };
        if (values == 0 || count * sizeof(AtkValue) < sizeof(AddonItemDetailCompare.AtkValuesArray))
            return;

        ref var slot = ref ((AddonItemDetailCompare.AtkValuesArray*)values)->SlotName;
        if (slot.Type is not (ValueType.String or ValueType.ManagedString or ValueType.ConstString) || slot.String.Value == null)
            return;
        var name = SeString.Parse(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(slot.String.Value));
        var suffix = $" · {shown.Abbreviation}";
        if (name.TextValue.EndsWith(suffix))
            return;

        var bytes = new SeStringBuilder().Append(name).AddText(suffix).Build().EncodeWithNullTerminator();
        fixed (byte* text = bytes)
            slot.SetManagedString(text);
    }

    // Arrows on the window's top edge, or its bottom when there's no room above.
    public void Draw()
    {
        if (!config.CompareAsEnabled || page < 0 || pages.Count < 2)
            return;
        var addon = Shown();
        var agent = AgentItemComp.Instance();
        if (addon == null || agent == null || agent->ItemData == null ||
            agent->ItemData->Items[0].Item.GetBaseItemId() != ItemTooltip.SplitItemId(compared.ItemId).Id)
            return;

        var size = new Vector2(addon->RootNode->Width, addon->RootNode->Height) * addon->Scale;
        var above = addon->Y >= ImGui.GetFrameHeightWithSpacing() * 2;
        var anchor = ImGui.GetMainViewport().Pos + new Vector2(addon->X + size.X / 2, above ? addon->Y : addon->Y + size.Y);
        ImGui.SetNextWindowPos(anchor, ImGuiCond.Always, new Vector2(0.5f, above ? 1 : 0));
        if (ImGui.Begin("##GlamKitCompareAs", BarFlags))
            DrawPage();
        ImGui.End();
    }

    // The label keeps one width, so the arrows stay put while flipping.
    private void DrawPage()
    {
        var step = pad.Step();
        if (ImGui.ArrowButton("##previous", ImGuiDir.Left))
            step = -1;
        ImGui.SameLine();
        var start = ImGui.GetCursorPosX();
        var job = pages[page];
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{job.Abbreviation} {job.Level}");
        if (job.Upgrade)
        {
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.CaretUp.ToIconString());
            ImGui.PopFont();
        }
        ImGui.SameLine(start + ImGui.CalcTextSize("WWW 100").X + ImGui.GetFontSize() + ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.ArrowButton("##next", ImGuiDir.Right))
            step = 1;

        if (step != 0)
            turn = step;
    }

}
