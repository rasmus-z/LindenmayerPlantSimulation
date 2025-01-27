using Dman.LSystem.SystemCompiler;
using Dman.LSystem.SystemRuntime.DynamicExpressions;
using Dman.LSystem.SystemRuntime.NativeCollections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;

namespace Dman.LSystem.SystemRuntime
{

    public class BasicRule
    {
        /// <summary>
        /// the symbol which this rule will replace. Apply rule will only ever be called with this symbol.
        /// </summary>
        public int TargetSymbol => _targetSymbolWithParameters.targetSymbol;

        public SymbolSeriesPrefixMatcher ContextPrefix { get; private set; }
        public SymbolSeriesSuffixMatcher ContextSuffix { get; private set; }

        public int CapturedLocalParameterCount { get; private set; }

        private short ruleGroupIndex;
        private readonly InputSymbol _targetSymbolWithParameters;
        private DynamicExpressionData conditionalChecker;
        private StructExpression conditionalCheckerBlittable;
        public bool HasConditional => conditionalChecker != null;

        public RuleOutcome[] possibleOutcomes;

        private SymbolSeriesPrefixBuilder backwardsMatchBuilder;
        private SymbolSeriesSuffixBuilder forwardsMatchBuilder;

        public BasicRule(
            ParsedRule parsedInfo,
            int branchOpenSymbol,
            int branchCloseSymbol)
        {
            _targetSymbolWithParameters = parsedInfo.coreSymbol;
            ruleGroupIndex = parsedInfo.ruleGroupIndex;

            possibleOutcomes = new RuleOutcome[] {
                new RuleOutcome(1, parsedInfo.replacementSymbols)
            };

            conditionalChecker = parsedInfo.conditionalMatch;


            backwardsMatchBuilder = new SymbolSeriesPrefixBuilder(parsedInfo.backwardsMatch);

            forwardsMatchBuilder = new SymbolSeriesSuffixBuilder(parsedInfo.forwardsMatch);
            forwardsMatchBuilder.BuildGraphIndexes(branchOpenSymbol, branchCloseSymbol);


            CapturedLocalParameterCount = _targetSymbolWithParameters.parameterLength +
                backwardsMatchBuilder.targetSymbolSeries.Sum(x => x.parameterLength) +
                forwardsMatchBuilder.targetSymbolSeries.Sum(x => x.parameterLength);
        }
        /// <summary>
        /// Create a new basic rule with multiple random outcomes.
        /// It is garenteed and required that all of the stochastic rules will capture the
        /// same parameters
        /// </summary>
        /// <param name="parsedRules"></param>
        public BasicRule(IEnumerable<ParsedStochasticRule> parsedRules, int branchOpenSymbol, int branchCloseSymbol)
        {
            possibleOutcomes = parsedRules
                .Select(x =>
                    new RuleOutcome(x.probability, x.replacementSymbols)
                ).ToArray();
            var firstOutcome = parsedRules.First();

            _targetSymbolWithParameters = firstOutcome.coreSymbol;
            ruleGroupIndex = firstOutcome.ruleGroupIndex;

            conditionalChecker = firstOutcome.conditionalMatch;

            backwardsMatchBuilder = new SymbolSeriesPrefixBuilder(firstOutcome.backwardsMatch);

            forwardsMatchBuilder = new SymbolSeriesSuffixBuilder(firstOutcome.forwardsMatch);
            forwardsMatchBuilder.BuildGraphIndexes(branchOpenSymbol, branchCloseSymbol);

            CapturedLocalParameterCount = _targetSymbolWithParameters.parameterLength +
                backwardsMatchBuilder.targetSymbolSeries.Sum(x => x.parameterLength) +
                forwardsMatchBuilder.targetSymbolSeries.Sum(x => x.parameterLength);
        }

        public RuleDataRequirements RequiredMemorySpace => new RuleDataRequirements
        {
            suffixChildren = forwardsMatchBuilder.RequiredChildrenMemSpace,
            suffixGraphNodes = forwardsMatchBuilder.RequiredGraphNodeMemSpace,
            prefixNodes = backwardsMatchBuilder.targetSymbolSeries.Length,
            ruleOutcomes = possibleOutcomes.Length,
            operatorMemory = (conditionalChecker == null ? 0 : conditionalChecker.OperatorSpaceNeeded)
        } + possibleOutcomes.Aggregate(new RuleDataRequirements(), (a, b) => a + b.MemoryReqs);


