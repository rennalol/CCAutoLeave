using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.NativeWrapper;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace CCAutoLeave.Services;

internal class LeaveCCService : IDisposable
{
	public void Dispose()
	{

	}

	/*  Edge case handling:
     *  If player are in the KO'd state when the match end,
     *  there is a chance an error will occur with the message: 'You were unable to leave the area.'.
     *  This is a bruteforce approach - simple retry the whole process until the player left successfully.
     */
	public async void AttemptToLeaveCC()
	{
		while (Plugin.Condition.Any(ConditionFlag.BoundByDuty))
		{
			LeaveCC();
			await SleepTaskAsync();
		}
	}

	private unsafe void LeaveCC()
	{
		// Copied from SimpleTweaks's Leave Duty Command
		// Open `Abandon duty?` popup
		var contentsFinderAgent = AgentModule.Instance()->GetAgentByInternalId(AgentId.ContentsFinderMenu);
		if (contentsFinderAgent == null) return;

		SendEvent(contentsFinderAgent, 0, 0);

		// Select Yes on `Abandon duty?` popup
		AtkUnitBasePtr selectYesnoAddon;
		if (GetAbandonDutyBase(out selectYesnoAddon))
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
			Plugin.Chat.PrintError("Abandon duty popup not found.");
		}
	}

	// Find the correct SelectYesno addon, as the KO'd countdown is also a SelectYesno addon
	private unsafe bool GetAbandonDutyBase(out AtkUnitBasePtr selectYesnoAddon)
	{
		for (int ii = 1; ii <= 2; ii++)
		{
			selectYesnoAddon = Plugin.GameGui.GetAddonByName("SelectYesno", ii);
			if (selectYesnoAddon != IntPtr.Zero)
			{
				string text = ((AtkUnitBase*)selectYesnoAddon.Address)->AtkValues[0].GetValueAsString();
				if (String.Equals(text, "Abandon duty?", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		selectYesnoAddon = IntPtr.Zero;
		return false;
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