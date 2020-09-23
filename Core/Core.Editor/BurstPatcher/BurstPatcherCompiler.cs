using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Debug = UnityEngine.Debug;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Latios.Editor
{
    internal static class BurstPatcherCompiler
    {
        public static string Compile(string scriptPath, List<string> assemblyPaths)
        {
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, optimizationLevel: OptimizationLevel.Debug);

            compilationOptions.WithMetadataImportOptions(MetadataImportOptions.All);
            typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(compilationOptions, (uint)1 << 22);

            List<MetadataReference> metadataReferences = new List<MetadataReference>();
            foreach (var asmPath in assemblyPaths)
            {
                metadataReferences.Add(MetadataReference.CreateFromFile(asmPath));
            }

            var compilationResult = CSharpCompilation.Create("LatiosBurstPatched",
                                                             new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(scriptPath)) },
                                                             metadataReferences,
                                                             compilationOptions).Emit(@"Library/PlayerScriptAssemblies/LatiosBurstPatched.dll");

            // Output compile errors.
            foreach (var d in compilationResult.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error))
            {
                Debug.LogError(string.Format("{0} ({1}): {2} {3}", d.Severity, d.Id, d.GetMessage(), d.Location.GetMappedLineSpan()));
            }

            return compilationResult.Success ? @"Library/PlayerScriptAssemblies/LatiosBurstPatched.dll" : null;
        }
    }
}