        private JaggedIndexing possibleOutcomeIndexing;

        public void WriteDataIntoMemory(
            SystemLevelRuleNativeData dataArray,
            SymbolSeriesMatcherNativeDataWriter dataWriter)
        {
            ContextSuffix = forwardsMatchBuilder.BuildIntoManagedMemory(dataArray, dataWriter);
            ContextPrefix = backwardsMatchBuilder.BuildIntoManagedMemory(dataArray, dataWriter);

            foreach (var outcome in possibleOutcomes)
            {
                outcome.WriteIntoMemory(dataArray, dataWriter);
            }

            possibleOutcomeIndexing = new JaggedIndexing
            {
                index = dataWriter.indexInRuleOutcomes,
                length = (ushort)possibleOutcomes.Length
            };
            for (int i = 0; i < possibleOutcomeIndexing.length; i++)
            {
                var possibleOutcome = possibleOutcomes[i];
                dataArray.ruleOutcomeMemorySpace[i + dataWriter.indexInRuleOutcomes] = possibleOutcome.AsBlittable();
            }
            dataWriter.indexInRuleOutcomes += possibleOutcomeIndexing.length;
            if (conditionalChecker != null)
            {
                var opSize = conditionalChecker.OperatorSpaceNeeded;
                conditionalCheckerBlittable = conditionalChecker.WriteIntoOpDataArray(
                    dataArray.dynamicOperatorMemory,
                    new JaggedIndexing
                    {
                        index = dataWriter.indexInOperatorMemory,
                        length = opSize
                    });
                dataWriter.indexInOperatorMemory += opSize;
            }
        }

        public Blittable AsBlittable()
        {
            return new Blittable
            {
                contextPrefix = ContextPrefix,
                contextSuffix = ContextSuffix,
                targetSymbolWithParameters = _targetSymbolWithParameters.AsBlittable(),
                capturedLocalParameterCount = CapturedLocalParameterCount,
                possibleOutcomeIndexing = possibleOutcomeIndexing,
                hasConditional = conditionalChecker != null,
                conditional = conditionalCheckerBlittable,
                ruleGroupIndex = ruleGroupIndex
            };
        }

        public struct Blittable
        {
            public SymbolSeriesPrefixMatcher contextPrefix;
            public SymbolSeriesSuffixMatcher contextSuffix;
            public InputSymbol.Blittable targetSymbolWithParameters;
            public int capturedLocalParameterCount;
            public JaggedIndexing possibleOutcomeIndexing;
            public bool hasConditional;
            public StructExpression conditional;

            public short ruleGroupIndex;

