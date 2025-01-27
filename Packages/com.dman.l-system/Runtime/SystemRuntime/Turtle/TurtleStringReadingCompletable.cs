﻿using Cysharp.Threading.Tasks;
using Dman.LSystem.SystemRuntime.CustomRules;
using Dman.LSystem.SystemRuntime.NativeCollections;
using Dman.LSystem.SystemRuntime.ThreadBouncer;
using Dman.LSystem.SystemRuntime.VolumetricData;
using Dman.LSystem.SystemRuntime.VolumetricData.Layers;
using Dman.LSystem.SystemRuntime.VolumetricData.NativeVoxels;
using Dman.Utilities;
using Dman.Utilities.Math;
using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Dman.LSystem.SystemRuntime.Turtle
{
    public struct TurtleOrganInstance
    {
        /// <summary>
        /// the unique ID of the organ template used to spawn this organ
        /// </summary>
        public ushort organIndexInAllOrgans;
        public float4x4 organTransform;
        /// <summary>
        /// floating point identity. a uint value of 0, or a color of RGBA(0,0,0,0) indicates there is no organ identity
        /// </summary>
        public UIntFloatColor32 organIdentity;

        /// <summary>
        /// generic extra vertex data
        /// </summary>
        public byte4 extraData;
    }

    /// <summary>
    /// Used to track how big a submesh will have to be, while also tracking the current index in each array
    ///     to be used while writing to the mesh data array
    /// </summary>
    public struct TurtleMeshAllocationCounter
    {
        public int indexInVertexes;
        public int totalVertexes;

        public int indexInTriangles;
        public int totalTriangleIndexes;
    }

    public class TurtleVolumeWorldReferences
    {
        public OrganVolumetricWorld world;
        public DoubleBufferModifierHandle durabilityWriter;
        public CommandBufferModifierHandle universalLayerWriter;
        public VoxelCapReachedTimestampEffect damageFlags;
    }

    /// <summary>
    /// Executes all actions relating to the turtle, and compiles a list of organs to be turned into a mesh
    /// </summary>
    public class TurtleStringReadingCompletable
    {
        public class TurtleMeshBuildingInstructions : IDisposable
        {
            public NativeList<TurtleOrganInstance> organInstances;
            public NativeList<TurtleStemInstance> stemInstances;

            public void Dispose()
            {
                organInstances.Dispose();
                stemInstances.Dispose();
            }
        }

        public static async UniTask<TurtleMeshBuildingInstructions> ReadString(
            DependencyTracker<SymbolString<float>> symbols,
            DependencyTracker<NativeTurtleData> nativeData,
            TurtleState defaultState,
            CustomRuleSymbols customSymbols,
            TurtleVolumeWorldReferences volumetrics,
            Matrix4x4 localToWorldTransform,
            CancellationToken token,
            Matrix4x4? postReadTransform = null)
        {
            UnityEngine.Profiling.Profiler.BeginSample("turtling job");

            var currentJobHandle = default(JobHandle);

            TurtleVolumetricHandles volumetricHandles;
            VoxelWorldVolumetricLayerData tempDataToDispose = default;
            if (volumetrics != null)
            {
                JobHandleWrapper volumetricJobHandle = currentJobHandle;
                volumetricHandles = new TurtleVolumetricHandles
                {
                    durabilityWriter = volumetrics.durabilityWriter.GetNextNativeWritableHandle(localToWorldTransform, ref volumetricJobHandle),
                    universalWriter = volumetrics.universalLayerWriter.GetNextNativeWritableHandle(localToWorldTransform),
                    volumetricData = volumetrics.world.NativeVolumeData.openReadData.AsReadOnly(),
                    IsCreated = true
                };
                currentJobHandle = volumetricJobHandle;
            }
            else
            {
                tempDataToDispose = new VoxelWorldVolumetricLayerData(new VolumetricWorldVoxelLayout
                {
                    volume = new VoxelVolume
                    {
                        voxelOrigin = Vector3.zero,
                        worldResolution = Vector3Int.zero,
                        worldSize = Vector3.one,
                    },
                    dataLayerCount = 0,
                }, Allocator.TempJob);
                volumetricHandles = new TurtleVolumetricHandles
                {
                    durabilityWriter = DoubleBufferNativeWritableHandle.GetTemp(Allocator.TempJob),
                    universalWriter = CommandBufferNativeWritableHandle.GetTemp(Allocator.TempJob),
                    volumetricData = tempDataToDispose.AsReadOnly(),
                    IsCreated = false
                };
            }


            UnityEngine.Profiling.Profiler.BeginSample("allocating");
            var tmpHelperStack = new TmpNativeStack<TurtleState>(50, Allocator.TempJob);

            var organInstancesBuilder = new NativeList<TurtleOrganInstance>(100, Allocator.TempJob);
            var stemInstancesBuilder = new NativeList<TurtleStemInstance>(100, Allocator.TempJob);

            UnityEngine.Profiling.Profiler.EndSample();

            NativeArray<float> destructionCommandTimestamps;
            var hasDamageFlags = volumetrics?.damageFlags != null;
            if (hasDamageFlags)
            {
                destructionCommandTimestamps = volumetrics.damageFlags.GetDestructionCommandTimestampsReadOnly();
            }
            else
            {
                destructionCommandTimestamps = new NativeArray<float>(0, Allocator.TempJob);
            }

            EntityCommandBufferSystem entitySpawningSystem;
            EntityCommandBuffer entitySpawnBuffer;

            if (nativeData.Data.HasEntitySpawning)
            {
                entitySpawningSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
                entitySpawnBuffer = entitySpawningSystem.CreateCommandBuffer();
            }
            else
            {
                entitySpawningSystem = null;
                entitySpawnBuffer = default;
            }


            var turtleCompileJob = new TurtleCompilationJob
            {
                symbols = symbols.Data,
                operationsByKey = nativeData.Data.operationsByKey,
                organData = nativeData.Data.allOrganData,

                organInstances = organInstancesBuilder,
                stemInstances = stemInstancesBuilder,
                spawnEntityBuffer = entitySpawnBuffer,

                nativeTurtleStack = tmpHelperStack,

                currentState = defaultState,

                customRules = customSymbols,

                volumetricHandles = volumetricHandles,
                hasVolumetricDestruction = hasDamageFlags,
                volumetricDestructionTimestamps = destructionCommandTimestamps,
                earliestValidDestructionCommand = hasDamageFlags ? Time.time - volumetrics.damageFlags.timeCommandStaysActive : -1
            };


            currentJobHandle = turtleCompileJob.Schedule(currentJobHandle);
            if (entitySpawningSystem != null)
            {
                entitySpawningSystem.AddJobHandleForProducer(currentJobHandle);
            }

            volumetrics?.world.NativeVolumeData.RegisterReadingDependency(currentJobHandle);
            volumetrics?.damageFlags?.RegisterReaderOfDestructionFlags(currentJobHandle);
            volumetrics?.durabilityWriter.RegisterWriteDependency(currentJobHandle);
            volumetrics?.universalLayerWriter.RegisterWriteDependency(currentJobHandle);

            if (!volumetricHandles.IsCreated)
            {
#pragma warning disable CS4014 // no await on awaitable handle warning. not relevant here since these should execute in their time, and be forgotten
                volumetricHandles.durabilityWriter.targetData.Dispose(currentJobHandle);
                volumetricHandles.universalWriter.modificationCommandBuffer.Dispose(currentJobHandle);
                tempDataToDispose.Dispose(currentJobHandle);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }


            nativeData.RegisterDependencyOnData(currentJobHandle);
            symbols.RegisterDependencyOnData(currentJobHandle);

            currentJobHandle = tmpHelperStack.Dispose(currentJobHandle);
            if (!hasDamageFlags)
            {
                currentJobHandle = destructionCommandTimestamps.Dispose(currentJobHandle);
            }

            if (postReadTransform.HasValue)
            {
                var postProcessJob = new TurtleOrganPostProcessJob
                {
                    organInstances = organInstancesBuilder,
                    stemInstances = stemInstancesBuilder,
                    transformOrgans = postReadTransform.Value
                };
                currentJobHandle = postProcessJob.Schedule(currentJobHandle);
            }

            UnityEngine.Profiling.Profiler.EndSample();


            var cancelled = await currentJobHandle.ToUniTaskImmediateCompleteOnCancel(token, maxFameDelay: 2);
            if (cancelled || nativeData.IsDisposed)
            {
                currentJobHandle.Complete();
                organInstancesBuilder.Dispose();
                stemInstancesBuilder.Dispose();
                throw new OperationCanceledException();
            }

            return new TurtleMeshBuildingInstructions
            {
                organInstances = organInstancesBuilder,
                stemInstances = stemInstancesBuilder
            };
        }

        [BurstCompile]
        public struct TurtleCompilationJob : IJob
        {
            // Inputs
            public SymbolString<float> symbols;
            [ReadOnly]
            public NativeHashMap<int, TurtleOperation> operationsByKey;
            [ReadOnly]
            public NativeArray<TurtleOrganTemplate.Blittable> organData;

            // Outputs
            public NativeList<TurtleOrganInstance> organInstances;
            public NativeList<TurtleStemInstance> stemInstances;
            public EntityCommandBuffer spawnEntityBuffer;

            // volumetric info
            public TurtleVolumetricHandles volumetricHandles;
            public bool hasVolumetricDestruction;
            [ReadOnly]
            public NativeArray<float> volumetricDestructionTimestamps;
            public float earliestValidDestructionCommand;


            // tmp Working memory
            public TmpNativeStack<TurtleState> nativeTurtleStack;

            public CustomRuleSymbols customRules;

            public TurtleState currentState;

            public void Execute()
            {
                for (int symbolIndex = 0; symbolIndex < symbols.Length; symbolIndex++)
                {
                    var symbol = symbols[symbolIndex];
                    if (symbol == customRules.branchOpenSymbol)
                    {
                        nativeTurtleStack.Push(currentState);
                        continue;
                    }
                    if (symbol == customRules.branchCloseSymbol)
                    {
                        currentState = nativeTurtleStack.Pop();
                        continue;
                    }
                    if (customRules.hasIdentifiers && customRules.identifier == symbol)
                    {
                        currentState.organIdentity = new UIntFloatColor32(symbols.parameters[symbolIndex, 0]);
                        continue;
                    }
                    if(customRules.hasExtraVertexData && customRules.extraVertexDataSymbol == symbol)
                    {
                        var customDataParameters = symbols.parameters[symbolIndex];
                        var customData = currentState.customData;
                        var len = math.min(customDataParameters.length, 4);
                        for (int i = 0; i < len; i++)
                        {
                            customData[i] = (byte)math.clamp(symbols.parameters[customDataParameters, i] * 255, byte.MinValue, byte.MaxValue);
                        }
                        currentState.customData = customData;
                    }
                    if (operationsByKey.TryGetValue(symbol, out var operation))
                    {
                        operation.Operate(
                            ref currentState,
                            symbolIndex,
                            symbols,
                            organData,
                            organInstances,
                            stemInstances,
                            volumetricHandles,
                            spawnEntityBuffer);
                        if (hasVolumetricDestruction && customRules.hasAutophagy && operation.operationType == TurtleOperationType.ADD_ORGAN)
                        {
                            // check for an operation which may have changed the position of the turtle
                            var turtlePosition = currentState.transformation.MultiplyPoint(Vector3.zero); // extract transformation
                            var voxelIndex = volumetricHandles.durabilityWriter.GetVoxelIndexFromLocalSpace(turtlePosition);
                            if (voxelIndex.IsValid)
                            {
                                var lastDestroyCommandTime = volumetricDestructionTimestamps[voxelIndex.Value];
                                if (lastDestroyCommandTime >= earliestValidDestructionCommand)
                                {
                                    symbols[symbolIndex] = customRules.autophagicSymbol;
                                    // TODO: can skipping over this whole branching structure work here? could save some time
                                }
                            }
                        }
                    }
                }
            }
        }

        public struct TurtleOrganPostProcessJob : IJob
        {
            public NativeList<TurtleOrganInstance> organInstances;
            public NativeList<TurtleStemInstance> stemInstances;

            public Matrix4x4 transformOrgans;

            public void ExecuteA(int index)
            {
                var organ = organInstances[index];
                organ.organTransform = transformOrgans * (Matrix4x4)organ.organTransform;
                organInstances[index] = organ;
            }
            public void ExecuteB(int index)
            {
                var stem = stemInstances[index];
                stem.orientation = transformOrgans * stem.orientation;
                stemInstances[index] = stem;
            }

            public void Execute()
            {
                for (int i = 0; i < organInstances.Length; i++)
                {
                    ExecuteA(i);
                }
                for (int i = 0; i < stemInstances.Length; i++)
                {
                    ExecuteB(i);
                }
            }
        }
    }
}
