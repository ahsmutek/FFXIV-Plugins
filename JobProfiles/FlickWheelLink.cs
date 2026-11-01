using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;

namespace JobProfiles;

internal sealed class FlickWheelLink(IDalamudPluginInterface pluginInterface)
{
    private readonly ICallGateSubscriber<(string Uid, string Name)[]> hotkeys =
        pluginInterface.GetIpcSubscriber<(string Uid, string Name)[]>("FlickWheel.Hotkeys");
    private readonly ICallGateSubscriber<string, bool, object> hold =
        pluginInterface.GetIpcSubscriber<string, bool, object>("FlickWheel.Hold");

    public bool Available => hotkeys.HasFunction;

    public (string Uid, string Name)[] Hotkeys()
    {
        try
        {
            return hotkeys.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            return [];
        }
    }

    public string NameOf(string uid) =>
        Hotkeys().FirstOrDefault(h => h.Uid == uid).Name ?? "a deleted hotkey";

    public void Hold(string uid, bool held)
    {
        try
        {
            hold.InvokeAction(uid, held);
        }
        catch (IpcNotReadyError)
        {
        }
    }
}
