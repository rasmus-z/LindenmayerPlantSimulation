﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProceduralToolkit.SplineMesh {
    /// <summary>
    /// Example of component to show the deformation of a mesh in a changing
    /// interval in spline space.
    /// 
    /// This component is only for demo purpose and is not intended to be used as-is.
    /// </summary>
    [ExecuteInEditMode]
    [RequireComponent(typeof(Spline))]
    public class ExampleContortAlong : MonoBehaviour {
        private Spline spline;
        private float rate = 0;
        private MeshBender meshBender;

        [HideInInspector]
        public GameObject generated;

        public Mesh mesh;
        public Material material;
        public Vector3 rotation;
        public Vector3 scale;

        public float DurationInSecond;

        private void OnEnable() {
            rate = 0;
            Init();
#if UNITY_EDITOR
            EditorApplication.update += EditorUpdate;
#endif
        }

        void OnDisable() {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
        }

        private void OnValidate() {
            Init();
        }

        void EditorUpdate() {
            rate += Time.deltaTime / DurationInSecond;
            if (rate > 1) {
                rate --;
            }
            Contort();
        }

        private void Contort() {
            if (generated != null) {
                meshBender.SetInterval(spline, spline.Length * rate);
                meshBender.ComputeIfNeeded();
            }
        }

        private void Init() {
            string generatedName = "generated by " + GetType().Name;
            var generatedTranform = transform.Find(generatedName);
            generated = generatedTranform != null ? generatedTranform.gameObject : UOUtility.Create(generatedName, gameObject,
                typeof(MeshFilter),
                typeof(MeshRenderer),
                typeof(MeshBender));

            generated.GetComponent<MeshRenderer>().material = material;

            meshBender = generated.GetComponent<MeshBender>();
            spline = GetComponent<Spline>();

            meshBender.Source = SourceMesh.Build(mesh)
                .Rotate(Quaternion.Euler(rotation))
                .Scale(scale);
            meshBender.Mode = MeshBender.FillingMode.Once;
            meshBender.SetInterval(spline, 0);
        }
    }
}
