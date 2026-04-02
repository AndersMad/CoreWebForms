// MIT License.

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace WebForms.Compiler.Dynamic;

internal sealed class PageAssemblyLoadContext : AssemblyLoadContext
{
    private readonly FrozenDictionary<string, Assembly> _map;
    private readonly FrozenDictionary<string, string> _referencePaths;
    private readonly ILogger<PageAssemblyLoadContext> _logger;

    private static readonly ConcurrentDictionary<string, int> _count = new();

    private static string GetName(string name)
    {
        var count = _count.AddOrUpdate(name, 1, static (key, value) => value + 1);

        return $"WebForms:{name}:{count}";
    }

    public PageAssemblyLoadContext(string route, IEnumerable<Assembly> assemblies, IEnumerable<string> referencePaths, ILogger<PageAssemblyLoadContext> logger)
        : base(GetName(route), isCollectible: true)
    {
        _map = assemblies.ToFrozenDictionary(a => a.FullName!);
        _referencePaths = referencePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .SelectMany(static path =>
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(path);
                    return new[]
                    {
                        new KeyValuePair<string, string>(name.FullName!, path),
                        new KeyValuePair<string, string>(name.Name!, path),
                    };
                }
                catch
                {
                    return [];
                }
            })
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(static group => group.Key, static group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        _logger = logger;

        logger.LogInformation("Created assembly for {Path}", Name);

        Unloading += PageAssemblyLoadContext_Unloading;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (_map.TryGetValue(assemblyName.FullName, out var existing))
        {
            return existing;
        }

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()))
            {
                return assembly;
            }
        }

        if (_referencePaths.TryGetValue(assemblyName.FullName ?? string.Empty, out var fullPath) ||
            _referencePaths.TryGetValue(assemblyName.Name ?? string.Empty, out fullPath))
        {
            return LoadFromAssemblyPath(fullPath);
        }

        return base.Load(assemblyName);
    }

    private void PageAssemblyLoadContext_Unloading(AssemblyLoadContext obj)
    {
        Unloading -= PageAssemblyLoadContext_Unloading;

        _logger.LogInformation("Unloading assembly load context for {Path}", Name);
    }
}
