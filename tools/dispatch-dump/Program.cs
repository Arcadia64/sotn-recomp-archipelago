using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

// Usage: dotnet run --project tools/dispatch-dump -- <folder with sotn.dll>
// The recomp resolves [PreHook("overlay", "Name")] through these tables (SymbolRegistry).
if (args.Length != 1) { Console.Error.WriteLine("usage: dispatch-dump <game folder>"); return 2; }
string dir = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string path = Path.Combine(dir, name.Name + ".dll");
    return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
};
var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dir, "sotn.dll"));

Type?[] types;
try { types = game.GetTypes(); }
catch (ReflectionTypeLoadException e) { types = e.Types; }

int overlays = 0;
foreach (var type in types)
{
    if (type == null || type.IsAbstract || type.GetInterface("IOverlay") == null) continue;
    var overlay = Activator.CreateInstance(type)!;
    string name = (string)type.GetProperty("Name")!.GetValue(overlay)!;
    var functions = (IDictionary)type.GetProperty("Functions")!.GetValue(overlay)!;
    foreach (DictionaryEntry entry in functions)
        Console.WriteLine($"{name} {((Delegate)entry.Value!).Method.Name}");
    overlays++;
}
Console.Error.WriteLine($"{overlays} overlays");
return overlays > 0 ? 0 : 1;
