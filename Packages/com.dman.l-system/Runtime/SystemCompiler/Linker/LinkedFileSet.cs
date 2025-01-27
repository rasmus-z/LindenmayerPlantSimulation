﻿using Dman.LSystem.SystemRuntime;
using Dman.LSystem.SystemRuntime.CustomRules;
using Dman.LSystem.SystemRuntime.LSystemEvaluator;
using Dman.Utilities.SerializableUnityObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;

namespace Dman.LSystem.SystemCompiler.Linker
{
    [Serializable]
    public class LinkedFileSet : ISymbolRemapper
    {
        public string originFile;

        public int[] immaturitySymbolMarkers;

        public SerializableDictionary<string, int> fileIndexesByFullIdentifier = new SerializableDictionary<string, int>();
        public BinarySerialized<List<LinkedFile>> allFiles;
        public List<SymbolDefinition> allSymbolDefinitionsLeafFirst;
        public SerializableDictionary<int, int> defaultSymbolDefinitionIndexBySymbol = new SerializableDictionary<int, int>();

        public List<DefineDirective> allGlobalCompileTimeParams;
        public List<RuntimeParameterAndDefault> allGlobalRuntimeParams;

        public LinkedFileSet(
            string originFileName,
            Dictionary<string, LinkedFile> allFilesByFullIdentifier,
            List<SymbolDefinition> allSymbolDefinitionsLeafFirst)
        {
            fileIndexesByFullIdentifier = new SerializableDictionary<string, int>();
            this.allSymbolDefinitionsLeafFirst = allSymbolDefinitionsLeafFirst;
            this.originFile = originFileName;

            var originFileData = allFilesByFullIdentifier[originFileName];
            if (originFileData.isLibrary)
            {
                throw new LinkException(LinkExceptionType.BASE_FILE_IS_LIBRARY, $"Origin file '{originFileName}' is a library. origin file must be a .lsystem file");
            }

            allFiles = new BinarySerialized<List<LinkedFile>>();
            var compileTimes = new Dictionary<string, DefineDirective>();
            var runTimes = new Dictionary<string, RuntimeParameterAndDefault>();
            var immaturitySymbols = new HashSet<int>();
            foreach (var kvp in allFilesByFullIdentifier)
            {
                allFiles.data.Add(kvp.Value);
                fileIndexesByFullIdentifier[kvp.Key] = allFiles.data.Count - 1;

                foreach (var compileTime in kvp.Value.delaredInFileCompileTimeParameters)
                {
                    if (compileTimes.ContainsKey(compileTime.name))
                    {
                        throw new LinkException(LinkExceptionType.GLOBAL_VARIABLE_COLLISION, $"Duplicated global compile time variable '{compileTime.name}' declared in {kvp.Value.fileSource}");
                    }
                    else
                    {
                        compileTimes[compileTime.name] = compileTime;
                    }
                }

                foreach (var runTime in kvp.Value.declaredInFileRuntimeParameters)
                {
                    if (runTimes.ContainsKey(runTime.name))
                    {
                        throw new LinkException(LinkExceptionType.GLOBAL_VARIABLE_COLLISION, $"Duplicated global run time variable '{runTime.name}' declared in {kvp.Value.fileSource}");
                    }
                    else
                    {
                        runTimes[runTime.name] = runTime;
                    }
                }

                foreach (var immature in kvp.Value.GetAllImmaturityMarkerSymbols())
                {
                    immaturitySymbols.Add(immature);
                }
            }
            allGlobalCompileTimeParams = compileTimes.Values.ToList();
            allGlobalRuntimeParams = runTimes.Values.ToList();
            this.immaturitySymbolMarkers = immaturitySymbols.ToArray();


            defaultSymbolDefinitionIndexBySymbol = new SerializableDictionary<int, int>();
            for (var i = 0; i < allSymbolDefinitionsLeafFirst.Count; i++)
            {
                var definition = allSymbolDefinitionsLeafFirst[i];
                if (defaultSymbolDefinitionIndexBySymbol.ContainsKey(definition.actualSymbol))
                {
                    continue;
                }
                defaultSymbolDefinitionIndexBySymbol[definition.actualSymbol] = i;
            }


            if (!fileIndexesByFullIdentifier.ContainsKey(originFileName))
            {
                throw new LinkException(LinkExceptionType.BAD_ORIGIN_FILE, $"could not find origin file '{originFileName}'");
            }
        }

        public SymbolString<float> GetAxiom(Allocator allocator = Allocator.Persistent)
        {
            if (!fileIndexesByFullIdentifier.ContainsKey(originFile))
            {
                throw new LinkException(LinkExceptionType.BAD_ORIGIN_FILE, $"could not find origin file '{originFile}'");
            }
            var originFileData = allFiles.data[fileIndexesByFullIdentifier[originFile]];

            return SymbolString<float>.FromString(originFileData.axiom, allocator, chr => originFileData.GetSymbolInFile(chr));
        }

        public SymbolDefinition GetLeafMostSymbolDefinition(int symbol)
        {
            return allSymbolDefinitionsLeafFirst[defaultSymbolDefinitionIndexBySymbol[symbol]];
        }


        public int GetIterations()
        {
            if (!fileIndexesByFullIdentifier.ContainsKey(originFile))
            {
                throw new LinkException(LinkExceptionType.BAD_ORIGIN_FILE, $"could not find origin file '{originFile}'");
            }
            var originFileData = allFiles.data[fileIndexesByFullIdentifier[originFile]];
            return originFileData.iterations;
        }

