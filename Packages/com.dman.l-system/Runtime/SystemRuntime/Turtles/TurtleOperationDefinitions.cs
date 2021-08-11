﻿using Dman.LSystem.SystemRuntime.NativeCollections;
using Dman.LSystem.SystemRuntime.VolumetricData;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Dman.LSystem.SystemRuntime.Turtle
{
    [StructLayout(LayoutKind.Explicit)]
    public struct TurtleOperation
    {
        [FieldOffset(0)] public TurtleOperationType operationType;
        /// <summary>
        /// organ addition operation
        /// </summary>
        [FieldOffset(1)] public TurtleMeshOperation meshOperation;
        /// <summary>
        /// bend towards operations
        /// </summary>
        [FieldOffset(1)] public TurtleBendTowardsOperationNEW bendTowardsOperation;
        /// <summary>
        /// thickness operation
        /// </summary>
        [FieldOffset(1)] public TurtleThiccnessOperation thiccnessOperation;
        /// <summary>
        /// scale operation
        /// </summary>
        [FieldOffset(1)] public TurtleScaleOperation scaleOperation;

        /// <summary>
        /// rotate operation
        /// </summary>
        [FieldOffset(1)] public TurtleRotationOperation rotationOperation;

        public void Operate(
            ref TurtleState currentState,
            NativeArray<TurtleMeshAllocationCounter> meshSizeCounterPerSubmesh,
            int indexInString,
            SymbolString<float> sourceString,
            NativeArray<TurtleOrganTemplate.Blittable> allOrgans,
            NativeList<TurtleOrganInstance> targetOrganInstances,
            VolumetricWorldNativeWritableHandle volumetricNativeWriter)
        {
            switch (operationType)
            {
                case TurtleOperationType.BEND_TOWARDS:
                    bendTowardsOperation.Operate(ref currentState, indexInString, sourceString);
                    break;
                case TurtleOperationType.ADD_ORGAN:
                    meshOperation.Operate(ref currentState, meshSizeCounterPerSubmesh, indexInString, sourceString, allOrgans, targetOrganInstances, volumetricNativeWriter);
                    break;
                case TurtleOperationType.ROTATE:
                    rotationOperation.Operate(ref currentState, indexInString, sourceString);
                    break;
                case TurtleOperationType.SCALE_TRANSFORM:
                    scaleOperation.Operate(ref currentState, indexInString, sourceString);
                    break;
                case TurtleOperationType.SCALE_THICCNESS:
                    thiccnessOperation.Operate(ref currentState, indexInString, sourceString);
                    break;
                default:
                    break;
            }
            return;
        }
    }

    public struct TurtleMeshOperation
    {
        public float3 extraNonUniformScaleForOrgan;
        public bool doScaleMesh;
        public bool scaleIsAdditional;
        public bool isVolumetricScale;
        public bool doApplyThiccness;
        public JaggedIndexing organTemplateVariants;

        public float volumetricValue;

        public void Operate(
            ref TurtleState state,
            NativeArray<TurtleMeshAllocationCounter> meshSizeCounterPerSubmesh,
            int indexInString,
            SymbolString<float> sourceString,
            NativeArray<TurtleOrganTemplate.Blittable> allOrgans,
            NativeList<TurtleOrganInstance> targetOrganInstances,
            VolumetricWorldNativeWritableHandle volumetricNativeWriter)
        {
            var pIndex = sourceString.parameters[indexInString];

            var meshTransform = state.transformation;

            var selectedMeshIndex = 0;
            if (organTemplateVariants.length > 1 && pIndex.length > 0)
            {
                var index = ((int)sourceString.parameters[pIndex, 0]) % organTemplateVariants.length;
                selectedMeshIndex = index;
            }
            var selectedOrganIndex = organTemplateVariants.index + selectedMeshIndex;
            var selectedOrgan = allOrgans[selectedOrganIndex];


            var turtleTranslate = selectedOrgan.translation;
            var scaleIndex = organTemplateVariants.length <= 1 ? 0 : 1;
            if (doScaleMesh && pIndex.length > scaleIndex)
            {
                var scale = sourceString.parameters[pIndex, scaleIndex];
                if (isVolumetricScale)
                {
                    scale = Mathf.Pow(scale, 1f / 3f);
                }
                var scaleVector = (scaleIsAdditional ? new float3(1, 1, 1) : new float3(0, 0, 0)) + (extraNonUniformScaleForOrgan * scale);

                if (selectedOrgan.alsoMove)
                {
                    turtleTranslate.Scale(scaleVector);
                }
                
                meshTransform *= Matrix4x4.Scale(scaleVector);
            }
            if (doApplyThiccness)
            {
                meshTransform *= Matrix4x4.Scale(new Vector3(1, state.thickness, state.thickness));
            }

            if (volumetricValue != 0)
            {
                var organCenter = meshTransform.MultiplyPoint(Vector3.zero);
                volumetricNativeWriter.WriteVolumetricAmountToTarget(volumetricValue, organCenter);
            }

            var meshSizeForSubmesh = meshSizeCounterPerSubmesh[selectedOrgan.materialIndex];

            var newOrganEntry = new TurtleOrganInstance
            {
                organIndexInAllOrgans = (ushort)selectedOrganIndex,
                organTransform = meshTransform,
                vertexMemorySpace = new JaggedIndexing
                {
                    index = meshSizeForSubmesh.totalVertexes,
                    length = selectedOrgan.vertexes.length
                },
                trianglesMemorySpace = new JaggedIndexing
                {
                    index = meshSizeForSubmesh.totalTriangleIndexes,
                    length = selectedOrgan.trianges.length
                },
                organIdentity = state.organIdentity
            };
            targetOrganInstances.Add(newOrganEntry);

            meshSizeForSubmesh.totalVertexes += newOrganEntry.vertexMemorySpace.length;
            meshSizeForSubmesh.totalTriangleIndexes += newOrganEntry.trianglesMemorySpace.length;
            meshSizeCounterPerSubmesh[selectedOrgan.materialIndex] = meshSizeForSubmesh;

            if (selectedOrgan.alsoMove)
            {
                state.transformation *= Matrix4x4.Translate(turtleTranslate);
            }
        }
    }
    public struct TurtleBendTowardsOperationNEW
    {
        public float3 defaultBendDirection;
        public float defaultBendFactor;
        public void Operate(
            ref TurtleState state,
            int indexInString,
            SymbolString<float> sourceString)
        {
            var paramIndex = sourceString.parameters[indexInString];
            float bendFactor = defaultBendFactor;
            if (paramIndex.length == 1)
            {
                bendFactor = sourceString.parameters[paramIndex, 0];
            }
            var localBendDirection = state.transformation.inverse.MultiplyVector(defaultBendDirection);
            var adjustment = (bendFactor) * (Vector3.Cross(localBendDirection, Vector3.right));
            state.transformation *= Matrix4x4.Rotate(
                Quaternion.Slerp(
                    Quaternion.identity,
                    Quaternion.FromToRotation(
                        Vector3.right,
                        localBendDirection),
                    adjustment.magnitude
                )
            );
        }
    }
    public struct TurtleThiccnessOperation
    {
        public float defaultThicknessScale;
        public void Operate(
            ref TurtleState state,
            int indexInString,
            SymbolString<float> sourceString)
        {
            var paramIndex = sourceString.parameters[indexInString];

            if (paramIndex.length == 0)
            {
                state.thickness *= defaultThicknessScale;
            }
            else
            if (paramIndex.length == 1)
            {
                state.thickness *= sourceString.parameters[paramIndex, 0];
            }
            else
            {
                Debug.LogError($"Invalid scale parameter length: {paramIndex.length}");
            }
        }
    }
    public struct TurtleScaleOperation
    {
        public float3 nonUniformScale;
        public float defaultScaleFactor;
        public void Operate(
            ref TurtleState state,
            int indexInString,
            SymbolString<float> sourceString)
        {
            var paramIndex = sourceString.parameters[indexInString];
            if (paramIndex.length == 0)
            {
                state.transformation *= Matrix4x4.Scale(defaultScaleFactor * nonUniformScale);
            }
            else
            if (paramIndex.length == 1)
            {
                state.transformation *= Matrix4x4.Scale(sourceString.parameters[paramIndex, 0] * nonUniformScale);
            }
            else
            if (paramIndex.length == 3)
            {
                state.transformation *= Matrix4x4.Scale(
                    new Vector3(
                        sourceString.parameters[paramIndex, 0],
                        sourceString.parameters[paramIndex, 1],
                        sourceString.parameters[paramIndex, 2]
                    ));
            }
            else
            {
                Debug.LogError($"Invalid scale parameter length: {paramIndex.length}");
            }
        }
    }
    public struct TurtleRotationOperation
    {
        public float3 unitEulerRotation;
        public float defaultTheta;
        public void Operate(
            ref TurtleState state,
            int indexInString,
            SymbolString<float> sourceString)
        {
            var paramIndex = sourceString.parameters[indexInString];
            float theta = defaultTheta;
            if (paramIndex.length == 1)
            {
                theta = sourceString.parameters[paramIndex, 0];
            }
            state.transformation *= Matrix4x4.Rotate(Quaternion.Euler(theta * unitEulerRotation));
        }
    }

    public enum TurtleOperationType : byte
    {
        BEND_TOWARDS,
        ADD_ORGAN,
        ROTATE,
        SCALE_TRANSFORM,
        SCALE_THICCNESS
    }
}
