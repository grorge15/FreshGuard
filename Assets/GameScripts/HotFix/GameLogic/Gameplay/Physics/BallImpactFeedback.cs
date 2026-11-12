using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>反弹加速短尾、普通撞击与暴走星芒；暂停时保留当前画面。</summary>
    [DisallowMultipleComponent]
    public sealed class BallImpactFeedback : MonoBehaviour
    {
        internal const int MaximumBursts = 16;
        internal const float GlassDuration = 0.20f;
        internal const float GlassHighlightDuration = 0.065f;
        internal const float BerserkDuration = 0.28f;
        private const float FirstDropletDelay = 0.008f;
        private const int DropletCurveSamples = 8;
        private const float WallDuration = 0.24f;
        private const float TrailLifetime = 0.16f;
        private const float TrailFadeProgress = 0.6f;
        private static readonly Color TrailGold = new Color32(255, 196, 56, 255);
        private static readonly Color TrailAmber = new Color32(238, 151, 20, 255);
        private static readonly Color Gold = new Color32(242, 182, 57, 255);
        private static readonly Color Amber = new Color32(211, 147, 34, 255);
        private static readonly Color Spark = new Color32(255, 203, 77, 255);
        private static readonly Color SparkEdge = new Color32(178, 106, 24, 255);
        private static readonly Color SparkHot = new Color32(255, 239, 179, 255);
        private static readonly Color DropletGold = new Color32(244, 187, 64, 255);
        private static readonly Color DropletAmber = new Color32(193, 133, 36, 255);
        private static readonly Color DropletHighlight = new Color32(255, 232, 162, 255);
        private static readonly Color BerserkGold = new Color32(255, 190, 34, 255);
        private static readonly Color BerserkHot = new Color32(255, 248, 199, 255);
        private static readonly Color BerserkOrange = new Color32(255, 127, 33, 255);
        private static readonly Color BerserkPink = new Color32(255, 73, 149, 255);
        private static readonly Color[] BerserkColors = { BerserkGold, BerserkOrange, BerserkPink,
            new Color32(165, 107, 255, 255), new Color32(99, 239, 246, 255) };
        private static readonly float[] BerserkRayLengths = { 57, 44, 59, 42, 53, 45, 55, 39 };
        private static readonly float[] WallAngles = { 164, 128, 53, 17 };
        private static readonly float[] WallLengths = { 13, 10, 14, 11 };
        private static readonly float[] WallDotAngles = { 147, 92, 25 };
        private readonly List<Burst> _bursts = new List<Burst>(MaximumBursts);
        private readonly List<TrailPoint> _points = new List<TrailPoint>(96);
        private PhysicalCollisionBall _ball;
        private CircleCollider2D _collider;
        private Material _material;
        private ImpactFeedbackMesh _trail;
        private ImpactFeedbackMesh _impacts;
        private bool _moving;
        private bool _berserk;
        private float _clock;
        private uint _glassSequence;

        private struct Burst
        {
            public Vector2 Point, Normal;
            public float Started, Unit;
            public bool Glass, Berserk;
            public Vector2 DropletScales, DropletAngles;
            public float SecondDropletDelay;
        }

        private struct TrailPoint { public Vector2 Position; public float Time; }
        internal int ActiveBurstCount => _bursts.Count;
        internal int TrailPointCount => _points.Count;
        internal bool IsReady => _trail != null;

        internal bool Initialize(PhysicalCollisionBall ball, SpriteRenderer source)
        {
            if (IsReady) return true;
            if (ball == null || source == null || source.sprite == null) return false;
            _material = ImpactFeedbackMesh.CreateMaterial();
            if (_material == null) return false;
            _ball = ball;
            _collider = ball.GetComponent<CircleCollider2D>();
            // Temporary balls clone an already-running ball, including its visual children.
            // Retire only the cloned nodes; their shared runtime meshes belong to the source.
            foreach (var name in new[] { "GoldenBallTrail", "GoldenBallImpacts" })
            {
                var previous = transform.Find(name);
                if (previous == null) continue;
                previous.gameObject.SetActive(false);
                previous.name = "Retired" + name;
                ImpactFeedbackMesh.Destroy(previous.gameObject);
            }
            _trail = new ImpactFeedbackMesh(transform, "GoldenBallTrail", _material, source.sortingLayerID, source.sortingOrder - 1);
            _impacts = new ImpactFeedbackMesh(transform, "GoldenBallImpacts", _material, source.sortingLayerID, source.sortingOrder + 20);
            RefreshRenderBinding();
            return true;
        }

        private void RefreshRenderBinding()
        {
            var group = GetComponent<BattleSideRenderGroup>();
            if (group != null) BattleSideRenderGroup.BindRoot(gameObject, group.Registry, group.Side);
        }

        internal void SetMotion(bool moving, bool berserk)
        {
            _moving = moving;
            if (_berserk != berserk)
            {
                _points.Clear();
                _trail?.Clear();
            }
            _berserk = berserk;
        }

        internal void RecordCorner(Vector2 center)
        {
            if (IsReady && _moving && !_berserk) AppendPoint(center);
        }

        internal void PlayGlass(Vector2 point, Vector2 normal) => Play(point, normal, true);
        internal void PlayWall(Vector2 point, Vector2 normal) => Play(point, normal, false);
        internal void PlayRobot(Vector2 point, Vector2 normal, bool enteringBerserk = false)
        {
            if (_ball != null && _ball.IsBerserk) Play(point, normal, false, enteringBerserk);
        }

        private void Play(Vector2 point, Vector2 normal, bool glass, bool enteringBerserk = false)
        {
            if (!IsReady || !_moving || !_ball.IsLaunched) return;
            if (_bursts.Count == MaximumBursts) _bursts.RemoveAt(0);
            // 50 Combo触发瞬间保留原尺寸；后续机器人、墙体及玻璃碰撞缩小一半。
            var scale = _ball.IsBerserk && !enteringBerserk ? 0.5f : 1f;
            var burst = new Burst { Point = point, Normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.up,
                Started = _clock, Unit = _ball.WorldRadius / 14f * scale, Glass = glass, Berserk = _ball.IsBerserk };
            if (glass && !burst.Berserk)
            {
                // Cosmetic variations must not consume Unity's shared gameplay random stream.
                var seed = unchecked((uint)GetInstanceID() * 747796405u + ++_glassSequence * 2891336453u);
                burst.DropletScales = new Vector2(Mathf.Lerp(0.9f, 1.1f, Variation(ref seed)),
                    Mathf.Lerp(0.9f, 1.1f, Variation(ref seed)));
                burst.DropletAngles = new Vector2(158f + Mathf.Lerp(-8f, 8f, Variation(ref seed)),
                    24f + Mathf.Lerp(-8f, 8f, Variation(ref seed)));
                burst.SecondDropletDelay = FirstDropletDelay + Mathf.Lerp(0.01f, 0.02f, Variation(ref seed));
            }
            _bursts.Add(burst);
            DrawImpacts();
        }

        private static float Variation(ref uint seed)
        {
            if (seed == 0u) seed = 0xA341316Cu;
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0x00FFFFFFu) / 16777215f;
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        internal void Advance(float deltaTime)
        {
            if (!IsReady || !_moving) return;
            _clock += Mathf.Max(0f, deltaTime);
            for (var i = _bursts.Count - 1; i >= 0; i--)
                if (_clock - _bursts[i].Started >= (_bursts[i].Berserk ? BerserkDuration :
                    _bursts[i].Glass ? GlassDuration : WallDuration)) _bursts.RemoveAt(i);
            var acceleration = _ball.BounceAccelerationProgress;
            if (!_berserk && acceleration > 0f)
            {
                AppendPoint(_collider.bounds.center);
                TrimTrail();
                // 前40%的加速时段保持清晰，剩余时段跟随加速衰减整体淡出。
                DrawTrail(Mathf.Clamp01(acceleration / TrailFadeProgress));
            }
            else { _points.Clear(); _trail.Clear(); }
            DrawImpacts();
        }

        private void AppendPoint(Vector2 point)
        {
            if (_points.Count > 0 && (_points[_points.Count - 1].Position - point).sqrMagnitude < 0.0000001f) return;
            if (_points.Count == 96) _points.RemoveAt(0);
            _points.Add(new TrailPoint { Position = point, Time = _clock });
        }

        private void TrimTrail()
        {
            while (_points.Count > 1 && _clock - _points[0].Time > TrailLifetime) _points.RemoveAt(0);
            var distance = 0f;
            var maximum = _ball.WorldRadius * 3.5f;
            for (var i = _points.Count - 2; i >= 0; i--)
            {
                var segment = Vector2.Distance(_points[i + 1].Position, _points[i].Position);
                if (distance + segment > maximum)
                {
                    var clipped = _points[i];
                    clipped.Position = Vector2.Lerp(_points[i + 1].Position, clipped.Position, (maximum - distance) / segment);
                    _points[i] = clipped;
                    if (i > 0) _points.RemoveRange(0, i);
                    break;
                }
                distance += segment;
            }
        }

        private Vector3 Local(Vector2 point) => transform.InverseTransformPoint(new Vector3(point.x, point.y, transform.position.z));

        private void DrawTrail(float visibility)
        {
            _trail.Begin();
            var total = 0f;
            for (var i = 1; i < _points.Count; i++) total += Vector2.Distance(_points[i - 1].Position, _points[i].Position);
            var distance = 0f;
            for (var i = 0; i < _points.Count - 1; i++)
            {
                var a = _points[i].Position;
                var b = _points[i + 1].Position;
                var start = total > 0f ? distance / total : 0f;
                distance += Vector2.Distance(a, b);
                var end = total > 0f ? distance / total : 1f;
                var sa = TrailSide(i) * (_ball.WorldRadius * 0.6f * Mathf.Sqrt(start));
                var sb = TrailSide(i + 1) * (_ball.WorldRadius * 0.6f * Mathf.Sqrt(end));
                var ca = Color.Lerp(TrailGold, TrailAmber, start); ca.a = 0.95f * visibility * Mathf.Pow(start, 0.35f);
                var cb = Color.Lerp(TrailGold, TrailAmber, end); cb.a = 0.95f * visibility * Mathf.Pow(end, 0.35f);
                _trail.Quad(Local(a - sa), Local(a + sa), Local(b + sb), Local(b - sb), ca, cb);
            }
            _trail.Commit();
        }

        private Vector2 TrailSide(int index)
        {
            var before = (_points[Mathf.Max(0, index - 1)].Position - _points[index].Position).normalized;
            var after = (_points[index].Position - _points[Mathf.Min(_points.Count - 1, index + 1)].Position).normalized;
            if (before == Vector2.zero) before = after;
            if (after == Vector2.zero) after = before;
            var na = new Vector2(-before.y, before.x);
            var nb = new Vector2(-after.y, after.x);
            var miter = (na + nb).normalized;
            return miter / Mathf.Max(0.5f, Vector2.Dot(miter, nb));
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }
        private static float Ease(float value) => Mathf.SmoothStep(0f, 1f, value);
        private static Vector2 Direction(Burst burst, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var tangent = new Vector2(burst.Normal.y, -burst.Normal.x);
            return tangent * Mathf.Cos(radians) + burst.Normal * Mathf.Sin(radians);
        }

        private void Stroke(Vector2 a, Vector2 b, float width, Color color) =>
            _impacts.Line(Local(a), Local(b), width / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x)), color);

        private void Dot(Vector2 point, float radius, Color color, bool soft = false) =>
            _impacts.Disc(Local(point), radius / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.x)), color, soft);

        private void DrawImpacts()
        {
            _impacts.Begin();
            foreach (var burst in _bursts) DrawBurst(burst, _clock - burst.Started);
            _impacts.Commit();
        }

        private void DrawBurst(Burst burst, float age)
        {
            if (burst.Berserk) { DrawBerserkBurst(burst, age); return; }
            if (burst.Glass) { DrawGlassBurst(burst, age); return; }
            var duration = WallDuration;
            const float hold = 0.065f;
            var fade = 1f - Ease((age - hold) / (duration - hold));
            var u = burst.Unit;
            var angles = WallAngles;
            var lengths = WallLengths;
            for (var i = 0; i < angles.Length; i++)
            {
                var direction = Direction(burst, angles[i]);
                var start = 3f + age * 74f;
                var end = start + lengths[i] * (1f - age / duration * 0.55f);
                var a = burst.Point + direction * (start * u);
                var b = burst.Point + direction * (Mathf.Min(end, 35f) * u);
                Stroke(a, b, 2.8f * u, WithAlpha(SparkEdge, 0.8f * fade));
                Stroke(a, b, 2f * u, WithAlpha(Spark, fade));
                if (i % 2 == 0) Stroke(a, Vector2.Lerp(a, b, 0.28f), 0.75f * u, WithAlpha(SparkHot, 0.94f * fade));
            }
            var dotAngles = WallDotAngles;
            for (var i = 0; i < dotAngles.Length; i++)
            {
                var offset = Direction(burst, dotAngles[i]) * (7f + age * (94f + i * 7f));
                offset = Vector2.ClampMagnitude(offset, 35f);
                var point = burst.Point + offset * u;
                Dot(point, 1.8f * u, WithAlpha(SparkEdge, 0.65f * fade));
                Dot(point, 1.35f * u, WithAlpha(Spark, fade));
            }
            if (age < 0.12f)
            {
                var core = 1f - Ease((age - 0.045f) / 0.075f);
                var tangent = new Vector2(burst.Normal.y, -burst.Normal.x);
                Stroke(burst.Point - tangent * (7f * u), burst.Point + tangent * (7f * u), 2f * u, WithAlpha(Spark, core));
                Dot(burst.Point, 2.8f * u, WithAlpha(Spark, core));
                Dot(burst.Point, 1.4f * u, WithAlpha(SparkHot, core));
            }
            if (age < 0.085f) Dot(burst.Point, 14f * u, WithAlpha(Gold, 0.52f * (1f - age / 0.085f)), true);
        }

        private static float EaseOut(float progress) => 1f - Mathf.Pow(1f - Mathf.Clamp01(progress), 3f);

        private void DrawBerserkBurst(Burst burst, float age)
        {
            var progress = Mathf.Clamp01(age / BerserkDuration);
            var expansion = EaseOut(progress);
            var opacity = Mathf.Pow(1f - progress, 1.2f);
            var growth = EaseOut(age / 0.065f);
            // 与示意一致：以18像素球半径为尺度，星芒最大约为球直径的3倍。
            var unit = burst.Unit * (14f / 18f);
            Dot(burst.Point, (29f + expansion * 18f) * unit, WithAlpha(BerserkOrange, opacity * 0.38f), true);
            DrawBerserkRing(burst.Point, (9f + expansion * 37f) * unit,
                (3f * (1f - progress) + 0.8f) * unit, WithAlpha(BerserkPink, opacity * 0.65f));
            for (var i = 0; i < BerserkRayLengths.Length; i++)
            {
                var angle = i * Mathf.PI / 4f + 0.12f;
                DrawBerserkShard(burst.Point, angle, (6f + expansion * 15f) * unit,
                    BerserkRayLengths[i] * growth * (1f - progress * 0.35f) * unit,
                    (i % 2 == 0 ? 8f : 5f) * unit, WithAlpha(i % 2 == 0 ? BerserkGold : BerserkPink, opacity));
                DrawBerserkShard(burst.Point, angle, (4f + expansion * 8f) * unit,
                    BerserkRayLengths[i] * 0.57f * growth * unit, 2.6f * unit, WithAlpha(BerserkHot, opacity));
            }
            for (var i = 0; i < 9; i++)
            {
                var angle = i * 2.399f + 0.7f;
                DrawBerserkShard(burst.Point, angle, (16f + expansion * (37f + i % 3 * 7f)) * unit,
                    (8f * (1f - progress) + 3f) * unit, 2.5f * unit,
                    WithAlpha(BerserkColors[i % BerserkColors.Length], opacity * 0.9f));
            }
            DrawBerserkCore(burst.Point, 21f * growth * (1f - progress * 0.65f) * unit, WithAlpha(BerserkHot, opacity));
        }

        private void DrawBerserkRing(Vector2 center, float radius, float width, Color color)
        {
            const int segments = 40;
            var inner = Mathf.Max(0f, radius - width * 0.5f);
            var outer = radius + width * 0.5f;
            for (var i = 0; i < segments; i++)
            {
                var a = RadialDirection(i * Mathf.PI * 2f / segments);
                var b = RadialDirection((i + 1) * Mathf.PI * 2f / segments);
                _impacts.Quad(Local(center + a * inner), Local(center + a * outer),
                    Local(center + b * outer), Local(center + b * inner), color, color);
            }
        }

        private void DrawBerserkShard(Vector2 point, float angle, float distance, float length, float width, Color color)
        {
            const int samples = 8;
            var direction = RadialDirection(angle);
            var side = new Vector2(-direction.y, direction.x);
            var center = point + direction * distance;
            var start = new Vector2(-length * 0.52f, 0f);
            var end = new Vector2(length * 0.55f, 0f);
            for (var half = -1; half <= 1; half += 2)
            {
                var control = new Vector2(-length * 0.18f, width * half);
                var previous = start;
                for (var i = 1; i <= samples; i++)
                {
                    var next = Quadratic(start, control, end, (float)i / samples);
                    _impacts.Triangle(Local(center), Local(center + direction * previous.x + side * previous.y),
                        Local(center + direction * next.x + side * next.y), color);
                    previous = next;
                }
            }
        }

        private void DrawBerserkCore(Vector2 point, float radius, Color color)
        {
            const int samples = 6;
            for (var quarter = 0; quarter < 4; quarter++)
            {
                var start = RadialDirection(quarter * Mathf.PI * 0.5f) * radius;
                var end = RadialDirection((quarter + 1) * Mathf.PI * 0.5f) * radius;
                var control = (start + end) * 0.12f;
                var previous = start;
                for (var i = 1; i <= samples; i++)
                {
                    var next = Quadratic(start, control, end, (float)i / samples);
                    _impacts.Triangle(Local(point), Local(point + previous), Local(point + next), color);
                    previous = next;
                }
            }
        }

        private static Vector2 RadialDirection(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        private static Vector2 Quadratic(Vector2 a, Vector2 b, Vector2 c, float t)
        {
            var remaining = 1f - t;
            return remaining * remaining * a + 2f * remaining * t * b + t * t * c;
        }

        private void DrawGlassBurst(Burst burst, float age)
        {
            var u = burst.Unit;
            DrawGlassDroplet(burst, age, FirstDropletDelay, burst.DropletAngles.x, burst.DropletScales.x, 11.5f, 5.6f, 87f);
            DrawGlassDroplet(burst, age, burst.SecondDropletDelay, burst.DropletAngles.y, burst.DropletScales.y, 8.8f, 4.4f, 76f);
            if (age >= GlassHighlightDuration) return;
            var pulse = 1f - Ease((age - 0.012f) / (GlassHighlightDuration - 0.012f));
            Dot(burst.Point, 6.4f * u, WithAlpha(DropletGold, 0.42f * pulse), true);
            Dot(burst.Point, 1.7f * u, WithAlpha(DropletGold, 0.96f * pulse));
            Dot(burst.Point + burst.Normal * (0.2f * u), 0.72f * u, WithAlpha(DropletHighlight, 0.96f * pulse));
        }

        private void DrawGlassDroplet(Burst burst, float age, float delay, float angle, float size, float length, float width, float speed)
        {
            var elapsed = age - delay;
            if (elapsed < 0f) return;
            var lifetime = GlassDuration - delay;
            var opacity = 1f - Ease((elapsed - 0.035f) / (lifetime - 0.035f));
            var scale = size * (0.65f + 0.35f * Ease(elapsed / 0.018f)) *
                (1f - 0.18f * Ease((elapsed - 0.09f) / 0.11f));
            var direction = Direction(burst, angle);
            var tip = burst.Point + (direction * (3f + speed * elapsed) - burst.Normal * (20f * elapsed * elapsed)) * burst.Unit;
            var side = new Vector2(-direction.y, direction.x);
            length *= scale * burst.Unit;
            width *= scale * burst.Unit;
            var tangent = new Vector2(burst.Normal.y, -burst.Normal.x);
            DropletShape(tip + (tangent * 0.18f - burst.Normal * 0.28f) * burst.Unit,
                direction, side, length, width, WithAlpha(DropletAmber, 0.76f * opacity));
            DropletShape(tip, direction, side, length, width, WithAlpha(DropletGold, opacity));
            var previous = DropletHighlightPoint(0f, length, width);
            for (var i = 1; i <= DropletCurveSamples; i++)
            {
                var next = DropletHighlightPoint((float)i / DropletCurveSamples, length, width);
                Stroke(tip + direction * previous.x + side * previous.y, tip + direction * next.x + side * next.y,
                    0.65f * burst.Unit, WithAlpha(DropletHighlight, 0.92f * opacity));
                previous = next;
            }
        }

        private void DropletShape(Vector2 tip, Vector2 direction, Vector2 side, float length, float width, Color color)
        {
            var center = Local(tip + direction * (length * 0.64f));
            const int segments = DropletCurveSamples * 3;
            for (var i = 0; i < segments; i++)
            {
                var a = DropletOutlinePoint(i, length, width);
                var b = DropletOutlinePoint((i + 1) % segments, length, width);
                _impacts.Triangle(center, Local(tip + direction * a.x + side * a.y),
                    Local(tip + direction * b.x + side * b.y), color);
            }
        }

        private static Vector2 DropletOutlinePoint(int index, float length, float width)
        {
            var part = index / DropletCurveSamples;
            var t = (float)(index % DropletCurveSamples) / DropletCurveSamples;
            var upper = new Vector2(length * 0.70f, -width * 0.50f);
            var lower = new Vector2(length * 0.70f, width * 0.50f);
            if (part == 0) return Bezier(Vector2.zero, new Vector2(length * 0.28f, -width * 0.07f),
                new Vector2(length * 0.40f, -width * 0.48f), upper, t);
            if (part == 1) return Bezier(upper, new Vector2(length * 1.12f, -width * 0.48f),
                new Vector2(length * 1.12f, width * 0.48f), lower, t);
            return Bezier(lower, new Vector2(length * 0.40f, width * 0.48f),
                new Vector2(length * 0.28f, width * 0.07f), Vector2.zero, t);
        }

        private static Vector2 DropletHighlightPoint(float t, float length, float width) => Bezier(
            new Vector2(length * 0.47f, -width * 0.18f), new Vector2(length * 0.65f, -width * 0.32f),
            new Vector2(length * 0.84f, -width * 0.28f), new Vector2(length * 0.89f, -width * 0.03f), t);

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            var u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        internal void Clear()
        {
            _moving = false;
            _bursts.Clear(); _points.Clear(); _clock = 0f;
            _trail?.Clear(); _impacts?.Clear();
        }

        private void OnDisable() { _moving = false; Clear(); }
        private void OnDestroy()
        {
            _trail?.Dispose(); _impacts?.Dispose();
            ImpactFeedbackMesh.Destroy(_material);
        }
    }
}