        public int GetSymbolFromRoot(char characterInFile)
        {
            return GetSymbol(originFile, characterInFile);
        }
        public int GetSymbol(string fileName, char characterInFile)
        {
            if (!fileIndexesByFullIdentifier.ContainsKey(fileName))
            {
                throw new LSystemRuntimeException("could not find file: " + fileName);
            }
            var fileData = allFiles.data[fileIndexesByFullIdentifier[fileName]];
            return fileData.GetSymbolInFile(characterInFile);
        }

        public char GetCharacterInRoot(int trueSymbol)
        {
            return GetCharacterInFile(originFile, trueSymbol);
        }
        public char GetCharacterInFile(string fileName, int symbolFromFile)
        {
            if (!fileIndexesByFullIdentifier.ContainsKey(fileName))
            {
                throw new LSystemRuntimeException("could not find file: " + fileName);
            }
            var fileData = allFiles.data[fileIndexesByFullIdentifier[fileName]];
            return fileData.GetCharacterFromSymbolInFile(symbolFromFile);
        }

        public LSystemStepper CompileSystem(Dictionary<string, string> globalCompileTimeOverrides = null)
        {
            UnityEngine.Profiling.Profiler.BeginSample("L System compilation");

            var openSymbol = GetSymbol(originFile, '[');
            var closeSymbol = GetSymbol(originFile, ']');

            var allReplacementDirectives = GetCompileTimeReplacementsWithOverrides(globalCompileTimeOverrides);

            var compiledRules = CompileAllRules(
                allReplacementDirectives,
                out var nativeRuleData,
                openSymbol, closeSymbol);

            var includedByFile = allFiles.data
                .Select(x => new HashSet<int>(x.GetAllIncludedContextualSymbols()))
                .ToArray();

            var customSymbols = new CustomRuleSymbols();
            customSymbols.branchOpenSymbol = openSymbol;
            customSymbols.branchCloseSymbol = closeSymbol;

            foreach (var file in allFiles.data)
            {
                file.SetCustomRuleSymbols(ref customSymbols);
            }
            if(customSymbols.hasSunlight && !customSymbols.hasIdentifiers)
            {
                throw new LinkException(LinkExceptionType.INVALID_CUSTOM_SYMBOL_CONFIGURATION, "Imported sunlight library but did not import the identifiers library. The sunlight library must be used with the identifier library");
            }
            if (allReplacementDirectives.TryGetValue("diffusionStepsPerStep", out var defineValue))
            {
                if (!int.TryParse(defineValue, out var stepsPerStep))
                {
                    throw new LinkException(LinkExceptionType.BAD_GLOBAL_PARAMETER, $"global parameter 'diffusionStepsPerStep' is defined, but is not an integer. this parameter must be an integer: '{defineValue}'");
                }
                customSymbols.diffusionStepsPerStep = stepsPerStep;
            }
            customSymbols.independentDiffusionUpdate = false;
            customSymbols.diffusionConstantRuntimeGlobalMultiplier = 1f;
            if (allReplacementDirectives.TryGetValue("independentDiffusionStep", out defineValue))
            {
                if (!bool.TryParse(defineValue, out var stepsIndependent))
                {
                    throw new LinkException(LinkExceptionType.BAD_GLOBAL_PARAMETER, $"global parameter 'independentDiffusionStep' is defined, but is not a boolean. this parameter must be either 'true' or 'false': '{defineValue}'");
                }
                customSymbols.independentDiffusionUpdate = stepsIndependent;
            }

            var result = new LSystemStepper(
                compiledRules,
                nativeRuleData,
                customSymbols,
                expectedGlobalParameters: allGlobalRuntimeParams.Count,
                includedContextualCharactersByRuleIndex: includedByFile
            );
            UnityEngine.Profiling.Profiler.EndSample();
            return result;
        }

        public IEnumerable<BasicRule> CompileAllRules(
            Dictionary<string, string> allReplacementDirectives,
            out SystemLevelRuleNativeData ruleNativeData,
            int openSymbol, int closeSymbol
            )
        {
            var allValidRuntimeParameters = allGlobalRuntimeParams.Select(x => x.name).ToArray();
            var parsedRules = allFiles.data
                .SelectMany((file, index) =>
                {
                    Func<char, int> remappingFunction = character => file.GetSymbolInFile(character);
                    try
                    {
                        return file.GetRulesWithReplacements(allReplacementDirectives)
                            .Select(x => RuleParser.ParseToRule(x, remappingFunction, (short)index, allValidRuntimeParameters))
                            .ToList();
                    }
                    catch (SyntaxException ex)
                    {
                        ex.fileName = file.fileSource;
                        throw ex;
                    }
                })
                .Where(x => x != null)
                .ToArray();
            var allRules = RuleParser.CompileAndCheckParsedRules(parsedRules, out ruleNativeData, openSymbol, closeSymbol);

            ruleNativeData.immaturityMarkerSymbols = new NativeHashSet<int>(immaturitySymbolMarkers.Length, Allocator.Persistent);
            foreach (var immature in immaturitySymbolMarkers)
            {
                ruleNativeData.immaturityMarkerSymbols.Add(immature);
            }
            return allRules;
        }

        private Dictionary<string, string> GetCompileTimeReplacementsWithOverrides(Dictionary<string, string> overrides)
        {
            var resultReplacements = new Dictionary<string, string>();
            foreach (var replacement in allGlobalCompileTimeParams)
            {
                var replacementString = replacement.replacement;
                if (overrides != null
                    && overrides.TryGetValue(replacement.name, out var overrideValue))
                {
                    replacementString = overrideValue;
                }
                resultReplacements[replacement.name] = replacementString;
            }
            return resultReplacements;
        }
    }
}


