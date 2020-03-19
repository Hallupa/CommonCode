using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text.RegularExpressions;
using log4net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using TraderTools.Core.Trading;
using TraderTools.Simulation;

namespace TraderTools.Strategy
{
    [Export(typeof(StrategyService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class StrategyService
    {
        private List<IStrategy> _strategies = new List<IStrategy>();
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static int _classNumber = 1;
        private Subject<object> _updatedSubject = new Subject<object>();
        public IObservable<object> UpdatedObservable => _updatedSubject.AsObservable();

        public List<IStrategy> Strategies => _strategies.ToList();

        public void RegisterStrategy(IStrategy strategy, bool notifyChanged = true)
        {
            _strategies.Add(strategy);

            if (notifyChanged)
            {
                _updatedSubject.OnNext(null);
            }
        }

        public void NotifyStrategiesChanged()
        {
            _updatedSubject.OnNext(null);
        }

        public void ClearStrategies(bool notifyChanged = true)
        {
            for (var i = _strategies.Count - 1; i >= 0; i--)
            {
                _strategies.Clear();
            }

            if (notifyChanged)
            {
                _updatedSubject.OnNext(null);
            }
        }

        public void SetStrategiesToUseRiskSizing(bool useRiskSizing)
        {
            foreach (var s in _strategies)
            {
                if (s is StrategyBase b)
                {
                    b.UseRiskSize = useRiskSizing;
                }
            }
        }

        public void RegisterStrategy(string code, bool notifyChanged = true)
        {
            _classNumber++;

            var namespaceRegex = new Regex(@"namespace [a-zA-Z\.\r\n ]*{");
            var match = namespaceRegex.Match(code);
            if (match.Success)
            {
                code = namespaceRegex.Replace(code, "");

                var removeLastBraces = new Regex("}[ \n]*$");
                code = removeLastBraces.Replace(code, "");
            }

            // Get class name
            var classNameRegex = new Regex("public class ([a-zA-Z0-9]*)");
            match = classNameRegex.Match(code);
            var className = match.Groups[1].Captures[0].Value;

            var alteredCode = code
                .Replace($"class {className}", "class Test" + _classNumber)
                .Replace($"public {className}", "public Test" + _classNumber)
                .Replace($"private {className}", "public Test" + _classNumber);

            var a = CSharpLanguage.CreateAssemblyDefinition(
                alteredCode,
                "TraderTools.AI.dll",
                "TraderTools.Basics.dll",
                "TraderTools.Core.dll",
                "TraderTools.Simulation.dll",
                "Hallupa.Library.dll",
                "TraderTools.Indicators.dll",
                "Hallupa.Library.dll", 
                "Python.Runtime.dll",
                "Keras.dll",
                "Numpy.Bare.dll");
                //"NumSharp.Core.dll",
                //"TensorFlow.NET.dll");
                //@"C:\Users\Oliver Wickenden\AppData\Local\Programs\Python\Python37\python37.dll"

            if (a == null)
            {
                Log.Error("Unable to compile strategy");
                return;
            }

            var t = a.GetType("Test" + _classNumber);
            if (t == null)
            {
                Log.Error("Unable to create class 'Test'");
                return;
            }

            var strategy = (IStrategy)Activator.CreateInstance(t);
            RegisterStrategy(strategy, notifyChanged);
        }
    }
    
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
                MetadataReference.CreateFromFile( Path.Combine(netDirectory, "Facades", "netstandard.dll")),
                MetadataReference.CreateFromFile(@"System.Reactive.dll")
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