// MIT License.

#nullable enable

using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.UI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace WebForms.Extensions;

internal sealed class ScriptResourceHandler : IScriptResourceHandler
{
    private static readonly Regex _webResourceRegex = new(
        @"<%\s*=\s*(?<resourceType>WebResource|ScriptResource)\(""(?<resourceName>[^""]*)""\)\s*%>",
        RegexOptions.Singleline | RegexOptions.Multiline);

    private readonly ILogger<ScriptResourceHandler> _logger;
    private readonly IDataProtector _protector;
    private readonly AssemblyLoadContext _context = AssemblyLoadContext.Default;

    internal sealed record ResolvedResource(Stream Content, string ContentType);

    public ScriptResourceHandler(IDataProtectionProvider protector, ILogger<ScriptResourceHandler> logger)
    {
        _logger = logger;
        _protector = protector.CreateProtector("ScriptResource");
    }

    public string Prefix { get; } = "/__webforms/resource";

    public ResolvedResource? Resolve(string encodedFile)
    {
        try
        {
            var decoded = WebEncoders.Base64UrlDecode(encodedFile);
            var bytes = _protector.Unprotect(decoded);

            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream);

            var assemblyName = new AssemblyName(reader.ReadString());
            var resourceName = reader.ReadString();
            var cultureName = reader.ReadString();
            var zip = reader.ReadBoolean();
            List<string> lookupAttempts = new();

            foreach (Assembly assembly in ResolveAssemblies(assemblyName))
            {
                string? manifestResourceName = ResolveManifestResourceName(assembly, resourceName);
                lookupAttempts.Add(DescribeLookupAttempt(assembly, resourceName, manifestResourceName));

                if (manifestResourceName is not null && assembly.GetManifestResourceStream(manifestResourceName) is { } resource)
                {
                    _logger.LogTrace("Found script for {Assembly}/{ResourceName}/{ManifestResourceName}/{Culture}/{Zip} at {Encoded}", assemblyName, resourceName, manifestResourceName, cultureName, zip, encodedFile);

                    if (FindWebResourceAttribute(assembly, resourceName) is { PerformSubstitution: true } webResourceAttribute)
                    {
                        return new ResolvedResource(
                            RewriteResource(resource, assembly, resourceName),
                            webResourceAttribute.ContentType);
                    }

                    return new ResolvedResource(resource, GetContentType(assembly, resourceName));
                }
            }

            _logger.LogWarning("Failed to find script for {Assembly}/{ResourceName}/{Culture}/{Zip} at {Encoded}. Lookup attempts: {LookupAttempts}", assemblyName, resourceName, cultureName, zip, encodedFile, string.Join(" | ", lookupAttempts));

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error trying to decode requested script {Encoded}", encodedFile);
            return null;
        }
    }

    public string GetScriptResourceUrl(Assembly assembly, string resourceName, CultureInfo culture, bool zip)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(assembly.FullName!);
        writer.Write(resourceName);
        writer.Write(culture.Name);
        writer.Write(zip);

        var @protected = _protector.Protect(ms.ToArray());
        var encoded = WebEncoders.Base64UrlEncode(@protected);

        _logger.LogTrace("Getting script URL for {Assembly}/{ResourceName}/{Culture}/{Zip} at {Encoded}", assembly.FullName, resourceName, culture.Name, zip, encoded);

        return Prefix + "?s=" + encoded;
    }

    public string GetWebResourceUrl(Type type, string resourceName, bool htmlEncoded, IScriptManager scriptManager, bool enableCdn)
    {
        return AssemblyResourceLoader.GetWebResourceUrl(type.Assembly, resourceName, htmlEncoded, scriptManager, enableCdn);
    }

    private IEnumerable<Assembly> ResolveAssemblies(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        HashSet<string> seen = new(StringComparer.Ordinal);

        // Reference source resolves resources against assemblies already loaded in the
        // AppDomain. On .NET, WebForms pages and controls can come from different load
        // contexts, and multiple copies with the same identity can be present.
        foreach (Assembly assembly in GetLoadedAssemblies(assemblyName))
        {
            if (!seen.Add(GetAssemblyKey(assembly)))
            {
                continue;
            }

            yield return assembly;
        }

        Assembly? loadedAssembly = null;
        try
        {
            loadedAssembly = _context.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
        }

        if (loadedAssembly is not null && seen.Add(GetAssemblyKey(loadedAssembly)))
        {
            yield return loadedAssembly;
        }
    }

    private static IEnumerable<Assembly> GetLoadedAssemblies(AssemblyName assemblyName)
    {
        string? fullName = assemblyName.FullName;
        string? simpleName = assemblyName.Name;

        foreach (AssemblyLoadContext context in AssemblyLoadContext.All)
        {
            foreach (Assembly assembly in context.Assemblies)
            {
                if (!string.IsNullOrEmpty(fullName) && string.Equals(assembly.FullName, fullName, StringComparison.Ordinal))
                {
                    yield return assembly;
                    continue;
                }

                if (!string.IsNullOrEmpty(simpleName) && string.Equals(assembly.GetName().Name, simpleName, StringComparison.Ordinal))
                {
                    yield return assembly;
                }
            }
        }
    }

    private static string GetAssemblyKey(Assembly assembly)
        => assembly.Location.Length > 0 ? assembly.Location : assembly.FullName ?? assembly.GetHashCode().ToString(CultureInfo.InvariantCulture);

    private static string? ResolveManifestResourceName(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);

        // WebResourceAttribute names and actual manifest resource names can diverge in SDK-style
        // builds. Prefer the requested name, then fall back to a case-insensitive or suffix match.
        string[] manifestResourceNames = assembly.GetManifestResourceNames();

        foreach (string manifestResourceName in manifestResourceNames)
        {
            if (string.Equals(manifestResourceName, resourceName, StringComparison.Ordinal))
            {
                return manifestResourceName;
            }
        }

        foreach (string manifestResourceName in manifestResourceNames)
        {
            if (string.Equals(manifestResourceName, resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return manifestResourceName;
            }
        }

        string suffix = "." + resourceName;
        foreach (string manifestResourceName in manifestResourceNames)
        {
            if (manifestResourceName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                manifestResourceName.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase))
            {
                return manifestResourceName;
            }
        }

        return null;
    }

    private static string DescribeLookupAttempt(Assembly assembly, string resourceName, string? manifestResourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);

        string assemblyLocation = assembly.Location.Length > 0 ? assembly.Location : "<dynamic>";
        bool hasWebResourceAttribute = FindWebResourceAttribute(assembly, resourceName) is not null;
        string candidateNames = GetCandidateManifestResourceNames(assembly, resourceName);

        return $"Assembly='{assemblyLocation}', AttributeMatch={hasWebResourceAttribute}, ManifestMatch='{manifestResourceName ?? "<none>"}', Candidates=[{candidateNames}]";
    }

    private static string GetCandidateManifestResourceNames(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);

        string[] manifestResourceNames = assembly.GetManifestResourceNames();
        string fileName = GetTrailingResourceToken(resourceName);
        StringBuilder builder = new();
        int count = 0;

        foreach (string manifestResourceName in manifestResourceNames)
        {
            if (!manifestResourceName.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase) &&
                !manifestResourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (count > 0)
            {
                builder.Append(", ");
            }

            builder.Append(manifestResourceName);
            count++;

            if (count == 5)
            {
                break;
            }
        }

        return count == 0 ? "<none>" : builder.ToString();
    }

    private static string GetTrailingResourceToken(string resourceName)
    {
        int lastSeparator = resourceName.LastIndexOf('.');
        if (lastSeparator < 0)
        {
            return resourceName;
        }

        int previousSeparator = resourceName.LastIndexOf('.', lastSeparator - 1);
        if (previousSeparator < 0)
        {
            return resourceName;
        }

        return resourceName.Substring(previousSeparator + 1);
    }

    private static string GetContentType(Assembly assembly, string resourceName)
        => FindWebResourceAttribute(assembly, resourceName)?.ContentType ?? "application/octet-stream";

    private static WebResourceAttribute? FindWebResourceAttribute(Assembly assembly, string resourceName)
    {
        foreach (object attribute in assembly.GetCustomAttributes(false))
        {
            if (attribute is WebResourceAttribute webResourceAttribute &&
                string.Equals(webResourceAttribute.WebResource, resourceName, StringComparison.Ordinal))
            {
                return webResourceAttribute;
            }
        }

        return null;
    }

    private Stream RewriteResource(Stream resource, Assembly assembly, string resourceName)
    {
        using var reader = new StreamReader(resource, detectEncodingFromByteOrderMarks: true);

        string content = reader.ReadToEnd();
        MatchCollection matches = _webResourceRegex.Matches(content);
        int startIndex = 0;
        StringBuilder rewritten = new();

        foreach (Match match in matches)
        {
            rewritten.Append(content.AsSpan(startIndex, match.Index - startIndex));

            string embeddedResourceName = match.Groups["resourceName"].Value;
            string resourceType = match.Groups["resourceType"].Value;

            if (embeddedResourceName.Length > 0)
            {
                if (string.Equals(embeddedResourceName, resourceName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Circular web resource reference detected for '{resourceName}'.");
                }

                rewritten.Append(string.Equals(resourceType, "ScriptResource", StringComparison.Ordinal)
                    ? GetScriptResourceUrl(assembly, embeddedResourceName, CultureInfo.InvariantCulture, zip: false)
                    : AssemblyResourceLoader.GetWebResourceUrl(assembly, embeddedResourceName, htmlEncoded: false, scriptManager: null, enableCdn: false));
            }

            startIndex = match.Index + match.Length;
        }

        rewritten.Append(content.AsSpan(startIndex, content.Length - startIndex));

        byte[] bytes = reader.CurrentEncoding.GetBytes(rewritten.ToString());
        return new MemoryStream(bytes);
    }
}
