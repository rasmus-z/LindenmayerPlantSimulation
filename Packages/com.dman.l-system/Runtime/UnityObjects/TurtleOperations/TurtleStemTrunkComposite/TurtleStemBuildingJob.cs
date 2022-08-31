﻿using Dman.LSystem.SystemRuntime.Turtle;
using Dman.Utilities.Math;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Dman.LSystem.UnityObjects.StemTrunk
{
    [BurstCompile]
    internal struct TurtleStemBuildingJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<TurtleMeshAllocationCounter> submeshSizes;

        [ReadOnly]
        public NativeArray<TurtleStemInstance> stemInstances;
        [ReadOnly]
        public NativeArray<OrganMeshMemorySpaceAllocation> organMeshAllocations;

        public int meshMemoryOffset;

        public TurtleMeshData targetMesh;

        public void Execute(int stemIndex)
        {
            var vertexTargetData = targetMesh.vertexData;
            var triangleIndexes = targetMesh.indices;

            var stemInstance = stemInstances[stemIndex];
            var pointTransform = stemInstance.orientation;
            var meshMemorySpace = organMeshAllocations[stemIndex + meshMemoryOffset];
            var submeshData = submeshSizes[stemInstance.materialIndex];

            var vertexOffset = submeshData.indexInVertexes + meshMemorySpace.vertexMemorySpace.index;

            var angleMultiplier = math.PI * 2f / stemInstance.radialResolution;
            for (int theta = 0; theta < stemInstance.radialResolution; theta++)
            {
                var radians = theta * angleMultiplier;
                var point = new float3(0, math.sin(radians), math.cos(radians));
                vertexTargetData[theta + vertexOffset] = new MeshVertexLayout
                {
                    pos = pointTransform.MultiplyPoint3x4(point),
                    normal = pointTransform.MultiplyVector(point),
                    uv = float2.zero,
                    color = ColorFromIdentity(stemInstance.organIdentity, (uint)stemIndex),
                    extraData = byte4.ZERO
                };
            }
            if (stemInstance.parentIndex < 0)
            {
                return;
            }
            var triangleOffset = submeshData.indexInTriangles + meshMemorySpace.trianglesMemorySpace.index;
            var parentStem = stemInstances[stemInstance.parentIndex];
            if (parentStem.radialResolution != stemInstance.radialResolution || parentStem.materialIndex != stemInstance.materialIndex)
            {
                for (int i = 0; i < meshMemorySpace.trianglesMemorySpace.length; i++)
                {
                    // clear out triangle indexes to 0 here
                    triangleIndexes[i + triangleOffset] = 0;
                }
                return;
            }
            var parentStemMeshMemory = organMeshAllocations[stemInstance.parentIndex + meshMemoryOffset];
            // create the rectangle strip. only supported when equal radial vertex count and same submesh
            var parentVertexOffset = submeshData.indexInVertexes + parentStemMeshMemory.vertexMemorySpace.index;

            var myCircleIndexOffset = (GetNormalizedCircleOffset(parentStem.orientation, pointTransform) + 1) * stemInstance.radialResolution;


            for (int rectIndex = 0; rectIndex < stemInstance.radialResolution; rectIndex++)
            {
                var nextIndex = (rectIndex + 1) % stemInstance.radialResolution;
                var p1 = (uint)(rectIndex + parentVertexOffset);
                var p2 = (uint)(nextIndex + parentVertexOffset);

                // offset child indexes by index offset, determined by angular difference with the parent
                var childIndex = (rectIndex + myCircleIndexOffset) % stemInstance.radialResolution;
                var childNextIndex = (rectIndex + myCircleIndexOffset + 1) % stemInstance.radialResolution;
                var c1 = (uint)(childIndex + vertexOffset);
                var c2 = (uint)(childNextIndex + vertexOffset);

                var triangleBase = rectIndex * 6 + triangleOffset;
                triangleIndexes[triangleBase + 0] = p1;
                triangleIndexes[triangleBase + 1] = c1;
                triangleIndexes[triangleBase + 2] = c2;

                triangleIndexes[triangleBase + 3] = p1;
                triangleIndexes[triangleBase + 4] = c2;
                triangleIndexes[triangleBase + 5] = p2;
            }
        }

        /// <summary>
        /// returns a value representing the rotation required to align the y axis of <paramref name="next"/> up as closely as posslbe to the y axis of <paramref name="parent"/>
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="next"></param>
        /// <returns>a value between -0.5 and 0.5, representing rotations about the x-axis from -180 to 180 degrees</returns>
        private float GetNormalizedCircleOffset(Matrix4x4 parent, Matrix4x4 next)
        {
            var parentBasisPlaneX = parent.MultiplyVector(Vector3.forward);
            var parentBasisPlaneY = parent.MultiplyVector(Vector3.up);
            var parentBasisPlaneNormal = parent.MultiplyVector(Vector3.right);

            var nextY = next.MultiplyVector(Vector3.up);
            var nextYProjectedOnParentBasisPlane = ProjectOntoPlane(nextY, parentBasisPlaneX, parentBasisPlaneY);

            var angleOffset = Vector3.SignedAngle(parentBasisPlaneY, nextYProjectedOnParentBasisPlane, parentBasisPlaneNormal);
            return angleOffset / 360f;
        }

        private Vector3 ProjectOntoPlane(Vector3 projectionVector, Vector3 planeBasisX, Vector3 planeBasisY)
        {
            var projectedX = planeBasisX * (Vector3.Dot(planeBasisX, projectionVector));
            var projectedY = planeBasisY * (Vector3.Dot(planeBasisY, projectionVector));
            return projectedX + projectedY;
        }

        private Color32 ColorFromIdentity(UIntFloatColor32 identity, uint index)
        {
            //identity.UIntValue = BitMixer.Mix(identity.UIntValue);

            return identity.color;
        }
    }
}
