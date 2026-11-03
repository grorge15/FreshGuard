using UnityEngine;

namespace GameLogic
{
    /// <summary>沿整体外轮廓偏移边缘；凹形机器人不使用圆圈或包围盒，且不改碰撞体。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotImpactOutline : MonoBehaviour
    {
        private const float Duration = 0.28f;
        private Material _material;
        private ImpactFeedbackMesh _mesh;
        private Vector3[][] _points;
        private Vector3[][] _outward;
        private float _elapsed;
        internal bool IsPlaying { get; private set; }

        internal void Play(SpriteRenderer icon, PolygonCollider2D collider)
        {
            if (icon == null || collider == null || !icon.enabled) return;
            if (_mesh == null && !Initialize(icon, collider)) return;
            _elapsed = 0f;
            IsPlaying = true;
            Draw();
        }

        private bool Initialize(SpriteRenderer icon, PolygonCollider2D collider)
        {
            _material = ImpactFeedbackMesh.CreateMaterial();
            if (_material == null) return false;
            _mesh = new ImpactFeedbackMesh(icon.transform, "RobotImpactOutline", _material,
                icon.sortingLayerID, icon.sortingOrder + 20);
            _points = new Vector3[collider.pathCount][];
            _outward = new Vector3[collider.pathCount][];
            for (var path = 0; path < collider.pathCount; path++)
            {
                var vertices = collider.GetPath(path);
                _points[path] = new Vector3[vertices.Length];
                _outward[path] = new Vector3[vertices.Length];
                var area = 0f;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var next = vertices[(i + 1) % vertices.Length];
                    area += vertices[i].x * next.y - next.x * vertices[i].y;
                }
                var winding = area >= 0f ? 1f : -1f;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var before = (vertices[i] - vertices[(i + vertices.Length - 1) % vertices.Length]).normalized;
                    var after = (vertices[(i + 1) % vertices.Length] - vertices[i]).normalized;
                    var first = new Vector2(before.y, -before.x) * winding;
                    var second = new Vector2(after.y, -after.x) * winding;
                    var miter = (first + second) / Mathf.Max(0.001f, 1f + Vector2.Dot(first, second));
                    // Undo the physical safety inset only in the visual copy.
                    var point = vertices[i] + collider.offset + miter * (MapGridMath.REFERENCE_CELL_SIZE * 0.06f);
                    _points[path][i] = icon.transform.InverseTransformPoint(collider.transform.TransformPoint(point));
                    _outward[path][i] = icon.transform.InverseTransformVector(collider.transform.TransformVector(miter));
                }
            }
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null) BattleSideRenderGroup.BindRoot(gameObject, group.Registry, group.Side);
            return true;
        }

        internal void Advance(float deltaTime)
        {
            if (!IsPlaying) return;
            _elapsed += Mathf.Max(0f, deltaTime);
            if (_elapsed >= Duration) { Clear(); return; }
            Draw();
        }

        private void Draw()
        {
            var expansion = Mathf.SmoothStep(0f, 1f, _elapsed / 0.2f);
            var opacity = Mathf.SmoothStep(0f, 1f, _elapsed / 0.025f) *
                (1f - Mathf.SmoothStep(0f, 1f, (_elapsed - 0.055f) / 0.225f));
            var gap = Mathf.Lerp(0.018f, 0.055f, expansion);
            _mesh.Begin();
            Ribbon(gap - 0.004f, gap + 0.032f, new Color(0.21f, 0.41f, 0.53f, 0.28f * opacity));
            Ribbon(gap - 0.006f, gap + 0.026f, new Color(1f, 1f, 1f, 0.12f * opacity));
            Ribbon(gap, gap + 0.018f, new Color(1f, 0.996f, 0.98f, 0.96f * opacity));
            _mesh.Commit();
        }

        private void Ribbon(float inner, float outer, Color color)
        {
            for (var path = 0; path < _points.Length; path++)
            {
                var points = _points[path];
                var outward = _outward[path];
                for (var i = 0; i < points.Length; i++)
                {
                    var next = (i + 1) % points.Length;
                    _mesh.Quad(points[i] + outward[i] * inner, points[i] + outward[i] * outer,
                        points[next] + outward[next] * outer, points[next] + outward[next] * inner, color, color);
                }
            }
        }

        internal void Clear() { IsPlaying = false; _elapsed = 0f; _mesh?.Clear(); }
        private void OnDisable() => Clear();
        private void OnDestroy() { _mesh?.Dispose(); ImpactFeedbackMesh.Destroy(_material); }
    }
}
