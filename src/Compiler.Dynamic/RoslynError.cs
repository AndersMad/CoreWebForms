// MIT License.

using Microsoft.CodeAnalysis;

namespace WebForms.Compiler.Dynamic;

internal sealed class RoslynError
{
    public required string Id { get; init; }

    public required string Message { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    public required string Location { get; init; }

    public string? FilePath { get; init; }

    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }

    public string ToMsBuildString(string? route = null)
    {
        var severity = Severity switch
        {
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Hidden => "hidden",
            _ => "error",
        };

        var routeSuffix = string.IsNullOrWhiteSpace(route) ? string.Empty : $" Route: {route}";

        if (!string.IsNullOrWhiteSpace(FilePath) && StartLine is int startLine && StartColumn is int startColumn)
        {
            return $"{FilePath}({startLine},{startColumn}): {severity} {Id}: {Message}{routeSuffix}";
        }

        if (!string.IsNullOrWhiteSpace(Location))
        {
            return $"{Location}: {severity} {Id}: {Message}{routeSuffix}";
        }

        return $"{severity} {Id}: {Message}{routeSuffix}";
    }
}
