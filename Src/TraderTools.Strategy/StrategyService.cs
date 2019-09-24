using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using Keras.Models;
using log4net;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using TraderTools.AI;
using TraderTools.Basics;
using TraderTools.Core.Broker;
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

            /*var a = Compile(code
                    .Replace($"class {className}", "class Test" + _classNumber)
                    .Replace($"public {className}", "public Test" + _classNumber)
                    .Replace($"private {className}", "public Test" + _classNumber),
                "System.dll", "System.Core.dll", "TraderTools.Core.dll", "Hallupa.Library.dll", "TraderTools.Indicators.dll", "TraderTools.Basics.dll",
                "TraderTools.Simulation.dll", "TraderTools.AI.dll", "Keras.dll", //"Numpy.Bare.dll", "TensorFlow.NET.dll", "NumSharp.Core.dll",
                @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\PresentationCore.dll",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.ComponentModel.Composition.dll");*/

            /*var assemblies = new[]
            {
                "System.dll", "System.Core.dll", "TraderTools.Core.dll", "Hallupa.Library.dll",
                "TraderTools.Indicators.dll", "TraderTools.Basics.dll",
                "TraderTools.Simulation.dll", "TraderTools.AI.dll",
                "Keras.dll", //"Numpy.Bare.dll", "TensorFlow.NET.dll", "NumSharp.Core.dll",
                @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\PresentationCore.dll",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.ComponentModel.Composition.dll"
            };*/

            /* var nugetCache = Environment.GetEnvironmentVariable("UserProfile") + @"\.nuget\packages\";
             var references = new List<MetadataReference>
                 {
                     MetadataReference.CreateFromFile(nugetCache + @"System.Runtime\4.3.0\ref\netstandard1.5\System.Runtime.dll"),
                     MetadataReference.CreateFromFile(nugetCache + @"System.Runtime.Extensions\4.3.0\ref\netstandard1.5\System.Runtime.Extensions.dll"),
                 }
                 .ToList();*/

            //   var a2 = Assembly.Load("netstandard, Version=2.0.0.0");
            var a = CompileMk2(alteredCode,
                // typeof(object).Assembly,
                //typeof(Enumerable).Assembly,
                typeof(BrokerAccount).Assembly,
                typeof(Candle).Assembly,
                typeof(StopTrailIndicatorAttribute).Assembly,
                //typeof(string).Assembly,
                typeof(BaseModel).Assembly,
                typeof(DataGenerator).Assembly);


            // typeof(Dictionary<int, int>).Assembly);

            /*if (a.Errors.Count > 0)
            {
                foreach (var error in a.Errors)
                {
                    Log.Error(error);
                }

                return;
            }*/

            // create Test instance
            //var t2 = new Test2();
            var t = a.GetType("Test" + _classNumber);
            //var t = a.CompiledAssembly.GetType("Test" + _classNumber);

            if (t == null)
            {
                Log.Error("Unable to create class 'Test'");
                return;
            }

            var strategy = (IStrategy)Activator.CreateInstance(t);
            RegisterStrategy(strategy, notifyChanged);
        }

        private static Assembly CompileMk2(string code, params Assembly[] assemblies)
        {
            /*var a = CSharpRunner.Compile(CSharpRunner.Parse(code), out var errors, assemblies);

            if (errors != null)
            {
                foreach (var error in errors)
                {
                    Log.Error(error);
                }
            }

            return a;*/


            return CSharpLanguage.CreateAssemblyDefinition(code);
            /*Compilation compilation = sourceLanguage
                .CreateLibraryCompilation(assemblyName: "InMemoryAssembly", enableOptimisations: false)
                .AddReferences(_references)
                .AddSyntaxTrees(syntaxTree);

            var stream = new MemoryStream();
            var emitResult = compilation.Emit(stream);

            if (emitResult.Success)
            {
                stream.Seek(0, SeekOrigin.Begin);
                AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(stream);
            }*/
        }
    }
    
    public class CSharpLanguage : ILanguageService
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
      /*  private static readonly IReadOnlyCollection<MetadataReference> _references = new[] {
                      MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location),
                      MetadataReference.CreateFromFile(typeof(ValueTuple<>).GetTypeInfo().Assembly.Location)
                  };*/

        public static Assembly CreateAssemblyDefinition(string code)
        {
            var pnet = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\";

            var references = new[]
            {
                MetadataReference.CreateFromFile(Path.Combine(pnet, "mscorlib.dll")),
                MetadataReference.CreateFromFile("Keras.dll"),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Xml.Linq.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Data.DataSetExtensions.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "Microsoft.CSharp.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Data.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Net.Http.dll")),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Xml.dll")),
                MetadataReference.CreateFromFile("TraderTools.AI.dll"),
                MetadataReference.CreateFromFile("TraderTools.Basics.dll"),
                MetadataReference.CreateFromFile("TraderTools.Core.dll"),
                MetadataReference.CreateFromFile("TraderTools.Simulation.dll"),
                MetadataReference.CreateFromFile(Path.Combine(pnet, "System.Core.dll")),
                MetadataReference.CreateFromFile( Path.Combine(pnet, "Facades", "netstandard.dll")),
                /*MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.ComponentModel.Composition.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Drawing.dll"),*/
                MetadataReference.CreateFromFile(@"Hallupa.Library.dll"),
                MetadataReference.CreateFromFile(@"System.Reactive.dll"),
                /*MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\PresentationCore.dll"),
                MetadataReference.CreateFromFile(@"C:\OCW\SrcAutomatedTrading\Common\Src\TraderTools.Core\bin\x64\Debug\log4net.dll"),
                MetadataReference.CreateFromFile(@"C:\OCW\SrcAutomatedTrading\Common\Src\TraderTools.Core\bin\x64\Debug\Newtonsoft.Json.dll"),*/
                MetadataReference.CreateFromFile("TraderTools.Indicators.dll"),
                MetadataReference.CreateFromFile("Hallupa.Library.dll"),
                /*MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.IO.Compression.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.IO.Compression.FileSystem.dll"),
                MetadataReference.CreateFromFile(@"C:\OCW\SrcAutomatedTrading\Common\Src\TraderTools.Basics\bin\x64\Debug\System.Threading.Tasks.Extensions.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Windows.Forms.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\WindowsBase.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Web.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Configuration.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Numerics.dll"),
                MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Runtime.Serialization.dll"),*/
                //MetadataReference.CreateFromFile(Path.Combine(coreDir, "System.Private.CoreLib.dll")),


            };



            //var sourceLanguage = new CSharpLanguage();
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

            Compilation compilation = CreateLibraryCompilation(assemblyName: "InMemoryAssembly", enableOptimisations: false, references)
                .AddReferences(references)
                .AddSyntaxTrees(syntaxTree);

            var stream = new MemoryStream();

            var emitResult = compilation.Emit(stream);

            if (emitResult.Success)
            {
                stream.Seek(0, SeekOrigin.Begin);
                var bytes = stream.ToArray();
                //File.WriteAllBytes(path, bytes);
                return Assembly.Load(bytes);
                //AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(stream);
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
            options = options.WithPlatform(Platform.X64);                           //Set platform// OCW Added

            return CSharpCompilation.Create(assemblyName, options: options, references: references);
        }
    }

    /* public static class CSharpRunner
     {
         /*public static object Run(string snippet, IEnumerable<Assembly> references, string typeName, string methodName, params object[] args) =>
            Invoke(Compile(Parse(snippet), references), typeName, methodName, args);*/

    /* public static object Run(MethodInfo methodInfo, params object[] args)
     {
         var refs = methodInfo.DeclaringType.Assembly.GetReferencedAssemblies().Select(n => Assembly.Load(n));
         return Invoke(Compile(Decompile(methodInfo), refs), methodInfo.DeclaringType.FullName, methodInfo.Name, args);
     }/


    //Assembly.Load("netstandard");
    //private static readonly MetadataReference NetStandard = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);


    public static Assembly Compile(SyntaxTree syntaxTree, out string[] errors, IEnumerable<Assembly> references = null)
    {
        if (references is null) references = new[] { typeof(object).Assembly, typeof(Enumerable).Assembly };
        var mrefs = references?.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToList();

        //mrefs.Add(NetStandard);
        var nugetCache = Environment.GetEnvironmentVariable("UserProfile") + @"\.nuget\packages\";
        /*mrefs.AddRange(new[]
        {
            MetadataReference.CreateFromFile(nugetCache + @"System.Runtime\4.3.0\ref\netstandard1.5\System.Runtime.dll"),
            MetadataReference.CreateFromFile(nugetCache + @"System.Runtime.Extensions\4.3.0\ref\netstandard1.5\System.Runtime.Extensions.dll"),
        });/

        //var p = @"C:\Program Files\dotnet\sdk\NuGetFallbackFolder\microsoft.netcore.app\2.1.0\ref\netcoreapp2.1\";
         var p = @"C:\Program Files\dotnet\sdk\NuGetFallbackFolder\microsoft.netcore.app\2.1.0\ref\netcoreapp2.1";
         mrefs.AddRange(new[]
         {
             MetadataReference.CreateFromFile(Path.Combine(p, "netstandard.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.Runtime.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.Collections.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.Core.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "Microsoft.CSharp.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.Runtime.Extensions.dll")),
             MetadataReference.CreateFromFile(Path.Combine(p, "System.Linq.dll"))
         });
        /*
         var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
         mrefs.AddRange(new[]
         {
             MetadataReference.CreateFromFile(Path.Combine(p, "mscorlib.dll")),
         });/

        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        mrefs.AddRange(new[]
        {
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\mscorlib.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\Microsoft.CSharp.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Core.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Data.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Net.Http.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Xml.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Xml.Linq.dll"),
            MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Xml.Linq.dll"),
            //MetadataReference.CreateFromFile(@"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.Runtime.dll")
        });

        var assemblyName = "MainStuff";
        var path = @"C:\OCW\SrcAutomatedTrading\Src\AutomatedTraderDesigner\bin\x64\Debug\" + assemblyName + ".dll";
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        //options = options.WithAllowUnsafe(true);                                //Allow unsafe code;   // OCW Added
        //options = options.WithOptimizationLevel(OptimizationLevel.Release);     //Set optimization level// OCW Added
        //options = options.WithPlatform(Platform.X64);                           //Set platform// OCW Added
        var compilation = CSharpCompilation.Create(assemblyName, new[] { syntaxTree }, mrefs, options);
        errors = null;

        using (var ms = new MemoryStream())
        {
            var result = compilation.Emit(ms);
            if (result.Success)
            {
                ms.Seek(0, SeekOrigin.Begin);
                var bytes = ms.ToArray();
                File.WriteAllBytes(path, bytes);
                return Assembly.Load(bytes);
            }
            else
            {
                errors = result.Diagnostics
                    .Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"{d.Location} {d.Id}: {d.GetMessage()}")
                    .ToArray();
                return null;
            }
        }
    }

    /*  private static SyntaxTree Decompile(MethodInfo methodInfo)
      {
          var decompiler = new CSharpDecompiler(methodInfo.DeclaringType.Assembly.Location, new DecompilerSettings());
          var typeInfo = decompiler.TypeSystem.MainModule.Compilation.FindType(methodInfo.DeclaringType).GetDefinition();
          return Parse(decompiler.DecompileTypeAsString(typeInfo.FullTypeName));
      }/

    private static object Invoke(Assembly assembly, string typeName, string methodName, object[] args)
    {
        var type = assembly.GetType(typeName);
        var obj = Activator.CreateInstance(type);
        return type.InvokeMember(methodName, BindingFlags.Default | BindingFlags.InvokeMethod, null, obj, args);
    }

    public static SyntaxTree Parse(string snippet) => CSharpSyntaxTree.ParseText(snippet);
}*/
}