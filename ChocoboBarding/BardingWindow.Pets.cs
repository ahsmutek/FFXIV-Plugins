using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace ChocoboBarding;

internal sealed partial class BardingWindow
{
    private const float PetIconSize = 40;

    private bool resummon = true;

    private void DrawPets()
    {
        ImGui.TextWrapped("Change how your pet looks with the game's /petglamour command. Copy a line into a macro.");
        ImGui.Checkbox("Add a line that resummons the pet (the new look shows after resummoning)", ref resummon);

        foreach (var pet in pets)
        {
            ImGui.PushID(pet.Name);
            ImGui.Spacing();
            DrawIcon(pet.Icon, SmallIconSize);
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(pet.Name);
            ImGui.Separator();

            if (ImGui.BeginTable("looks", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Look", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Macro", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Copy", ImGuiTableColumnFlags.WidthFixed);

                foreach (var look in pet.Looks)
                {
                    var macro = resummon ? $"{look.Line}\n{pet.Resummon}" : look.Line;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    DrawIcon(look.Icon, PetIconSize);

                    var offset = (PetIconSize - ImGui.GetFrameHeight()) / 2;
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(look.Name == pet.Name ? $"{look.Name} (normal look)" : look.Name);
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (PetIconSize - ImGui.CalcTextSize(macro).Y) / 2);
                    ImGui.TextUnformatted(macro);
                    ImGui.TableNextColumn();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
                    CopyButton(look.Name, macro);
                }

                ImGui.EndTable();
            }

            ImGui.PopID();
        }

        if (resizes.Count > 0)
            DrawSizes();
    }

    private void DrawSizes()
    {
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextUnformatted("Summon size");
        ImGui.Separator();
        ImGui.TextWrapped("Make Bahamut, Phoenix and the primals small, medium or large with the game's /petsize command. Only you see the change.");

        if (!ImGui.BeginTable("sizes", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Summon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Copy", ImGuiTableColumnFlags.WidthFixed);

        foreach (var summon in resizes.Append(new PetGlamour.Resize("all", 0)))
        {
            ImGui.PushID(summon.Name);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawIcon(summon.Icon, PetIconSize);

            var offset = (PetIconSize - ImGui.GetFrameHeight()) / 2;
            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(summon.Name == "all" ? "All of them" : summon.Name);

            ImGui.TableNextColumn();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
            for (var i = 0; i < PetGlamour.Sizes.Length; i++)
            {
                if (i > 0)
                    ImGui.SameLine();
                var size = PetGlamour.Sizes[i];
                var line = summon.Line(size);
                if (ImGui.Button($"{char.ToUpper(size[0])}{size[1..]}"))
                    ImGui.SetClipboardText(line);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Copy {line}");
            }
            ImGui.PopID();
        }

        ImGui.EndTable();
    }
}
