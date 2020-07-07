using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;

namespace TraderTools.Simulation
{
    public class CSharpLanguage : ILanguageService
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static Assembly CreateAssemblyDefinition(string code, params string[] references)
        {
            var netDirectory = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\";

            var metadataReferences = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Xml.Linq.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Data.DataSetExtensions.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "Microsoft.CSharp.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Data.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Net.Http.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Xml.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "System.Core.dll")),
                MetadataReference.CreateFromFile(Path.Combine(netDirectory, "Facades", "netstandard.dll"))
            };

            metadataReferences.AddRange(references.Select(r => MetadataReference.CreateFromFile(r)));

            //var sourceLanguage = new CSharpLanguage();
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

            Compilation compilation = CreateLibraryCompilation(assemblyName: "InMemoryAssembly", enableOptimisations: false, references: metadataReferences.ToArray())
                .AddSyntaxTrees(syntaxTree);

            var stream = new MemoryStream();

            var emitResult = compilation.Emit(stream);

            if (emitResult.Success)
            {
                stream.Seek(0, SeekOrigin.Begin);
                var bytes = stream.ToArray();
                return Assembly.Load(bytes);
            }
            else
            {
                var errors = emitResult.Diagnostics
                    .Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Location} {d.Id}: {d.GetMessage()}")
                    .ToArray();
                foreach (var msg in errors)
                {
                    Log.Error(msg);
                }
            }
            
            return null;
        }

        public static Compilation CreateLibraryCompilation(string assemblyName, bool enableOptimisations, MetadataReference[] references)
        {
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: enableOptimisations ? OptimizationLevel.Release : OptimizationLevel.Debug,
                allowUnsafe: true);
            options = options.WithPlatform(Platform.X64);

            return CSharpCompilation.Create(assemblyName, options: options, references: references);
        }
    }
}