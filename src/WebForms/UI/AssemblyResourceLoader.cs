// MIT License.

#nullable enable

using System.Globalization;
using System.Reflection;
using System.Web.Util;
using Microsoft.Extensions.DependencyInjection;

namespace System.Web.UI;

internal class AssemblyResourceLoader
{
    internal static string FormatCdnUrl(Assembly assembly, string cdnPath)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(cdnPath);

        AssemblyName assemblyName = new(assembly.FullName!);

        return string.Format(
            CultureInfo.InvariantCulture,
            cdnPath,
            HttpUtility.UrlEncode(assemblyName.Name),
            HttpUtility.UrlEncode(assemblyName.Version?.ToString(4) ?? string.Empty),
            HttpUtility.UrlEncode(AssemblyUtil.GetAssemblyFileVersion(assembly)));
    }

    internal static Assembly GetAssemblyFromType(Type type) => type.Assembly;

    internal static string GetWebResourceUrl(Type type, string resourceName, bool htmlEncoded, IScriptManager scriptManager, bool enableCdn)
        => GetWebResourceUrl(GetAssemblyFromType(type), resourceName, htmlEncoded, scriptManager, enableCdn);

    internal static string GetWebResourceUrl(Assembly assembly, string resourceName, bool htmlEncoded, IScriptManager? scriptManager, bool enableCdn)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);

        Assembly effectiveAssembly = assembly;
        string effectiveResourceName = resourceName;
        bool debuggingEnabled = scriptManager?.IsDebuggingEnabled ?? false;
        bool secureConnection = scriptManager?.IsSecureConnection ?? false;

        if (ClientScriptManager._scriptResourceMapping?.GetDefinition(resourceName, effectiveAssembly) is { } definition)
        {
            if (!string.IsNullOrEmpty(definition.ResourceName))
            {
                effectiveResourceName = definition.ResourceName;
            }

            if (definition.ResourceAssembly != null)
            {
                effectiveAssembly = definition.ResourceAssembly;
            }

            if (ResolveDefinitionPath(definition, effectiveAssembly, effectiveResourceName, enableCdn, debuggingEnabled, secureConnection) is { Length: > 0 } path)
            {
                return htmlEncoded ? HttpUtility.HtmlEncode(path) : path;
            }
        }
        else if (enableCdn && GetCdnPath(resourceName, effectiveAssembly, secureConnection) is { Length: > 0 } cdnPath)
        {
            return htmlEncoded ? HttpUtility.HtmlEncode(cdnPath) : cdnPath;
        }

        string resourceUrl = HttpRuntime.WebObjectActivator
            .GetRequiredService<IScriptResourceHandler>()
            .GetScriptResourceUrl(effectiveAssembly, effectiveResourceName, CultureInfo.InvariantCulture, zip: false);

        return htmlEncoded ? HttpUtility.HtmlEncode(resourceUrl) : resourceUrl;
    }

    internal static string GetWebResourceUrl(Type type, string path)
    {
        return $"/__webforms/scripts/{path}";
    }

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

    private static string? GetCdnPath(string resourceName, Assembly assembly, bool secureConnection)
    {
        string? cdnPath = secureConnection
            ? FindWebResourceAttribute(assembly, resourceName)?.CdnPathSecureConnection
            : FindWebResourceAttribute(assembly, resourceName)?.CdnPath;

        return string.IsNullOrEmpty(cdnPath)
            ? null
            : FormatCdnUrl(assembly, cdnPath);
    }

    private static string? ResolveDefinitionPath(
        IScriptResourceDefinition definition,
        Assembly assembly,
        string resourceName,
        bool enableCdn,
        bool debuggingEnabled,
        bool secureConnection)
    {
        string? path;

        if (enableCdn)
        {
            if (debuggingEnabled)
            {
                path = secureConnection ? definition.CdnDebugPathSecureConnection : definition.CdnDebugPath;
                if (string.IsNullOrEmpty(path))
                {
                    path = definition.DebugPath;

                    if (string.IsNullOrEmpty(path))
                    {
                        if (!secureConnection || string.IsNullOrEmpty(definition.CdnDebugPath))
                        {
                            path = GetCdnPath(resourceName, assembly, secureConnection);
                        }

                        if (string.IsNullOrEmpty(path))
                        {
                            path = definition.Path;
                        }
                    }
                }
            }
            else
            {
                path = secureConnection ? definition.CdnPathSecureConnection : definition.CdnPath;
                if (string.IsNullOrEmpty(path))
                {
                    if (!secureConnection || string.IsNullOrEmpty(definition.CdnPath))
                    {
                        path = GetCdnPath(resourceName, assembly, secureConnection);
                    }

                    if (string.IsNullOrEmpty(path))
                    {
                        path = definition.Path;
                    }
                }
            }
        }
        else
        {
            path = debuggingEnabled && !string.IsNullOrEmpty(definition.DebugPath)
                ? definition.DebugPath
                : definition.Path;
        }

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return UrlPath.IsAppRelativePath(path)
            ? UrlPath.MakeVirtualPathAppAbsolute(path)
            : path;
    }
}
