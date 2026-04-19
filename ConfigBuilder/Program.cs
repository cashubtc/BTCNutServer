using System.Reflection;
using System.Text.Json;

var plugins = Directory.GetDirectories("../../../../Plugin");
var p = "";
foreach (var plugin in plugins)
{
    try
    {
        var assemblyConfigurationAttribute = typeof(Program).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
        var binPath = Path.Combine(plugin, "bin");
        if (!Directory.Exists(binPath)) continue;

        p += $"{Path.GetFullPath(plugin)}/bin/{buildConfigurationName}/net10.0/{Path.GetFileName(plugin)}.dll;";
        // if (x.Any(s => s.EndsWith("Altcoins-Debug")))
        // {
        //     p += $"{Path.GetFullPath(plugin)}/bin/Altcoins-Debug/net8.0/{Path.GetFileName(plugin)}.dll;";
        // }
        // else
        // {
        // }
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
    }
}

var content = JsonSerializer.Serialize(new
{
    DEBUG_PLUGINS = p
});

Console.WriteLine(content);
await File.WriteAllTextAsync("../../../../submodules/BTCPayServer/BTCPayServer/appsettings.dev.json", content);