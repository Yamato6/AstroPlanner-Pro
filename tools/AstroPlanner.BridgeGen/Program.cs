using System.Text;
using AstroPlanner.BridgeGen;

// dotnet run --project tools/AstroPlanner.BridgeGen            writes the TypeScript contracts
// dotnet run --project tools/AstroPlanner.BridgeGen -- check   exits with 1 if they are stale

var root = FindRepositoryRoot(AppContext.BaseDirectory) ?? FindRepositoryRoot(Environment.CurrentDirectory);
if (root is null)
{
    Console.Error.WriteLine("Run from inside the repository (AstroPlanner.slnx not found).");
    return 2;
}

var path = Path.Combine(root, TypeScriptContracts.RelativePath);
var generated = TypeScriptContracts.Generate();
var current = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : null;

if (args is ["check"])
{
    if (current == generated)
    {
        Console.WriteLine($"{TypeScriptContracts.RelativePath} is up to date.");
        return 0;
    }

    Console.Error.WriteLine($"{TypeScriptContracts.RelativePath} is stale: run `dotnet run --project tools/AstroPlanner.BridgeGen`.");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, generated, new UTF8Encoding(false));
Console.WriteLine(current == generated ? $"{TypeScriptContracts.RelativePath} unchanged." : $"Wrote {TypeScriptContracts.RelativePath}.");
return 0;

static string? FindRepositoryRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "AstroPlanner.slnx")))
        {
            return directory.FullName;
        }
    }

    return null;
}
