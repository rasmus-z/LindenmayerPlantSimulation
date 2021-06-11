using Dman.LSystem.SystemCompiler;
using Dman.LSystem.SystemCompiler.Linker;
using Dman.LSystem.SystemRuntime.LSystemEvaluator;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Dman.LSystem.UnityObjects
{
    public class LSystemObject : ScriptableObject
    {
        public int seed;

        public ParsedFile parsedSystemFile;

        public LSystemStepper compiledSystem { get; private set; }
        public string axiom => parsedSystemFile.axiom;
        public int iterations => parsedSystemFile.iterations == -1 ? 7 : parsedSystemFile.iterations;

        /// <summary>
        /// Emits whenever the system is compiled
        /// </summary>
        public event Action OnCachedSystemUpdated;

        public ArrayParameterRepresenation<float> GetRuntimeParameters()
        {
            return ArrayParameterRepresenation<float>.GenerateFromList(parsedSystemFile.declaredInFileRuntimeParameters, p => p.name, p => p.defaultValue);
        }

        /// <summary>
        /// Compile this L-system into the <see cref="compiledSystem"/> property
        /// </summary>
        /// <param name="globalCompileTimeOverrides">overrides to the compile time directives. Will only be applied if the Key matches an already defined compile time parameter</param>
        public void CompileToCached(Dictionary<string, string> globalCompileTimeOverrides = null)
        {
            var newSystem = CompileSystem(globalCompileTimeOverrides);
            if (newSystem != null)
            {
                compiledSystem?.Dispose();
                compiledSystem = newSystem;

                OnCachedSystemUpdated?.Invoke();
            }
        }

        private void OnDisable()
        {
            compiledSystem?.Dispose();
            compiledSystem = null;
        }

        private void OnDestroy()
        {
            compiledSystem?.Dispose();
            compiledSystem = null;
        }

        /// <summary>
        /// Compile this L-system and return the result, not caching it into this object
        /// </summary>
        /// <param name="globalCompileTimeOverrides">overrides to the compile time directives. Will only be applied if the Key matches an already defined compile time parameter</param>
        public LSystemStepper CompileWithParameters(Dictionary<string, string> globalCompileTimeOverrides)
        {
            return CompileSystem(globalCompileTimeOverrides);
        }

        private LSystemStepper CompileSystem(Dictionary<string, string> globalCompileTimeOverrides)
        {
            UnityEngine.Profiling.Profiler.BeginSample("L System compilation");
            try
            {
                IEnumerable<string> rulesPostReplacement = parsedSystemFile.ruleLines;
                foreach (var replacement in parsedSystemFile.delaredInFileCompileTimeParameters)
                {
                    var replacementString = replacement.replacement;
                    if (globalCompileTimeOverrides != null && globalCompileTimeOverrides.TryGetValue(replacement.name, out var overrideValue))
                    {
                        replacementString = overrideValue;
                    }
                    rulesPostReplacement = rulesPostReplacement.Select(x => x.Replace(replacement.name, replacementString));
                }
                return LSystemBuilder.FloatSystem(
                    rulesPostReplacement,
                    parsedSystemFile.declaredInFileRuntimeParameters.Select(x => x.name).ToArray(),
                    parsedSystemFile.ignoredCharacters);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }
            return null;
        }

        /// <summary>
        /// Reload this asset from the .lsystem file assocated with it
        /// NO-op if not in editor mode
        /// </summary>
        public void TriggerReloadFromFile()
        {
#if UNITY_EDITOR
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(this);
            this.LoadFromFilePath(assetPath);
#endif
        }

        public void LoadFromFilePath(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var lSystemCode = File.ReadAllText(filePath);
                parsedSystemFile = new ParsedFile(filePath, lSystemCode, isLibrary: false);
            }
        }

        //TODO: deprecate. will have to start from a filepath every time.
        public void ParseRulesFromCode(string fullText)
        {
            parsedSystemFile = new ParsedFile("", fullText, isLibrary: false);
        }
    }
}
