using System.Runtime.InteropServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace StsPvp;

/// <summary>Entry point called by the game's ModManager.</summary>
[ModInitializer(nameof(Init))]
public static class StsPvpMod
{
    private const int RtldNowGlobal = 0x102; // RTLD_NOW | RTLD_GLOBAL

    private static nint _libgcc;

    public static void Init()
    {
        LoadLibgccOnLinux();

        // Each patch class on its own: one that cannot be applied must not take the others down.
        var harmony = new Harmony("uilianks.StsPvp");
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(StsPvpMod).Assembly))
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
            {
                continue;
            }
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                Log.Error($"[pvp] patch {type.Name} failed: {e.GetBaseException().Message}");
            }
        }

        // Node hooks without patching Godot callbacks (_Ready is invoked from native code).
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.NodeAdded += OnNodeAdded;
        }
        Log.Info("[pvp] StsPvp loaded");
    }

    /// <summary>
    /// On Linux, Harmony's native exception helper (mm-exhelper.so) needs _Unwind_RaiseException,
    /// which the game process does not export: without libgcc_s loaded globally every patch fails
    /// with "undefined symbol: _Unwind_RaiseException". BaseLib works around it the same way.
    /// </summary>
    private static void LoadLibgccOnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || _libgcc != 0)
        {
            return;
        }
        _libgcc = dlopen("libgcc_s.so.1", RtldNowGlobal);
        if (_libgcc == 0)
        {
            Log.Error("[pvp] could not load libgcc_s.so.1: " + Marshal.PtrToStringAnsi(dlerror()));
        }
    }

    [DllImport("libdl.so.2")]
    private static extern nint dlopen(string filename, int flags);

    [DllImport("libdl.so.2")]
    private static extern nint dlerror();

    private static void OnNodeAdded(Node node)
    {
        switch (node)
        {
            // Deferred: runs after the node's own _Ready has set up its children.
            case NMainMenu menu:
                Callable.From(() => OnMainMenuReady(menu)).CallDeferred();
                break;
            case NCreature creature:
                Callable.From(() => HideOwnProxy(creature)).CallDeferred();
                break;
        }
    }

    private static void OnMainMenuReady(NMainMenu menu)
    {
        if (!GodotObject.IsInstanceValid(menu))
        {
            return;
        }
        // Back on the main menu: any previous duel is over.
        Duel.Reset();
        DuelMenu.AddButton(menu, menu.GetNode<NMainMenuTextButton>("MainMenuTextButtons/MultiplayerButton"));
    }

    /// <summary>Your own proxy is hidden on your screen: you only see (and target) the opponent's.</summary>
    private static void HideOwnProxy(NCreature creature)
    {
        if (!GodotObject.IsInstanceValid(creature))
        {
            return;
        }
        var player = Duel.PlayerFor(creature.Entity);
        if (player != null && LocalContext.IsMe(player))
        {
            creature.Visible = false;
        }
    }
}
