using Dman.LSystem.SystemCompiler;
using System.Collections.Generic;
using System.Linq;

namespace Dman.LSystem.SystemRuntime
{
    internal class BasicRule : IRule<double>
    {
        /// <summary>
        /// the symbol which this rule will replace. Apply rule will only ever be called with this symbol.
        /// </summary>
        public int TargetSymbol => _targetSymbol;
        private readonly int _targetSymbol;

        private readonly InputSymbol _targetSymbolWithParameters;
        private System.Delegate conditionalChecker;

        public RuleOutcome[] possibleOutcomes;

        public BasicRule(ParsedRule parsedInfo)
        {
            _targetSymbolWithParameters = parsedInfo.coreSymbol;
            _targetSymbol = _targetSymbolWithParameters.targetSymbol;
            conditionalChecker = parsedInfo.conditionalMatch;
            possibleOutcomes = new RuleOutcome[] {
                new RuleOutcome
                {
                    probability = 1,
                    replacementSymbols = parsedInfo.replacementSymbols
                }
            };
        }
        public BasicRule(IEnumerable<ParsedStochasticRule> parsedRules)
        {
            possibleOutcomes = parsedRules
                .Select(x => new RuleOutcome
                {
                    probability = x.probability,
                    replacementSymbols = x.replacementSymbols
                }).ToArray();
            var firstOutcome = parsedRules.First();
            _targetSymbolWithParameters = firstOutcome.coreSymbol;
            _targetSymbol = _targetSymbolWithParameters.targetSymbol;

            conditionalChecker = firstOutcome.conditionalMatch;
        }

        /// <summary>
        /// retrun the symbol string to replace the given symbol with. return null if no match
        /// </summary>
        /// <param name="symbol">the symbol to be replaced</param>
        /// <param name="symbolParameters">the parameters applied to the symbol. Could be null if no parameters.</param>
        /// <returns></returns>
        public SymbolString<double> ApplyRule(
            SymbolString<double> symbols,
            int indexInSymbols,
            ref Unity.Mathematics.Random random,
            double[] globalRuntimeParameters = null)
        {
            var orderedMatchedParameters = new List<object>();
            if (globalRuntimeParameters != null)
            {
                foreach (var globalParam in globalRuntimeParameters)
                {
                    orderedMatchedParameters.Add(globalParam);
                }
            }
            //for (int targetSymbolIndex = 0; targetSymbolIndex < _targetSymbolsWithParameters.Length; targetSymbolIndex++)
            //{
            var target = _targetSymbolWithParameters;
            var parameter = symbols.parameters[indexInSymbols];
            if (parameter == null)
            {
                if (target.parameterLength > 0)
                {
                    return null;
                }
            }
            else
            {
                if (target.parameterLength != parameter.Length)
                {
                    return null;
                }
                for (int parameterIndex = 0; parameterIndex < parameter.Length; parameterIndex++)
                {
                    orderedMatchedParameters.Add(parameter[parameterIndex]);
                }
            }
            //}

            var paramArray = orderedMatchedParameters.ToArray();

            if (conditionalChecker != null)
            {
                var invokeResult = conditionalChecker.DynamicInvoke(paramArray);
                if (!(invokeResult is bool boolResult))
                {
                    // TODO: call this out a bit better. All compilation context is lost here
                    throw new System.Exception($"Conditional expression must evaluate to a boolean");
                }
                var conditionalResult = boolResult;
                if (!conditionalResult)
                {
                    return null;
                }
            }


            RuleOutcome outcome = SelectOutcome(ref random);

            return outcome.GenerateReplacement(paramArray);
        }

        private RuleOutcome SelectOutcome(ref Unity.Mathematics.Random rand)
        {
            if (possibleOutcomes.Length > 1)
            {
                var sample = rand.NextDouble();
                double currentPartition = 0;
                foreach (var possibleOutcome in possibleOutcomes)
                {
                    currentPartition += possibleOutcome.probability;
                    if (sample <= currentPartition)
                    {
                        return possibleOutcome;
                    }
                }
                throw new System.Exception("possible outcome probabilities do not sum to 1");
            }
            return possibleOutcomes[0];
        }
    }
}
