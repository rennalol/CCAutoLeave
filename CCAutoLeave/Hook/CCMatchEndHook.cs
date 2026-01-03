using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace CCAutoLeave.Hook;

internal class CCMatchEndHook : IDisposable
{
    private readonly Plugin plugin;

    // Copied from PVPStats
    // p1 = director
    // p2 = results packet
    // p3 = results packet + offset (ref to specific variable?)
    // p4 = ???
    private delegate void CCMatchEnd101Delegate(IntPtr p1, IntPtr p2, IntPtr p3, uint p4);
    [Signature("40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 0F B6 42", DetourName = nameof(CCMatchEndDetour))]
    private readonly Hook<CCMatchEnd101Delegate> ccMatchEndHook;

    public CCMatchEndHook(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.InteropProvider.InitializeFromAttributes(this);
        ccMatchEndHook.Enable();
    }


    public void Dispose()
    {
        ccMatchEndHook.Dispose();
    }

    private void CCMatchEndDetour(IntPtr p1, IntPtr p2, IntPtr p3, uint p4)
    {
        // Keep the original flow going
        ccMatchEndHook.Original(p1, p2, p3, p4);

        if (plugin.Configuration.Enabled)
        {
            if (Plugin.ObjectTable.LocalPlayer.CurrentHp == 0)
            {
                // Run in loop mode
                AttemptToLeaveCC();
            }
            else
            {
                LeaveCC();
            }

        }
    }

    /*  Edge case handling:
     *  If player are in the KO'd state when the match end,
     *  there is a chance an error will occur with the message: 'You were unable to leave the area.'.
     *  This is a bruteforce approach - simple retry the whole process until the player left successfully.
     */
    private async void AttemptToLeaveCC()
    {
        while (Plugin.ClientState.IsPvPExcludingDen)
        {
            LeaveCC();
            await SleepTaskAsync();
        }
    }

    private unsafe void LeaveCC()
    {
        // Copied from SimpleTweaks's Leave Duty Command
        var contentsFinderAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentsFinderMenu);
        if (contentsFinderAgent == null) return;

        SendEvent(contentsFinderAgent, 0, 0);

        // Select Yes on Abandon duty? popup
        var selectYesnoAddon = Plugin.GameGui.GetAddonByName("SelectYesno");
        if (selectYesnoAddon != IntPtr.Zero)
        {
            var atkValues = CreateAtkValueArray([0]);
            try
            {
                ((AtkUnitBase*)selectYesnoAddon.Address)->FireCallback(1, atkValues, true);
            }
            finally
            {
                Marshal.FreeHGlobal(new IntPtr(atkValues));
            }
        }
        else
        {
            Plugin.Chat.PrintError("Abandon duty window not found.");
        }
    }

    private async Task<bool> SleepTaskAsync()
    {
        await Task.Delay(500);
        return true;
    }

    // Copied from SimpleTweaks Common
    // Similar to ECommons.Automation.Callback -> Fire
    private unsafe AtkValue* SendEvent(AgentInterface* agentInterface, ulong eventKind, params object[] eventParams)
    {
        var eventObject = stackalloc AtkValue[1];
        return SendEvent(agentInterface, eventObject, eventKind, eventParams);
    }

    private unsafe AtkValue* SendEvent(AgentInterface* agentInterface, AtkValue* eventObject, ulong eventKind, params object[] eventParams)
    {
        var atkValues = CreateAtkValueArray(eventParams);
        if (atkValues == null) return eventObject;
        try
        {
            agentInterface->ReceiveEvent(eventObject, atkValues, (uint)eventParams.Length, eventKind);
            return eventObject;
        }
        finally
        {
            for (var i = 0; i < eventParams.Length; i++)
            {
                if (atkValues[i].Type == ValueType.String)
                {
                    Marshal.FreeHGlobal(new IntPtr(atkValues[i].String));
                }
            }

            Marshal.FreeHGlobal(new IntPtr(atkValues));
        }
    }

    private unsafe AtkValue* CreateAtkValueArray(params object[] values)
    {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
        if (atkValues == null) return null;
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                switch (v)
                {
                    case uint uintValue:
                        atkValues[i].Type = ValueType.UInt;
                        atkValues[i].UInt = uintValue;
                        break;
                    case int intValue:
                        atkValues[i].Type = ValueType.Int;
                        atkValues[i].Int = intValue;
                        break;
                    case float floatValue:
                        atkValues[i].Type = ValueType.Float;
                        atkValues[i].Float = floatValue;
                        break;
                    case bool boolValue:
                        atkValues[i].Type = ValueType.Bool;
                        atkValues[i].Byte = (byte)(boolValue ? 1 : 0);
                        break;
                    case string stringValue:
                        {
                            atkValues[i].Type = ValueType.String;
                            var stringBytes = Encoding.UTF8.GetBytes(stringValue);
                            var stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                            Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                            Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                            atkValues[i].String = (byte*)stringAlloc;
                            break;
                        }
                    default:
                        throw new ArgumentException($"Unable to convert type {v.GetType()} to AtkValue");
                }
            }
        }
        catch
        {
            return null;
        }

        return atkValues;
    }
}

