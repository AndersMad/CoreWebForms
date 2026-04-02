// MIT License.

using System.ComponentModel.Design;
using System.Reflection;
using System.Runtime.Loader;
using System.Web.UI;
using Microsoft.CodeAnalysis;

namespace WebForms.Compiler.Dynamic;

internal sealed class StaticControlCollection : AssemblyLoadContext, IDisposable, ITypeResolutionService, IMetadataProvider
{
    private readonly List<MetadataReference> _reference = [];
    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action? _dispose;

    public StaticControlCollection(IEnumerable<string> paths)
        : base("WebForms Compilation Context")
    {
        foreach (var path in paths)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(path);
            _assemblyPaths[assemblyName] = path;

            var metadata = AssemblyMetadata.CreateFromFile(path);
            _dispose += metadata.Dispose;

            if (HasTagPrefix(metadata) && !IsWebFormsLibrary(path))
            {
                LoadFromAssemblyPath(path);
                _reference.Add(metadata.GetReference());
            }
            else
            {
                _reference.Add(metadata.GetReference());
            }
        }
    }

    // We only want to load assemblies that have controls, so we can check for their attribute in its metadata
    private static bool HasTagPrefix(AssemblyMetadata assembly)
    {
        foreach (var module in assembly.GetModules())
        {
            var reader = module.GetMetadataReader();

            if (reader.HasAttribute<TagPrefixAttribute>())
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWebFormsLibrary(string path)
    {
        return string.Equals(Path.GetFileName(path), "CoreWebForms.dll", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<Assembly> ControlAssemblies => [typeof(Page).Assembly, .. Assemblies];

    public IEnumerable<MetadataReference> References => _reference;

    public IEnumerable<string> ReferencePaths => _assemblyPaths.Values.Distinct(StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        foreach (var assembly in Assemblies)
        {
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()))
            {
                return assembly;
            }
        }

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()))
            {
                return assembly;
            }
        }

        if (_assemblyPaths.TryGetValue(assemblyName.Name ?? string.Empty, out var path))
        {
            try
            {
                return LoadFromAssemblyPath(path);
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        return null;
    }

    public void Dispose() => _dispose?.Invoke();

    Assembly? ITypeResolutionService.GetAssembly(AssemblyName assemblyName)
    {
        return LoadFromAssemblyName(assemblyName);
    }

    Type? ITypeResolutionService.GetType(string type)
    {
        foreach (var assembly in ControlAssemblies)
        {
            if (assembly.GetType(type, throwOnError: false) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // TODO Unused
    Assembly? ITypeResolutionService.GetAssembly(AssemblyName name, bool throwOnError)
    {
        throw new NotImplementedException();
    }

    string? ITypeResolutionService.GetPathOfAssembly(AssemblyName name)
    {
        throw new NotImplementedException();
    }

    Type? ITypeResolutionService.GetType(string name, bool throwOnError)
    {
        throw new NotImplementedException();
    }

    Type? ITypeResolutionService.GetType(string name, bool throwOnError, bool ignoreCase)
    {
        throw new NotImplementedException();
    }

    void ITypeResolutionService.ReferenceAssembly(AssemblyName name)
    {
        throw new NotImplementedException();
    }
}
