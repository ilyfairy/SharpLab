using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ICSharpCode.Decompiler.Metadata;
using Mono.Cecil;
using SharpLab.Server.Common.Diagnostics;

namespace SharpLab.Server.Common {
    public class PreCachedAssemblyResolver : ICSharpCode.Decompiler.Metadata.IAssemblyResolver, Mono.Cecil.IAssemblyResolver {
        private static readonly Task<MetadataFile?> NullFileTask = Task.FromResult((MetadataFile?)null);

        private readonly ConcurrentDictionary<string, (MetadataFile file, Task<MetadataFile> task)> _peFileCache = new();
        private readonly ConcurrentDictionary<string, AssemblyDefinition> _cecilCache = new();

        public PreCachedAssemblyResolver(IReadOnlyCollection<ILanguageAdapter> languages) {
            foreach (var language in languages) {
                language.AssemblyReferenceDiscoveryTask.ContinueWith(assemblyPaths => AddToCaches(assemblyPaths));
            }
        }

        private void AddToCaches(IReadOnlyCollection<string> assemblyPaths) {
            PerformanceLog.Checkpoint("PreCachedAssemblyResolver.AddToCaches.Start");
            foreach (var path in assemblyPaths) {
                var file = new PEFile(path);
                _peFileCache.TryAdd(file.Name, (file, Task.FromResult<MetadataFile>(file)));

                var definition = AssemblyDefinition.ReadAssembly(path);
                _cecilCache.TryAdd(definition.Name.Name, definition);
            }
            PerformanceLog.Checkpoint("PreCachedAssemblyResolver.AddToCaches.End");
        }

        public MetadataFile? Resolve(IAssemblyReference reference) {
            return ResolveFromCacheForDecompilation(reference).file;
        }

        public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference) {
            return ResolveFromCacheForDecompilation(reference).task;
        }

        public MetadataFile ResolveModule(MetadataFile mainModule, string moduleName) {
            throw new NotSupportedException();
        }

        public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) {
            throw new NotSupportedException();
        }

        public AssemblyDefinition Resolve(Mono.Cecil.AssemblyNameReference name) {
            return ResolveFromCacheForExecution(name);
        }

        public AssemblyDefinition Resolve(Mono.Cecil.AssemblyNameReference name, ReaderParameters parameters) {
            throw new NotSupportedException();
        }

        private (MetadataFile? file, Task<MetadataFile?> task) ResolveFromCacheForDecompilation(IAssemblyReference reference) {
            // It is OK to _not_ find the assembly for decompilation, as e.g. in IL we can reference arbitrary assemblies
            if (!_peFileCache.TryGetValue(reference.Name, out var cached))
                return (null, NullFileTask);

            return (cached.file, ResultAsNullable(cached.task));
        }

        private AssemblyDefinition ResolveFromCacheForExecution(Mono.Cecil.AssemblyNameReference name) {
            if (!_cecilCache.TryGetValue(name.Name, out var assembly))
                throw new Exception($"Assembly {name.Name} was not found in cache.");
            return assembly;
        }

        private Task<MetadataFile?> ResultAsNullable(Task<MetadataFile> task) => (Task<MetadataFile?>)(object)task;

        public void Dispose() {
        }
    }
}
