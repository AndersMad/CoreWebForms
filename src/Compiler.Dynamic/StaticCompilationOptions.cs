// MIT License.

using System.ComponentModel.DataAnnotations;

namespace WebForms.Compiler.Dynamic;

public class StaticCompilationOptions
{
    [Required]
    public string InputDirectory { get; set; } = null!;

    [Required]
    public string TargetDirectory { get; set; } = null!;

    public List<StaticControlRegistration> ControlRegistrations { get; } = [];
}

public sealed class StaticControlRegistration
{
    [Required]
    public string TagPrefix { get; set; } = null!;

    [Required]
    public string NamespaceName { get; set; } = null!;

    [Required]
    public string AssemblyName { get; set; } = null!;
}
