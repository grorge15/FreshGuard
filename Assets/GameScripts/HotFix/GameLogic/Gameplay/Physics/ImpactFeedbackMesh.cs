using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameLogic
{
    /// <summary>复用动态网格和顶点缓冲；碰撞表现不创建粒子对象或修改物理尺寸。</summary>
    internal sealed class ImpactFeedbackMesh : IDisposable
    {
        private readonly List<Vector3> _vertices = new List<Vector3>(2048);
        private readonly List<Color32> _colors = new List<Color32>(2048);
        private readonly List<int> _triangles = new List<int>(3072);
        private readonly Mesh _mesh;
        internal readonly MeshRenderer Renderer;
        internal Transform Transform => Renderer.transform;
        internal int VertexCount => _vertices.Count;

        internal ImpactFeedbackMesh(Transform parent, string name, Material material, int layer, int order)
        {
            var node = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            _mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            node.GetComponent<MeshFilter>().sharedMesh = _mesh;
            Renderer = node.GetComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.sortingLayerID = layer;
            Renderer.sortingOrder = order;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.enabled = false;
        }

        internal void Begin() { _vertices.Clear(); _colors.Clear(); _triangles.Clear(); }

        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color) => Triangle(a, b, c, color, color, color);

        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            var index = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
            _colors.Add(ca); _colors.Add(cb); _colors.Add(cc);
            _triangles.Add(index); _triangles.Add(index + 1); _triangles.Add(index + 2);
        }

        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color start, Color end)
        {
            Triangle(a, b, c, start, start, end);
            Triangle(a, c, d, start, end, end);
        }

        internal void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            var direction = b - a;
            if (direction.sqrMagnitude < 0.00000001f) return;
            var side = new Vector3(-direction.y, direction.x, 0f).normalized * (width * 0.5f);
            Quad(a - side, a + side, b + side, b - side, color, color);
        }

        internal void Disc(Vector3 center, float radius, Color color, bool soft = false)
        {
            const int segments = 20;
            var edgeColor = color;
            if (soft) edgeColor.a = 0f;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                var b = (i + 1) * Mathf.PI * 2f / segments;
                Triangle(center,
                    center + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    center + new Vector3(Mathf.Cos(b), Mathf.Sin(b)) * radius, color, edgeColor, edgeColor);
            }
        }

        internal void Commit()
        {
            _mesh.Clear();
            Renderer.enabled = _vertices.Count > 0;
            if (!Renderer.enabled) return;
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        internal void Clear() { Begin(); if (_mesh != null) _mesh.Clear(); if (Renderer != null) Renderer.enabled = false; }

        public void Dispose()
        {
            Destroy(_mesh);
            if (Renderer != null) Destroy(Renderer.gameObject);
        }

        internal static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }

        internal static Material CreateMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            return shader == null ? null : new Material(shader)
                { name = "BallFeedback", hideFlags = HideFlags.DontSave, mainTexture = Texture2D.whiteTexture };
        }
    }
}
