using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

// Usage: PatchCheck <game data dir> <StsPvp.dll>
var gameData = args[0];
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    var path = Path.Combine(gameData, name.Name + ".dll");
    return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
};
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
var harmony = new Harmony("check.StsPvp");
var failures = 0;
foreach (var type in mod.GetTypes().Where(t => t.GetCustomAttributes<HarmonyPatch>().Any()))
{
    try
    {
        var before = harmony.GetPatchedMethods().ToHashSet();
        harmony.CreateClassProcessor(type).Patch();
        var added = harmony.GetPatchedMethods().Where(m => !before.Contains(m)).ToList();
        if (added.Count == 0) throw new InvalidOperationException("patched nothing");
        Console.WriteLine($"ok   {type.Name} -> {string.Join(", ", added.Select(m => m.DeclaringType!.Name + "." + m.Name))}");
    }
    catch (Exception e)
    {
        failures++;
        Console.WriteLine($"FAIL {type.Name}: {e}");
    }
}
// Models must be discoverable the way ModelDb finds mod models.
foreach (var name in new[] { "StsPvp.PvpOpponent", "StsPvp.PvpDuel" })
{
    var t = mod.GetType(name)!;
    Console.WriteLine($"model {t.Name} : {t.BaseType!.Name}");
}
return failures;
