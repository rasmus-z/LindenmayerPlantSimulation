﻿using Dman.LSystem.SystemRuntime.CustomRules;
using Dman.LSystem.SystemRuntime.GlobalCoordinator;
using Dman.LSystem.SystemRuntime.LSystemEvaluator;
using Dman.LSystem.SystemRuntime.NativeCollections;
using Dman.LSystem.SystemRuntime.NativeCollections.NativeVolumetricSpace;
using Dman.LSystem.SystemRuntime.ThreadBouncer;
using Dman.LSystem.SystemRuntime.VolumetricData.NativeVoxels;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dman.LSystem.SystemRuntime.VolumetricData
{
    public class DoubleBufferModifierHandle: ModifierHandle
    {
        public NativeArray<float> valuesA;
        public NativeArray<float> valuesB;
        /// <summary>
        /// the layer index which is buffered into valuesA and valuesB
        /// </summary>
        public readonly int doubleBufferedLayerIndex;

        public JobHandle writeDependency;
        public bool newDataIsAvailable;
        public bool mostRecentDataInA;
        public bool IsDisposed { get; private set; }


        public NativeArray<float> newValues => mostRecentDataInA ? valuesA : valuesB;
        public NativeArray<float> oldValues => mostRecentDataInA ? valuesB : valuesA;


        public VolumetricWorldVoxelLayout voxelLayout;

        public DoubleBufferModifierHandle(VolumetricWorldVoxelLayout voxels, int doubleBufferedLayerIndex)
        {
            valuesA = new NativeArray<float>(voxels.totalVoxels, Allocator.Persistent);
            valuesB = new NativeArray<float>(voxels.totalVoxels, Allocator.Persistent);
            mostRecentDataInA = true;

            this.voxelLayout = voxels;
            this.doubleBufferedLayerIndex = doubleBufferedLayerIndex;
            Debug.Log("double buffered modifier on layer " + doubleBufferedLayerIndex);
        }

        public bool ConsolidateChanges(VoxelWorldVolumetricLayerData layerData, ref JobHandleWrapper dependency)
        {
            // TODO: consider skipping if writeDependency is not complete yet. should the job chain keep extending, or should
            //  consolidation be deffered?
            if (!newDataIsAvailable)
            {
                return false;
            }
            var consolidationJob = new VoxelMarkerConsolidation
            {
                allBaseMarkers = layerData,
                oldMarkerLevels = oldValues,
                newMarkerLevels = newValues,
                markerLayerIndex = doubleBufferedLayerIndex,
            };
            dependency = consolidationJob.Schedule(newValues.Length, 1000, dependency + writeDependency);
            RegisterReadDependency(dependency);
            newDataIsAvailable = false;
            return true;
        }
        public void RemoveEffects(VoxelWorldVolumetricLayerData layerData, ref JobHandleWrapper dependency)
        {
            var layout = layerData.VoxelLayout;
            var subtractCleanupJob = new NativeArraySubtractNegativeProtectionJob
            {
                allBaseMarkers = layerData,
                markerLevelsToRemove = newValues,
                markerLayerIndex = doubleBufferedLayerIndex,
                totalLayersInBase = layout.dataLayerCount
            };
            dependency = subtractCleanupJob.Schedule(layout.totalVoxels, 1000, dependency + writeDependency);
        }


        public DoubleBufferNativeWritableHandle GetNextNativeWritableHandle(
            Matrix4x4 localToWorldTransform, 
            ref JobHandleWrapper dependency)
        {
            UnityEngine.Profiling.Profiler.BeginSample("volume clearing");
            if (!newDataIsAvailable)
            {
                // Only swap data if the old data hasn't been picked up yet.
                //  just update the new data where it is
                mostRecentDataInA = !mostRecentDataInA;
                newDataIsAvailable = true;
            }
            var mostRecentVolumeData = mostRecentDataInA ? valuesA : valuesB;
            var volumeClearJob = new NativeArrayClearJob
            {
                newValue = 0f,
                writeArray = mostRecentVolumeData
            };

            dependency = volumeClearJob.Schedule(mostRecentVolumeData.Length, 10000, dependency + writeDependency);
            writeDependency = dependency;

            UnityEngine.Profiling.Profiler.EndSample();

            return new DoubleBufferNativeWritableHandle(
                mostRecentVolumeData,
                voxelLayout, 
                localToWorldTransform);
        }

        public void RegisterWriteDependency(JobHandle newWriteDependency)
        {
            this.writeDependency = JobHandle.CombineDependencies(newWriteDependency, this.writeDependency);
        }

        public void RegisterReadDependency(JobHandle newReadDependency)
        {
            this.writeDependency = JobHandle.CombineDependencies(newReadDependency, this.writeDependency);
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;
            valuesA.Dispose();
            valuesB.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (IsDisposed)
            {
                return inputDeps;
            }
            IsDisposed = true;
            return JobHandle.CombineDependencies(
                valuesA.Dispose(inputDeps),
                valuesB.Dispose(inputDeps));
        }
    }
}
