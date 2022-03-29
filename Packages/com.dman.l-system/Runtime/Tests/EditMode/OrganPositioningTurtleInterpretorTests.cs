using Cysharp.Threading.Tasks;
using Dman.LSystem.SystemCompiler.Linker;
using Dman.LSystem.SystemRuntime.CustomRules;
using Dman.LSystem.SystemRuntime.LSystemEvaluator;
using Dman.LSystem.SystemRuntime.Turtle;
using Dman.LSystem.UnityObjects;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class OrganPositioningTurtleInterpretorTests
{
    private OrganPositioningTurtleInterpretor GetInterpretor(char[] meshKeys, Action<MeshKey> meshKeyOverrides = null)
    {
        var meshOperations = ScriptableObject.CreateInstance<TurtleMeshOperations>();
        meshOperations.meshKeys = meshKeys.Select(x =>
        {
            var mesh = new MeshKey
            {
                Character = x,
                MeshRef = Resources.GetBuiltinResource<Mesh>("Cube.fbx"),
                MeshVariants = new MeshVariant[0],
                material = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"),
                IndividualScale = Vector3.one,
                ParameterScale = false,
                ScaleIsAdditional = false,
                VolumetricScale = false,
                ScalePerParameter = Vector3.one,
                AlsoMove = true,
                UseThickness = false,
                volumetricDurabilityValue = 0,
            };
            meshKeyOverrides?.Invoke(mesh);
            return mesh;
        }).ToArray();

        var turnOperations = ScriptableObject.CreateInstance<TurtleRotateOperations>();
        turnOperations.defaultRollTheta = 90;
        turnOperations.defaultTurnTheta = 90;
        turnOperations.defaultTiltTheta = 90;

        var opSets = new List<TurtleOperationSet>() { meshOperations, turnOperations };
        var defaultState = new TurtleState
        {
            transformation = Matrix4x4.identity,
            thickness = 1,
            organIdentity = new UIntFloatColor32(0)
        };
        var simpleRemapper = new SimpleSymbolRemapper();

        var customSymbols = new CustomRuleSymbols
        {
            branchOpenSymbol = '[',
            branchCloseSymbol = ']',
        };

        return new OrganPositioningTurtleInterpretor(opSets, defaultState, simpleRemapper, customSymbols);
    }


    private void ExpectPositions(List<TurtleOrganInstance> organs, List<Vector3> positions)
    {
        Assert.AreEqual(positions.Count, organs.Count);
        for (int instanceNum = 0; instanceNum < organs.Count; instanceNum++)
        {
            var instance = organs[instanceNum];
            var position = ((Matrix4x4)instance.organTransform).MultiplyPoint(Vector3.zero);
            // assert position and orientation
            var expectedPos = positions[instanceNum];

            Assert.AreEqual(expectedPos.x, position.x, 1e-5, $"Expected organ at {expectedPos}, but was at {position}. index {instanceNum}");
            Assert.AreEqual(expectedPos.y, position.y, 1e-5, $"Expected organ at {expectedPos}, but was at {position}. index {instanceNum}");
            Assert.AreEqual(expectedPos.z, position.z, 1e-5, $"Expected organ at {expectedPos}, but was at {position}. index {instanceNum}");
        }
    }

    [UnityTest]
    public IEnumerator TurtleOrganIdsCompilesSingleOrgan() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C' }))
        using (var systemState = new DefaultLSystemState("C"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(.5f, 0, 0),
        };
        ExpectPositions(organInstances, expected);
    });

    [UnityTest]
    public IEnumerator TurtleOrganCompilesMultiOrgans() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C' }))
        using (var systemState = new DefaultLSystemState("CCCCC"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(0.5f, 0, 0),
            new Vector3(1.5f, 0, 0),
            new Vector3(2.5f, 0, 0),
            new Vector3(3.5f, 0, 0),
            new Vector3(4.5f, 0, 0),
        };
        ExpectPositions(organInstances, expected);
    });

    [UnityTest]
    public IEnumerator TurtleOrganCompilesMultiOrganTypes() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C', 'D' }))
        using (var systemState = new DefaultLSystemState("CDCDDCDCDCDDDC"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(0.5f, 0, 0),
            new Vector3(2.5f, 0, 0),
            new Vector3(5.5f, 0, 0),
            new Vector3(7.5f, 0, 0),
            new Vector3(9.5f, 0, 0),
            new Vector3(13.5f, 0, 0),
        };
        ExpectPositions(organInstances, expected);
    });

    [UnityTest]
    public IEnumerator TurtleOrganCompilesWithBending() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C' }))
        using (var systemState = new DefaultLSystemState("C+C-C+C-C"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(0.5f, 0, 0),
            new Vector3(1, 0, 0.5f),
            new Vector3(1.5f, 0, 1),
            new Vector3(2, 0, 1.5f),
            new Vector3(2.5f, 0, 2),
        };
        ExpectPositions(organInstances, expected);
    });

    [UnityTest]
    public IEnumerator TurtleOrganCompilesWithBendingAndBranching() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C' }))
        using (var systemState = new DefaultLSystemState("C[+CCC][-CCC]"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(0.5f, 0, 0),
            new Vector3(1, 0, 0.5f),
            new Vector3(1, 0, 1.5f),
            new Vector3(1, 0, 2.5f),
            new Vector3(1, 0,-0.5f),
            new Vector3(1, 0,-1.5f),
            new Vector3(1, 0,-2.5f),
        };
        ExpectPositions(organInstances, expected);
    });

    [UnityTest]
    public IEnumerator TurtleOrganCompilesAndRespectsScalingOfOrgans() => UniTask.ToCoroutine(async () =>
    {
        List<TurtleOrganInstance> organInstances;
        using (var turtle = GetInterpretor(new[] { 'C' }, (mesh) => mesh.IndividualScale = new Vector3(2, 2, 2)))
        using (var systemState = new DefaultLSystemState("CCC"))
        using (var cancellation = new CancellationTokenSource())
        using (var meshInstances = await turtle.CompileStringToMeshOrganInstances(systemState.currentSymbols, cancellation.Token))
        {
            organInstances = turtle.FilterOrgansByCharacter(meshInstances, 'C').ToList();
        }

        var expected = new List<Vector3>
        {
            new Vector3(1f, 0, 0),
            new Vector3(3f, 0, 0),
            new Vector3(5f, 0, 0),
        };
        ExpectPositions(organInstances, expected);
    });
}
