using Dman.LSystem;
using Dman.LSystem.Packages.Tests.EditMode;
using Dman.LSystem.SystemCompiler;
using Dman.LSystem.SystemRuntime;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

using Unity.PerformanceTesting;

public class LSystemTests
{
    [Test]
    public void SymbolStringParsesStringAxiom()
    {
        var systemState = new DefaultLSystemState("B");

        Assert.AreEqual("B", systemState.currentSymbols.ToString());
        Assert.AreEqual(new JaggedIndexing
        {
            index = 0,
            length = 0
        }, systemState.currentSymbols.newParameters.indexing[0]);
        Assert.AreEqual(0, systemState.currentSymbols.newParameters.data.Length);
        systemState.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemAppliesBasicRules()
    {
        LSystemState<float> state = new DefaultLSystemState("B");
        var basicLSystem = LSystemBuilder.FloatSystem(
            new string[] {
            "A -> AB",
            "B -> A"
            });

        Assert.AreEqual("B", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("AB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABAAB", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAppliesContextualRulesWithUniqueOrigins()
    {
        LSystemState<float> state = new DefaultLSystemState("A");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "    A     -> AB",
            "    B     -> CDC",
            "    D > C -> A",
            "D < C > D -> B"
        });

        Assert.AreEqual("A", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("AB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABCDC", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABCDCCAC", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABCDCCACCABC", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABCDCCACCABCCABCDCC", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemAppliesFlatContextualRules()
    {
        LSystemState<float> state = new DefaultLSystemState("B");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "    A     -> AB",
            "    B     -> A",
            "    A > A ->",
            "A < A     -> B"
        });

        Assert.AreEqual("B", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("AB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABAAB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABABA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABAABAAB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABABABA", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAssumesIdentityReplacementWithContextRules()
    {
        LSystemState<float> state = new DefaultLSystemState("B");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "B -> ABA",
            "A > A ->",
            "A < A -> B"
        });

        Assert.AreEqual("B", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("AABAA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("BABAB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ABAAABAAABA", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAssumesIdentityRule()
    {
        LSystemState<float> state = new DefaultLSystemState("B");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A -> ACB",
            "B -> A"
        });

        Assert.AreEqual("B", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACB", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACBCA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACBCACACB", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAppliesStochasticRule()
    {
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A -> AC",
            "P(0.5) | C -> A",
            "P(0.5) | C -> AB"
        });

        AssertStochasticResults(basicLSystem, "C",
            new[]{
                "A",
                "AB"
                });
    }
    [Test]
    public void LSystemAppliesStochasticRuleDifferently()
    {
        LSystemState<float> state = new DefaultLSystemState("C");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A -> AC",
            "P(0.9) | C -> A",
            "P(0.1) | C -> AB"
        });

        Assert.AreEqual("C", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("AC", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACAAC", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("ACAACACA", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAppliesParameterMatches()
    {
        LSystemState<float> state = new DefaultLSystemState("A(1)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x) -> A(x + 1)",
        });

        Assert.AreEqual("A(1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(4)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(5)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAppliesComplexParameterEquations()
    {
        LSystemState<float> state = new DefaultLSystemState("A(1, 1)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x, y) -> A(x + y, x * y)",
        });

        Assert.AreEqual("A(1, 1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(2, 1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(3, 2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(5, 6)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(11, 30)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemDoesAFibbonachi()
    {
        LSystemState<float> state = new DefaultLSystemState("A(1)B(1)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "       A(x) > B(y) -> A(x + y)",
            "A(x) < B(y)        -> B(x)",
        });

        Assert.AreEqual("A(1)B(1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(2)B(1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(3)B(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(5)B(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(8)B(5)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(13)B(8)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemAppliesParameterMatchesAndTerminatesAtCondition()
    {
        LSystemState<float> state = new DefaultLSystemState("A(2)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x) : x < 6 -> A(x + 1)",
        });

        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(4)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(5)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(6)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("A(6)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }


    [Test]
    public void LSystemAppliesReplacementBasedOnGlobalParameters()
    {
        var globalParameters = new string[] { "global" };

        LSystemState<float> state = new DefaultLSystemState("A(1, 1)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x, y) -> A((x + y) - global, x * y + global)",
        }, globalParameters);

        var defaultGlobalParams = new float[] { 5 };

        Assert.AreEqual("A(1, 1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(-3, 6)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(-2, -13)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(-20, 31)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(6, -615)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(-614, -3685)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }


    [Test]
    public void LSystemChecksContidionalBasedOnGlobalParameters()
    {
        var globalParameters = new string[] { "global" };

        LSystemState<float> state = new DefaultLSystemState("A(0)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x) : x < global -> A(x + 1)",
        }, globalParameters);

        var defaultGlobalParams = new float[] { 3 };

        Assert.AreEqual("A(0)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());

        var nextGlobalParams = new float[] { 4 };
        state = basicLSystem.StepSystem(state, nextGlobalParams);
        Assert.AreEqual("A(4)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemSelectsRuleToApplyBasedOnConditional()
    {
        var globalParameters = new string[] { "global" };

        LSystemState<float> state = new DefaultLSystemState("A(0)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A(x) : x < global -> A(x + 1)",
            "A(x) : x >= global -> A(x - 1)",
        }, globalParameters);

        var defaultGlobalParams = new float[] { 3 };

        Assert.AreEqual("A(0)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(1)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(2)", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state, defaultGlobalParams);
        Assert.AreEqual("A(3)", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemSelectsStochasticRuleToApplyBasedOnConditional()
    {
        var globalParameters = new string[] { "global" };

        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "P(0.5) | A(x) : x < global -> A(x + 1)",
            "P(0.5) | A(x) : x < global -> A(x + 0.5)",
            "P(0.5) | A(x) : x >= global -> A(x - 1)",
            "P(0.5) | A(x) : x >= global -> A(x - 0.5)",
        }, globalParameters);

        var defaultGlobalParams = new float[] { 3 };


        AssertStochasticResults(basicLSystem, "A(0)",
            new[]{
                "A(0.5)",
                "A(1)"
                }, defaultGlobalParams);
        AssertStochasticResults(basicLSystem, "A(2)",
            new[]{
                "A(2.5)",
                "A(3)"
                }, defaultGlobalParams);
        AssertStochasticResults(basicLSystem, "A(3)",
            new[]{
                "A(2.5)",
                "A(2)"
                }, defaultGlobalParams);
        AssertStochasticResults(basicLSystem, "A(3.5)",
            new[]{
                "A(3)",
                "A(2.5)"
                }, defaultGlobalParams);
    }

    /// <summary>
    /// Test to ensure that the random seeder, which seeds based off of a sequential index and a 
    ///     single seed value, is well distributed
    /// </summary>
    [Test]
    public void RandomNumTest()
    {
        var indexCount = 100;
        var seedCount = 100;

        var perfectDistribution = Enumerable.Range(0, 100).Select(x => x / 100d).StdDev(out var mean);

        var indexes = Enumerable.Range(1, indexCount).Select(x => (uint)x);
        var randomGen = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));
        var seeds = Enumerable.Repeat(0, seedCount).Select((x) =>
        {
            return (uint)randomGen.Next(int.MinValue, int.MaxValue);
        });
        var randomByIndexesThenBySeed = indexes.Select(x =>
        {
            return seeds.Select(seed =>
            {
                var r = Dman.LSystem.LSystem.RandomFromIndexAndSeed(x, seed);
                return r.NextUInt();
            }).ToArray();
        }).ToArray();

        var statsAcrossIndexes = Enumerable.Range(0, indexCount)
            .Select(index => randomByIndexesThenBySeed[index]
                .GetStats()
            ).ToArray().GetMetaStats();
        var statsAcrossSeeds = Enumerable.Range(0, seedCount)
            .Select(index => randomByIndexesThenBySeed
                .Select(x => x[index])
                .ToList()
                .GetStats()
            ).ToArray().GetMetaStats();

        UnityEngine.Debug.Log($"perfect stdDev:{perfectDistribution:P1}");
        UnityEngine.Debug.Log($"stats across indexes:\n{statsAcrossIndexes}");
        UnityEngine.Debug.Log($"stats across seeds:\n{statsAcrossSeeds}");

        // ensure even distribution across seeds, and across indexes within seeds
        Assert.AreEqual(0, statsAcrossIndexes.minStats.MeanRel, 0.02);
        Assert.AreEqual(1, statsAcrossIndexes.maxStats.MeanRel, 0.02);
        Assert.AreEqual(0, statsAcrossIndexes.stdDevStats.StdDevRel, 0.02);
        Assert.AreEqual(perfectDistribution, statsAcrossIndexes.stdDevStats.MeanRel, 0.02);

        Assert.AreEqual(0, statsAcrossSeeds.minStats.MeanRel, 0.02);
        Assert.AreEqual(1, statsAcrossSeeds.maxStats.MeanRel, 0.02);
        Assert.AreEqual(0, statsAcrossSeeds.stdDevStats.StdDevRel, 0.02);
        Assert.AreEqual(perfectDistribution, statsAcrossSeeds.stdDevStats.MeanRel, 0.02);
    }

    [Test]
    public void LSystemReproducesStochasticResultFromReplicatedState()
    {
        var globalParameters = new string[] { "global" };

        LSystemState<float> state = new DefaultLSystemState("A(0)");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "P(0.5) | A(x) : x < global -> A(x + 1)",
            "P(0.5) | A(x) : x < global -> A(x + 0.5)",
            "P(0.5) | A(x) : x >= global -> A(x - 1)",
            "P(0.5) | A(x) : x >= global -> A(x - 0.5)",
        }, globalParameters);

        var defaultGlobalParams = new float[] { 3 };

        var resultSequence = new List<string>();
        var resultSampleSize = 12;
        resultSequence.Add(state.currentSymbols.ToString());

        LSystemState<float> systemCopyAt5 = null;
        for (int i = 1; i < resultSampleSize; i++)
        {
            if (i == 5)
            {
                systemCopyAt5 = state;
                state = basicLSystem.StepSystem(state, defaultGlobalParams, false);
            }
            else
            {
                state = basicLSystem.StepSystem(state, defaultGlobalParams);
            }
            resultSequence.Add(state.currentSymbols.ToString());
        }
        state.currentSymbols.Dispose();

        for (int i = 5; i < resultSampleSize; i++)
        {
            systemCopyAt5 = basicLSystem.StepSystem(systemCopyAt5, defaultGlobalParams);
            Assert.AreEqual(resultSequence[i], systemCopyAt5.currentSymbols.ToString(), $"Index {i}");
        }
        systemCopyAt5.currentSymbols.Dispose();
    }

    [Test]
    public void LSystemPrioritizesContextualRulesBySize()
    {
        LSystemState<float> state = new DefaultLSystemState("AABCD");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A -> B",
            "A > A -> C",
            "A > ABCD -> F",
            "A > ABC -> E",
            "A > AB -> D",
        });

        Assert.AreEqual("AABCD", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("FBBCD", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemAppliesContextualRulesStochasticly()
    {
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A -> AB",
            "B -> A",
            "P(0.5) | A > A ->",
            "P(0.5) | A > A -> A",
            "A < A -> B"
        });

        AssertStochasticResults(basicLSystem, "ABAAB",
            new[]{
                "ABABA",
                "ABAABA"
                });
    }
    [Test]
    public void LSystemAppliesContextualRulesOfEqualComplexityInDefinitionOrder()
    {
        LSystemState<float> state = new DefaultLSystemState("AAA");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "    A > A -> B",
            "A < A     -> C"
        });
        Assert.AreEqual("AAA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("BBC", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();

        state = new DefaultLSystemState("AAA");
        basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "A < A     -> C",
            "    A > A -> B"
        });
        Assert.AreEqual("AAA", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("BCC", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }
    [Test]
    public void LSystemIgnoresIgnoredCharatchers()
    {
        LSystemState<float> state = new DefaultLSystemState("B");
        var basicLSystem = LSystemBuilder.FloatSystem(new string[] {
            "    A     -> A1B2",
            "    B     -> 3A4",
            "    A > A -> 5",
            "A < A     -> 6B7"
        }, ignoredCharacters: "1234567");

        Assert.AreEqual("B", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("3A4", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("3A1B24", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("3A1B213A424", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("3A1B213A4213A1B2424", state.currentSymbols.ToString());
        state = basicLSystem.StepSystem(state);
        Assert.AreEqual("3A1B213A4213542136B713A42424", state.currentSymbols.ToString());
        state.currentSymbols.Dispose();
    }

    [Test]
    public void RuleCompilationFailsWhenConflictingRules()
    {
        Assert.Throws<LSystemRuntimeException>(() =>
        {
            var compiledRules = RuleParser.CompileRules(new string[] {
                "A -> AB",
                "A -> CA",
            });
        });
    }
    [Test]
    public void RuleCompilationFailsWhenContextMatchesOfDifferentTypesTryToShareProbability()
    {
        Assert.Throws<LSystemRuntimeException>(() =>
        {
            var compiledRules = RuleParser.CompileRules(new string[] {
                "P(0.5) | A > B -> AB",
                "P(0.5) | A > BC -> CA",
            });
        });
    }
    [Test]
    public void RuleCompilationFailsWhenContextMatchesOfDifferentTypesMatchSamePattern()
    {
        Assert.Throws<LSystemRuntimeException>(() =>
        {
            var compiledRules = RuleParser.CompileRules(new string[] {
                "C < A > B[C][D] -> AB",
                "C < A > B[C][D] -> CA",
            });
        });
    }
    [Test, Ignore("future feature will be to compare rules semantically instead of literally")]
    public void RuleCompilationFailsWhenSuffixContextsMatchSemanticallyButNotLiterally()
    {
        Assert.Throws<LSystemRuntimeException>(() =>
        {
            var compiledRules = RuleParser.CompileRules(new string[] {
                "A > B[C]D -> AB",
                "A > B[C][D] -> CA",
            });
        });
    }

    private void AssertStochasticResults(Dman.LSystem.LSystem system, string axiom, string[] expectedResults, float[] globalParams = null)
    {
        var resultSet = new HashSet<string>();
        var attemptSeeder = new Unity.Mathematics.Random((uint)axiom.GetHashCode());
        var attempts = expectedResults.Length * 10;
        for (int attempt = 1; attempt < attempts; attempt++)
        {
            LSystemState<float> state = new DefaultLSystemState(axiom, attemptSeeder.NextUInt());
            state = system.StepSystem(state, globalParams);
            resultSet.Add(state.currentSymbols.ToString());
            state.currentSymbols.Dispose();
        }
        Assert.AreEqual(expectedResults.Length, resultSet.Count);
        for (int i = 0; i < expectedResults.Length; i++)
        {
            var expected = expectedResults[i];
            Assert.IsTrue(resultSet.Contains(expected), $"Generated states should contain stochastic result {expected}");
        }
    }
}
