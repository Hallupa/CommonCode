using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TraderTools.Simulation
{
    public class CSharpLanguage
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static Assembly CreateAssemblyDefinition(string code, params string[] references)
        {

            var systemRefLocation = typeof(object).GetTypeInfo().Assembly.Location;
            var coreDir = Directory.GetParent(systemRefLocation);

            var metadataReferences = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Uri).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(coreDir.FullName + Path.DirectorySeparatorChar + "mscorlib.dll"),
                MetadataReference.CreateFromFile(coreDir.FullName + Path.DirectorySeparatorChar + "System.Runtime.dll"),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Xml.XmlAttribute).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enum).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(coreDir.FullName, "System.Collections.dll")),
                MetadataReference.CreateFromFile(Path.Combine(coreDir.FullName, "netstandard.dll"))
            };
            

            metadataReferences.AddRange(references.Select(r => MetadataReference.CreateFromFile(r)));

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

            Compilation compilation = CreateLibraryCompilation(
                    assemblyName: "InMemoryAssembly",
                    enableOptimisations: false, references:
                    metadataReferences.ToArray())
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