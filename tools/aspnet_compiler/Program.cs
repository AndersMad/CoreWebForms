// MIT License.

using System.CommandLine;
using WebForms.Compiler;

var path = new Option<DirectoryInfo>("--path", "-p")
{
    Description = "Specifies the path to the root directory of the application",
    Required = true,
};
var references = new Option<FileInfo[]>("--references", "-r")
{
    Description = "Specifies the reference assemblies for the application",
};
var target = new Argument<DirectoryInfo>("targetDir")
{
    Description = "Specifies the path to the root directory of the application",
};
var isDebug = new Option<bool>("-d")
{
    Description = "Specifies if a debug build",
};
var rootCommand = new RootCommand("WebForms compilation");

rootCommand.Options.Add(path);
rootCommand.Options.Add(isDebug);
rootCommand.Options.Add(references);
rootCommand.Arguments.Add(target);

rootCommand.SetAction(async parseResult =>
{
    var pathValue = parseResult.GetValue(path);
    var targetValue = parseResult.GetValue(target);
    var referencesValue = parseResult.GetValue(references) ?? [];
    var isDebugValue = parseResult.GetValue(isDebug);

    ArgumentNullException.ThrowIfNull(pathValue);
    ArgumentNullException.ThrowIfNull(targetValue);

    if (!targetValue.Exists)
    {
        targetValue.Create();
    }

    await CompilationHost.RunAsync(pathValue, targetValue, referencesValue, isDebugValue).ConfigureAwait(false);
});

await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);

