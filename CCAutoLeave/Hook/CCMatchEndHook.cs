using System;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;

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
            plugin.LeaveCCService.AttemptToLeaveCC();
        }
    }
}