            public bool PreMatchCapturedParametersWithoutConditional(
                SymbolStringBranchingCache branchingCache,
                SymbolString<float> source,
                int indexInSymbols,
                NativeArray<float> parameterMemory,
                int startIndexInParameterMemory,
                ref LSystemSingleSymbolMatchData matchSingletonData,
                TmpNativeStack<SymbolStringBranchingCache.BranchEventData> helperStack,
                NativeArray<float> globalParams,
                NativeArray<OperatorDefinition> globalOperatorData,
                ref Unity.Mathematics.Random random,
                NativeArray<RuleOutcome.Blittable> outcomes)
            {
                var target = targetSymbolWithParameters;

                // parameters
                byte matchedParameterNum = 0;

                // context match
                if (contextPrefix.IsValid && contextPrefix.graphNodeMemSpace.length > 0)
                {
                    var backwardsMatchMatches = branchingCache.MatchesBackwards(
                        branchingCache.includeSymbols[ruleGroupIndex],
                        indexInSymbols,
                        contextPrefix,
                        source,
                        startIndexInParameterMemory + matchedParameterNum,
                        parameterMemory,
                        out var copiedParameters
                        );
                    if (!backwardsMatchMatches)
                    {
                        return false;
                    }
                    matchedParameterNum += copiedParameters;
                }

                var coreParametersIndexing = source.parameters[indexInSymbols];
                if (coreParametersIndexing.length != target.parameterLength)
                {
                    return false;
                }
                if (coreParametersIndexing.length > 0)
                {
                    for (int i = 0; i < coreParametersIndexing.length; i++)
                    {
                        var paramValue = source.parameters[coreParametersIndexing, i];

                        parameterMemory[startIndexInParameterMemory + matchedParameterNum] = paramValue;
                        matchedParameterNum++;
                    }
                }

                if (contextSuffix.IsCreated && contextSuffix.graphNodeMemSpace.length > 0)
                {
                    var forwardMatch = branchingCache.MatchesForward(
                        branchingCache.includeSymbols[ruleGroupIndex],
                        indexInSymbols,
                        contextSuffix,
                        source,
                        startIndexInParameterMemory + matchedParameterNum,
                        parameterMemory,
                        out var copiedParameters,
                        helperStack);
                    if (!forwardMatch)
                    {
                        return false;
                    }
                    matchedParameterNum += copiedParameters;
                }

                matchSingletonData.tmpParameterMemorySpace = new JaggedIndexing
                {
                    index = startIndexInParameterMemory,
                    length = matchedParameterNum
                };
                if (conditional.IsValid)
                {
                    var conditionalMatch = conditional.EvaluateExpression(
                        globalParams,
                        new JaggedIndexing { index = 0, length = (ushort)globalParams.Length },
                        parameterMemory,
                        matchSingletonData.tmpParameterMemorySpace,
                        globalOperatorData) > 0;
                    if (!conditionalMatch)
                    {
                        return false;
                    }
                }

                matchSingletonData.selectedReplacementPattern = SelectOutcomeIndex(ref random, outcomes, possibleOutcomeIndexing);
                var outcomeObject = outcomes[matchSingletonData.selectedReplacementPattern + possibleOutcomeIndexing.index];


                matchSingletonData.replacementSymbolIndexing = JaggedIndexing.GetWithOnlyLength(outcomeObject.replacementSymbolSize);
                matchSingletonData.replacementParameterIndexing = JaggedIndexing.GetWithOnlyLength(outcomeObject.replacementParameterCount);


                return true;
            }
            private static byte SelectOutcomeIndex(
                ref Unity.Mathematics.Random rand,
                NativeArray<RuleOutcome.Blittable> outcomes,
                JaggedIndexing allOutcomes)
            {
                if (allOutcomes.length > 1)
                {
                    var sample = rand.NextDouble();
                    double currentPartition = 0;
                    for (byte i = 0; i < allOutcomes.length; i++)
                    {
                        var possibleOutcome = outcomes[i + allOutcomes.index];
                        currentPartition += possibleOutcome.probability;
                        if (sample <= currentPartition)
                        {
                            return i;
                        }
                    }
                    throw new LSystemRuntimeException("possible outcome probabilities do not sum to 1");
                }
                return 0;
            }

            public void WriteReplacementSymbols(
                NativeArray<float> globalParameters,
                NativeArray<float> paramTempMemorySpace,
                SymbolString<float> target,
                LSystemSingleSymbolMatchData matchSingletonData,
                NativeArray<OperatorDefinition> globalOperatorData,
                NativeArray<ReplacementSymbolGenerator.Blittable> replacementSymbolSpace,
                NativeArray<RuleOutcome.Blittable> ruleOutcomeMemorySpace,
                NativeArray<StructExpression> structExpressionSpace)
            {
                var selectedReplacementPattern = matchSingletonData.selectedReplacementPattern;

                var matchedParametersIndexing = matchSingletonData.tmpParameterMemorySpace;
                var replacementSymbolsIndexing = matchSingletonData.replacementSymbolIndexing;
                var replacementParameterIndexing = matchSingletonData.replacementParameterIndexing;

                var orderedMatchedParameters = new NativeArray<float>(globalParameters.Length + matchedParametersIndexing.length, Allocator.Temp);
                for (int i = 0; i < globalParameters.Length; i++)
                {
                    orderedMatchedParameters[i] = globalParameters[i];
                }
                for (int i = 0; i < matchedParametersIndexing.length; i++)
                {
                    orderedMatchedParameters[globalParameters.Length + i] = paramTempMemorySpace[matchedParametersIndexing.index + i];
                }
                var outcome = ruleOutcomeMemorySpace[selectedReplacementPattern + possibleOutcomeIndexing.index];

                outcome.WriteReplacement(
                    orderedMatchedParameters,
                    new JaggedIndexing
                    {
                        index = 0,
                        length = (ushort)orderedMatchedParameters.Length
                    },
                    globalOperatorData,
                    target,
                    replacementSymbolSpace,
                    structExpressionSpace,
                    replacementSymbolsIndexing.index,
                    replacementParameterIndexing.index);
            }
        }
    }
}
