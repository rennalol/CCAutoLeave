﻿using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using CCAutoLeave.Windows;
using CCAutoLeave.Hook;

namespace CCAutoLeave;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "CCAutoLeave";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    internal CCMatchEndHook? CCMatchEndHook { get; init; }

    private const string CommandName = "/ccal";
    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new();
    private ConfigWindow ConfigWindow { get; init; }


    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(HandleCommand)
        {
            HelpMessage = """
            toggles CCAutoLeave plugin.
            /ccal c|config - opens CCAutoLeave config.
            """
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        try
        {
            CCMatchEndHook = new(this);
        }
        catch (SignatureException e)
        {
            Log.Error(e, $"failed to initialize CCMatchEndHook.");
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        CCMatchEndHook.Dispose();
    }

    private void HandleCommand(string command, string args)
    {
        if (new string[] { "c", "config" }.Contains(args, StringComparer.OrdinalIgnoreCase))
        {
            ConfigWindow.IsOpen = true;
        }
        else
        {
            Configuration.Enabled = !Configuration.Enabled;
            Chat.Print($"CCAutoLeave is {(Configuration.Enabled ? "enabled" : "disabled")}.");
        }
    }

    public void ToggleConfigUi() => ConfigWindow.IsOpen = true;
    public void ToggleMainUi() => ConfigWindow.IsOpen = true;
}
