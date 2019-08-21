using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text.RegularExpressions;
using log4net;
using TraderTools.Core.Trading;

namespace TraderTools.Core.Services
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
            foreach(var s in _strategies)
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

            var a = Compile(code
                    .Replace($"class {className}", "class Test" + _classNumber)
                    .Replace($"public {className}", "public Test" + _classNumber)
                    .Replace($"private {className}", "public Test" + _classNumber),
                "System.dll", "System.Core.dll", "TraderTools.Core.dll", "Hallupa.Library.dll", "TraderTools.Indicators.dll", "TraderTools.Basics.dll",
                "TraderTools.Simulation.dll",
                @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\PresentationCore.dll",
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.ComponentModel.Composition.dll");

            if (a.Errors.Count > 0)
            {
                foreach (var error in a.Errors)
                {
                    Log.Error(error);
                }

                return;
            }

            // create Test instance
            var t = a.CompiledAssembly.GetType("Test" + _classNumber);

            if (t == null)
            {
                Log.Error("Unable to create class 'Test'");
                return;
            }

            var strategy = (IStrategy)Activator.CreateInstance(t);
            RegisterStrategy(strategy, notifyChanged);
        }

        private static CompilerResults Compile(string code, params string[] assemblies)
        {
            var csp = new Microsoft.CodeDom.Providers.DotNetCompilerPlatform.CSharpCodeProvider();
            var cps = new CompilerParameters();
            cps.ReferencedAssemblies.AddRange(assemblies);
            cps.GenerateInMemory = false;
            cps.GenerateExecutable = false;
            var compilerResults = csp.CompileAssemblyFromSource(cps, code);


            return compilerResults;
        }
    }
}