// MIT License.

#nullable enable

using System.Globalization;
using Microsoft.AspNetCore.SystemWebAdapters;

namespace System.Web;

public static class HttpContextExtensions
{
    public static T? GetFeature<T>(this HttpContext context)
        => context.AsAspNetCore().Features.Get<T>();

    public static T GetRequiredFeature<T>(this HttpContext context)
        => context.AsAspNetCore().Features.Get<T>() ?? throw new InvalidOperationException($"Feature '{typeof(T).FullName}' is not available");

    public static object? GetGlobalResourceObject(this HttpContext context, string classKey, string resourceKey)
        => System.Web.UI.ResourceExpressionBuilder.GetGlobalResourceObject(classKey, resourceKey);

    public static object? GetGlobalResourceObject(this HttpContext context, string classKey, string resourceKey, CultureInfo? culture)
        => System.Web.UI.ResourceExpressionBuilder.GetGlobalResourceObject(classKey, resourceKey, null, null, culture);
}
