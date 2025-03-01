using Microsoft.CodeAnalysis.CSharp;
using MirrorSharp.Advanced;
using MirrorSharp.Advanced.EarlyAccess;

namespace SharpLab.Server.MirrorSharp.Guards {
    using Compilation = Microsoft.CodeAnalysis.Compilation;

    public class RoslynCompilationGuard : IRoslynCompilationGuard {
        private readonly IRoslynCompilationGuard<CSharpCompilation> _csharpCompilationGuard;

        public RoslynCompilationGuard(
            IRoslynCompilationGuard<CSharpCompilation> csharpCompilationGuard
        ) {
            _csharpCompilationGuard = csharpCompilationGuard;
        }

        public void ValidateCompilation(Compilation compilation, IRoslynSession session) {
            Argument.NotNull(nameof(compilation), compilation);
            switch (compilation) {
                case CSharpCompilation csharp:
                    _csharpCompilationGuard.ValidateCompilation(csharp);
                    break;
            }
        }
    }
}
