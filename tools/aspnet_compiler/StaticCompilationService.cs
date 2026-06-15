// MIT License.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebForms.Compiler.Dynamic;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace WebForms.Compiler;

internal sealed class StaticCompilationService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<StaticCompilationService> _logger;
    private readonly IOptions<StaticCompilationOptions> _options;
    private readonly IWebFormsCompiler _compiler;

    public StaticCompilationService(
        IOptions<StaticCompilationOptions> options,
        IWebFormsCompiler compiler,
        IHostApplicationLifetime lifetime,
        ILogger<StaticCompilationService> logger)
    {
        _options = options;
        _compiler = compiler;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogTrace("Starting static compilation");

        var errorsPath = Path.Combine(_options.Value.TargetDirectory, "webforms.errors.json");
        var errorsTextPath = Path.Combine(_options.Value.TargetDirectory, "webforms.errors.txt");

        try
        {
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var compilation = new PersistedCompilation(_options.Value);
            using var result = _compiler.CompilePages(compilation, stoppingToken);

            File.WriteAllText(errorsPath, JsonSerializer.Serialize(compilation.Errors, jsonOptions));

            if (compilation.Errors.Count > 0)
            {
                var diagnosticLines = GetCompilationDiagnosticLines(compilation.Errors).ToArray();
                File.WriteAllLines(errorsTextPath, diagnosticLines);
                WriteCompilationDiagnostics(diagnosticLines);
                _logger.LogError(
                    "There were {Count} WebForms compilation error(s). See '{ErrorsTextPath}' or '{ErrorsPath}' for details.",
                    compilation.Errors.Count,
                    errorsTextPath,
                    errorsPath);
                return Task.CompletedTask;
            }

            var pagesPath = Path.Combine(_options.Value.TargetDirectory, "webforms.pages.json");
            File.WriteAllText(pagesPath, JsonSerializer.Serialize(compilation.Pages, jsonOptions));

            _logger.LogInformation("Completed compilation");
        }
        catch (Exception ex)
        {
            WriteUnexpectedFailureArtifacts(errorsPath, errorsTextPath, ex);
            _logger.LogCritical(ex, "Exception while compiling occurred");
        }
        finally
        {
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> GetCompilationDiagnosticLines(IEnumerable<ErrorDetails> errors)
    {
        var lines = new List<string>();

        foreach (var pageError in errors)
        {
            foreach (var diagnostic in pageError.Diagnostics)
            {
                lines.Add(diagnostic.ToMsBuildString(pageError.Path));
            }
        }

        return lines;
    }

    private static void WriteCompilationDiagnostics(IEnumerable<string> diagnosticLines)
    {
        foreach (var line in diagnosticLines)
        {
            Console.WriteLine(line);
        }
    }

    private static void WriteUnexpectedFailureArtifacts(string errorsPath, string errorsTextPath, Exception ex)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(errorsPath)!);

        File.WriteAllText(
            errorsPath,
            JsonSerializer.Serialize(
                new
                {
                    FatalError = new
                    {
                        Type = ex.GetType().FullName,
                        ex.Message,
                        StackTrace = ex.ToString()
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(errorsTextPath, ex.ToString());
        Console.WriteLine(ex);
    }

    private sealed record PageDetails(string Path, string Type, string Assembly);

    private sealed record ErrorDetails(string Path, List<RoslynError> Diagnostics);

    private sealed class PersistedCompilation(StaticCompilationOptions options) : ICompilationStrategy
    {
        public List<PageDetails> Pages { get; } = [];

        public List<ErrorDetails> Errors { get; } = [];

        public bool HandleExceptions => false;

        Stream ICompilationStrategy.CreatePdbStream(string route, string typeName, string assemblyName)
            => CreateStream(GetAssemblyPath(assemblyName, isPdb: true));

        Stream ICompilationStrategy.CreatePeStream(string route, string typeName, string assemblyName)
            => CreateStream(GetAssemblyPath(assemblyName), () => Pages.Add(new(route, typeName, assemblyName)));

        private static Stream CreateStream(string path, Action? onCommit = null)
            => new TransactionalFileStream(path, onCommit);

        private string GetAssemblyPath(string assemblyName, bool isPdb = false)
        {
            var ext = isPdb ? "pdb" : "dll";
            return Path.Combine(options.TargetDirectory, $"{assemblyName}.{ext}");
        }

        public bool HandleErrors(string route, ImmutableArray<Diagnostic> errors)
        {
            Errors.Add(new(route, errors.ConvertToErrors().ToList()));

            return true;
        }

        private sealed class TransactionalFileStream : Stream, ICompilationOutputStream
        {
            private readonly string _finalPath;
            private readonly string _tempPath;
            private readonly FileStream _stream;
            private readonly Action? _onCommit;
            private bool _committed;
            private bool _disposed;

            public TransactionalFileStream(string finalPath, Action? onCommit)
            {
                _finalPath = finalPath;
                _onCommit = onCommit;
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                _tempPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
                _stream = File.Open(_tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            }

            public void Commit()
            {
                ThrowIfDisposed();

                if (_committed)
                {
                    return;
                }

                _committed = true;
                _onCommit?.Invoke();
            }

            public override bool CanRead => _stream.CanRead;
            public override bool CanSeek => _stream.CanSeek;
            public override bool CanWrite => _stream.CanWrite;
            public override long Length => _stream.Length;
            public override long Position { get => _stream.Position; set => _stream.Position = value; }

            public override void Flush() => _stream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
            public override void SetLength(long value) => _stream.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (_disposed)
                {
                    return;
                }

                if (disposing)
                {
                    _stream.Dispose();

                    if (_committed)
                    {
                        File.Move(_tempPath, _finalPath, overwrite: true);
                    }
                    else
                    {
                        if (File.Exists(_tempPath))
                        {
                            File.Delete(_tempPath);
                        }
                    }
                }

                _disposed = true;
                base.Dispose(disposing);
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }
    }
}
