using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.IO;
using MirrorSharp.Advanced;
using MirrorSharp.IL.Advanced;
using MirrorSharp.IL.Internal;
using Mobius.ILasm.Core;
using SharpLab.Server.Compilation.Internal;

namespace SharpLab.Server.Compilation;

public class Compiler : ICompiler {
    private static readonly EmitOptions RoslynEmitOptions = new(
        // TODO: try out embedded
        debugInformationFormat: DebugInformationFormat.PortablePdb
    );
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;

    public Compiler(RecyclableMemoryStreamManager memoryStreamManager) {
        _memoryStreamManager = memoryStreamManager;
    }

    public async Task<(bool assembly, bool symbols)> TryCompileToStreamAsync(
        MemoryStream assemblyStream,
        MemoryStream? symbolStream,
        IWorkSession session,
        IList<Diagnostic> diagnostics,
        CancellationToken cancellationToken
    ) {
        if (session.LanguageName != LanguageNames.CSharp) {
            return (false, false);
        }

        if (session.IsIL()) {
            var compiled = TryCompileILToStream(assemblyStream, session, diagnostics);
            return (compiled, false);
        }

        #warning TODO: Revisit after https: //github.com/dotnet/docs/issues/14784
        var compilation = (await session.Roslyn.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var emitResult = compilation.Emit(assemblyStream, pdbStream: symbolStream, options: RoslynEmitOptions);
        if (!emitResult.Success) {
            foreach (var diagnostic in emitResult.Diagnostics) {
                diagnostics.Add(diagnostic);
            }

            return (false, false);
        }

        return (true, true);
    }

    private static readonly DriverSettings ILDriverSettings = new() {
        ResourceResolver = ILNullResourceResolver.Default
    };
    private bool TryCompileILToStream(MemoryStream assemblyStream, IWorkSession session, IList<Diagnostic> diagnostics) {
        var il = (IILSessionInternal)session.IL();
        var ilText = il.GetTextBuilderForReadsOnly();

        // TODO: See if we can get offset from the library instead
        var lineColumnMap = ILLineColumnMap.BuildFor(ilText);
        var logger = new ILCompilationLogger(diagnostics, lineColumnMap);
        var driver = new Driver(logger, il.Target, ILDriverSettings);

        using var sourceStream = (RecyclableMemoryStream)_memoryStreamManager.GetStream("Compiler-IL", il.TextLength);
        foreach (var chunk in ilText.GetChunks()) {
            Encoding.UTF8.GetBytes(chunk.Span, sourceStream);
        }
        sourceStream.Position = 0;

        try {
            return driver.Assemble(new[] { sourceStream }, assemblyStream);
        }
        catch (Exception ex) when (ex.GetType().Name.StartsWith("yy")) {
            // These are also reported through the logger, so will be reported as diagnostics
            return false;
        }
    }
}