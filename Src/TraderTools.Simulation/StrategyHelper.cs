using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using log4net;

namespace TraderTools.Simulation
{
    public static class StrategyHelper
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static int _classNumber = 0;

        public static string[] GetStrategyMarkets(Type strategyType)
        {
            var strategy = (StrategyBase)Activator.CreateInstance(strategyType);
            if (strategy != null && strategy.Markets == null)
            {
                return StrategyBase.Majors.Concat(StrategyBase.Minors).Concat(StrategyBase.MajorIndices).ToArray();
            }

            return strategy.Markets;
        }

        public static StrategyBase CreateStrategyInstance(Type strategyType)
        {
            var strategy = (StrategyBase)Activator.CreateInstance(strategyType);
            if (strategy != null && strategy.Markets == null)
            {
                strategy.SetMarkets(StrategyBase.GetDefaultMarkets());
            }

            return strategy;
        }

        public static Type CompileStrategy(string code)
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
                "TraderTools.Basics.dll",
                "TraderTools.Core.dll",
                "TraderTools.Simulation.dll",
                "Hallupa.Library.dll",
                "TraderTools.Indicators.dll",
                "Hallupa.Library.dll");

            if (a == null)
            {
                Log.Error("Unable to compile strategy");
                return null;
            }

            var t = a.GetType("Test" + _classNumber);
            if (t == null)
            {
                Log.Error("Unable to create class 'Test'");
                return null;
            }

            return t;
        }
    }
}