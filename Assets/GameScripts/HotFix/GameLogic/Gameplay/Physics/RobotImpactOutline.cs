using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>沿整体占格轮廓绘制圆角描边与柔光，保留凹形缺口，不改碰撞体。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotImpactOutline : MonoBehaviour
    {
        private const float Duration = 0.28f;
        private const float OUTLINE_WIDTH_SCALE = 1.6f;
        private const float HALO_WIDTH_SCALE = 1.27f;
        private const int CORNER_SEGMENTS = 8;
        private const float CORNER_RADIUS = MapGridMath.REFERENCE_CELL_SIZE * 0.075f;
        private const float MIN_CORNER_RADIUS = MapGridMath.REFERENCE_CELL_SIZE * 0.02f;
        private Material _material;
        private ImpactFeedbackMesh _mesh;
        private OutlinePoint[][] _paths;
        private float _elapsed;
        private Color _levelColor;
        internal bool IsPlaying { get; private set; }

        private struct OutlinePoint
        {
            public Vector3 Corner, Outward, ArcOffset;
            public float Turn, MaximumRadius;

            public Vector3 At(float offset)
            {
                var radius = Mathf.Clamp(CORNER_RADIUS + Turn * offset,
                    Mathf.Min(MIN_CORNER_RADIUS, MaximumRadius), MaximumRadius);
                return Corner + Outward * offset + ArcOffset * radius;
            }
        }

        internal void Play(SpriteRenderer icon, PolygonCollider2D collider, int level)
        {
            if (icon == null || collider == null || !icon.enabled) return;
            if (_mesh == null && !Initialize(icon, collider)) return;
            _levelColor = RobotLevelStyle.ImpactColorForLevel(level);
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
            var visibleContours = ReadVisibleContours(icon, collider);
            _paths = new OutlinePoint[collider.pathCount][];
            for (var path = 0; path < collider.pathCount; path++)
            {
                var vertices = collider.GetPath(path);
                _paths[path] = new OutlinePoint[vertices.Length * (CORNER_SEGMENTS + 1)];
                var area = 0f;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var next = vertices[(i + 1) % vertices.Length];
                    area += vertices[i].x * next.y - next.x * vertices[i].y;
                }
                var winding = area >= 0f ? 1f : -1f;
                var edgeOffsets = new float[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                {
                    var next = vertices[(i + 1) % vertices.Length];
                    var direction = (next - vertices[i]).normalized;
                    var normal = new Vector2(direction.y, -direction.x) * winding;
                    edgeOffsets[i] = FindVisibleEdgeOffset((vertices[i] + next) * 0.5f + collider.offset,
                        normal, visibleContours);
                }
                for (var i = 0; i < vertices.Length; i++)
                {
                    var before = (vertices[i] - vertices[(i + vertices.Length - 1) % vertices.Length]).normalized;
                    var after = (vertices[(i + 1) % vertices.Length] - vertices[i]).normalized;
                    var first = new Vector2(before.y, -before.x) * winding;
                    var second = new Vector2(after.y, -after.x) * winding;
                    var miter = (first + second) / Mathf.Max(0.001f, 1f + Vector2.Dot(first, second));
                    // 以图片可见边缘定位视觉轮廓，避免碰撞安全间距或 PNG 留白带来额外空隙。
                    var dot = Vector2.Dot(first, second);
                    var edgeShift = ((first - second * dot) * edgeOffsets[(i + vertices.Length - 1) % vertices.Length] +
                        (second - first * dot) * edgeOffsets[i]) / Mathf.Max(0.001f, 1f - dot * dot);
                    var point = vertices[i] + collider.offset + edgeShift;
                    var angle = Mathf.Atan2(before.x * after.y - before.y * after.x, Vector2.Dot(before, after));
                    var turn = angle * winding >= 0f ? 1f : -1f;
                    var edgeLength = Mathf.Min(
                        Vector2.Distance(vertices[i], vertices[(i + vertices.Length - 1) % vertices.Length]),
                        Vector2.Distance(vertices[i], vertices[(i + 1) % vertices.Length]));
                    var maximumRadius = edgeLength * 0.45f / Mathf.Max(0.001f, Mathf.Tan(Mathf.Abs(angle) * 0.5f));
                    var corner = icon.transform.InverseTransformPoint(collider.transform.TransformPoint(point));
                    var outward = icon.transform.InverseTransformVector(collider.transform.TransformVector(miter));
                    for (var sample = 0; sample <= CORNER_SEGMENTS; sample++)
                    {
                        var theta = angle * sample / CORNER_SEGMENTS;
                        var radial = new Vector2(first.x * Mathf.Cos(theta) - first.y * Mathf.Sin(theta),
                            first.x * Mathf.Sin(theta) + first.y * Mathf.Cos(theta)) * turn;
                        _paths[path][i * (CORNER_SEGMENTS + 1) + sample] = new OutlinePoint
                        {
                            Corner = corner,
                            Outward = outward,
                            ArcOffset = icon.transform.InverseTransformVector(
                                collider.transform.TransformVector(radial - miter * turn)),
                            Turn = turn,
                            MaximumRadius = maximumRadius
                        };
                    }
                }
            }
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null) BattleSideRenderGroup.BindRoot(gameObject, group.Registry, group.Side);
            return true;
        }

        private static Vector2[][] ReadVisibleContours(SpriteRenderer icon, PolygonCollider2D collider)
        {
            var contours = new Vector2[icon.sprite.GetPhysicsShapeCount()][];
            var points = new List<Vector2>();
            for (var path = 0; path < contours.Length; path++)
            {
                points.Clear();
                icon.sprite.GetPhysicsShape(path, points);
                contours[path] = new Vector2[points.Count];
                for (var i = 0; i < points.Count; i++)
                    contours[path][i] = collider.transform.InverseTransformPoint(icon.transform.TransformPoint(points[i]));
            }
            return contours;
        }

        private static float FindVisibleEdgeOffset(Vector2 point, Vector2 normal, Vector2[][] contours)
        {
            var distance = float.PositiveInfinity;
            var offset = 0f;
            foreach (var contour in contours)
                for (var i = 0; i < contour.Length; i++)
                {
                    var a = contour[i];
                    var direction = contour[(i + 1) % contour.Length] - a;
                    var progress = Mathf.Clamp01(Vector2.Dot(point - a, direction) /
                        Mathf.Max(0.00000001f, direction.sqrMagnitude));
                    var delta = a + direction * progress - point;
                    if (delta.sqrMagnitude >= distance) continue;
                    distance = delta.sqrMagnitude;
                    offset = Vector2.Dot(delta, normal);
                }
            return offset;
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
            var gap = Mathf.Lerp(0f, 0.055f, expansion);
            var width = 0.018f * OUTLINE_WIDTH_SCALE;
            var center = gap + width * 0.5f;
            var glow = _levelColor;
            glow.a = 0.16f * opacity;
            var light = _levelColor;
            light.a = 0.24f * opacity;
            var transparentGlow = glow;
            transparentGlow.a = 0f;
            var transparentLight = light;
            transparentLight.a = 0f;
            _mesh.Begin();
            Ribbon(gap - 0.022f * HALO_WIDTH_SCALE, center, transparentGlow, glow);
            Ribbon(center, gap + width + 0.045f * HALO_WIDTH_SCALE, glow, transparentGlow);
            Ribbon(gap - 0.009f * HALO_WIDTH_SCALE, center, transparentLight, light);
            Ribbon(center, gap + width + 0.023f * HALO_WIDTH_SCALE, light, transparentLight);
            var core = _levelColor;
            core.a = 0.96f * opacity;
            Ribbon(gap, gap + width, core, core);
            _mesh.Commit();
        }

        private void Ribbon(float inner, float outer, Color innerColor, Color outerColor)
        {
            for (var path = 0; path < _paths.Length; path++)
            {
                var points = _paths[path];
                for (var i = 0; i < points.Length; i++)
                {
                    var next = (i + 1) % points.Length;
                    var a = points[i].At(inner);
                    var b = points[i].At(outer);
                    var c = points[next].At(outer);
                    var d = points[next].At(inner);
                    // 顶点透明度沿描边宽度渐变，使柔光自然消失在外侧。
                    _mesh.Triangle(a, b, c, innerColor, outerColor, outerColor);
                    _mesh.Triangle(a, c, d, innerColor, outerColor, innerColor);
                }
            }
        }

        internal void Clear() { IsPlaying = false; _elapsed = 0f; _mesh?.Clear(); }
        private void OnDisable() => Clear();
        private void OnDestroy() { _mesh?.Dispose(); ImpactFeedbackMesh.Destroy(_material); }
    }
}
