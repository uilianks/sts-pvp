using System.Runtime.InteropServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
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
                Callable.From(() => ArrangeDuelCreature(creature)).CallDeferred();
                break;
            case NPlayerTurnBanner banner:
                Callable.From(() => RetitleTurnBanner(banner)).CallDeferred();
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

    /// <summary>
    /// Duel turns go through the game's extra-turn list, so its banner says "Extra Turn":
    /// say whose turn it is instead.
    /// </summary>
    private static void RetitleTurnBanner(NPlayerTurnBanner banner)
    {
        if (!Duel.Active || !Duel.Started || !GodotObject.IsInstanceValid(banner))
        {
            return;
        }
        var active = CombatManager.Instance.PlayersTakingExtraTurn;
        if (active.Count == 0)
        {
            return;
        }
        var mine = active.Any(LocalContext.IsMe);
        Traverse.Create(banner).Field("_label").Method("SetTextAutoSize", mine ? "Your Turn" : "Opponent's Turn").GetValue();
    }

    /// <summary>
    /// On your screen the opponent appears once, as the enemy facing you: your own proxy and the
    /// opponent's co-op body (and pet) on your side are hidden; the opponent's proxy is mirrored.
    /// Display only; the creatures stay in the shared simulation.
    /// </summary>
    private static void ArrangeDuelCreature(NCreature creature)
    {
        if (!Duel.Active || !GodotObject.IsInstanceValid(creature) || creature.Entity == null)
        {
            return;
        }
        var entity = creature.Entity;
        var proxyFor = Duel.PlayerFor(entity);
        if (proxyFor != null)
        {
            if (LocalContext.IsMe(proxyFor))
            {
                creature.Visible = false;
            }
            else if (creature.Visuals != null)
            {
                var scale = creature.Visuals.Scale;
                creature.Visuals.Scale = new Vector2(-Math.Abs(scale.X), scale.Y);
            }
            return;
        }
        var owner = entity.Player ?? entity.PetOwner;
        if (owner != null && !LocalContext.IsMe(owner))
        {
            creature.Visible = false;
        }
    }
}
