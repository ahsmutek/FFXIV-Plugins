using System.Linq;
using Dalamud.Bindings.ImGui;

namespace ChocoboBarding;

// The Jobs tab: barding picked per job, put on with /cbarding job.
internal sealed partial class BardingWindow
{
    // Only ever read from; rows copy it into jobEdit before changing anything.
    private static readonly SlotChoice[] NotSet = [SlotChoice.Keep, SlotChoice.Keep, SlotChoice.Keep];

    // One job row's picks while it is drawn, reused for every row so drawing doesn't allocate.
    private readonly SlotChoice[] jobEdit = new SlotChoice[Barding.SlotCount];

    private SlotChoice[] JobChoices(uint jobId) =>
        config.JobBarding.TryGetValue(jobId, out var saved) && saved.Length == Barding.SlotCount
            ? saved
            : NotSet;

    // A job with every slot on "Keep current" has nothing picked, so it isn't stored.
    private void SetJobChoices(uint jobId, SlotChoice[] choices)
    {
        if (choices.All(choice => choice.Action == SlotAction.Keep))
            config.JobBarding.Remove(jobId);
        else
            config.JobBarding[jobId] = choices;
        save();
    }

    private string FullSetLabel(SlotChoice[] choices)
    {
        if (choices.All(choice => choice.Action == SlotAction.Keep))
            return "Not set";
        return choices[0] == choices[1] && choices[1] == choices[2] ? Label(choices[0]) : "Mixed";
    }

    private bool DrawFullSetCombo(string id, SlotChoice[] choices)
    {
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo($"##{id}", FullSetLabel(choices)))
            return false;

        SlotChoice? picked = null;
        if (ImGui.Selectable("Not set", choices.All(choice => choice.Action == SlotAction.Keep)))
            picked = SlotChoice.Keep;
        if (ImGui.Selectable("None", choices.All(choice => choice.Action == SlotAction.Remove)))
            picked = SlotChoice.Remove;

        foreach (var (name, rowId, icons) in owned)
        {
            DrawIcon(icons[1], SmallIconSize);
            ImGui.SameLine();
            if (ImGui.Selectable($"{name}##{id}-{rowId}", choices.All(choice => choice.Action == SlotAction.Set && choice.RowId == rowId)))
                picked = SlotChoice.Set(rowId);
        }

        ImGui.EndCombo();

        if (picked is not { } all)
            return false;

        choices[0] = choices[1] = choices[2] = all;
        return true;
    }

    private void DrawJobs()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"Macro: {JobMacro}");
        ImGui.SameLine();
        CopyButton("job", JobMacro);
        ImGui.TextDisabled("Pick barding for each job, then press the macro to put on the one for your current job.");

        var auto = config.MatchJobAutomatically;
        if (ImGui.Checkbox("Match my job automatically", ref auto))
        {
            config.MatchJobAutomatically = auto;
            save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Puts on your job's barding when you summon your chocobo, and when you change job while it's out.\n" +
                             "Only pieces that differ are changed, so nothing is sent when it already matches.\n" +
                             "Nothing happens while your chocobo isn't summoned, and it waits until you're out of combat.");
        ImGui.Spacing();

        if (!ImGui.BeginTable("jobs", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Full set", ImGuiTableColumnFlags.WidthStretch);
        for (var slot = 0; slot < Barding.SlotCount; slot++)
            ImGui.TableSetupColumn(Barding.SlotNames[slot], ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        var current = player.IsLoaded ? player.ClassJob.RowId : 0;
        foreach (var job in jobs)
        {
            ImGui.PushID((int)job.RowId);
            ImGui.TableNextRow();
            if (job.RowId == current || job.ClassJobParent.RowId == current)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.Header));

            var choices = jobEdit;
            JobChoices(job.RowId).CopyTo(choices, 0);
            var changed = false;

            ImGui.TableNextColumn();
            DrawIcon(JobIconBase + job.RowId, SmallIconSize);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(job.Abbreviation.ExtractText());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Barding.JobName(job));

            ImGui.TableNextColumn();
            changed |= DrawFullSetCombo("full", choices);

            for (var slot = 0; slot < Barding.SlotCount; slot++)
            {
                ImGui.TableNextColumn();
                changed |= DrawSlotCombo($"slot{slot}", slot, ref choices[slot]);
            }

            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!config.JobBarding.ContainsKey(job.RowId));
            if (ImGui.Button("Clear"))
            {
                NotSet.CopyTo(choices, 0);
                changed = true;
            }
            ImGui.EndDisabled();

            // The config keeps its own copy; jobEdit is reused by the next row.
            if (changed)
                SetJobChoices(job.RowId, (SlotChoice[])choices.Clone());

            ImGui.PopID();
        }

        ImGui.EndTable();
    }
}
