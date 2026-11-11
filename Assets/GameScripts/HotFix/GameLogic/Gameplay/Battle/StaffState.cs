using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只读等级快照。Staff是道路障碍，不创建攻击或伤害快照。</summary>
    public sealed class StaffConfig
    {
        public string Prefab { get; }
        public float SpawnGap { get; }
        public float RollSpeed { get; }
        public float RollDistance { get; }
        public float Width { get; }
        public float Thickness { get; }
        public float GrowTime { get; }
        public float SettleTime { get; }
        public StaffConfig(string prefab, float gap, float speed, float distance, float width, float thickness,
            float growTime, float settleTime)
        {
            if (string.IsNullOrWhiteSpace(prefab)) throw new ArgumentException(nameof(prefab));
            foreach (var v in new[] { gap, speed, distance, width, thickness, growTime, settleTime })
                EntityValidation.RequireFiniteNonNegative(v, nameof(v));
            if (speed <= 0 || distance <= 0 || width <= 0 || thickness <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
            Prefab = prefab; SpawnGap = gap; RollSpeed = speed; RollDistance = distance;
            Width = width; Thickness = thickness; GrowTime = growTime; SettleTime = settleTime;
        }
    }

    /// <summary>中心线及其弧长；个体路线用同一路段的比例映射，不使用最近点投影。</summary>
    public sealed class BattleRouteGeometry
    {
        private readonly Vector3[] _points;
        private readonly float[] _lengths;
        public float Length => _lengths[_lengths.Length - 1];
        public BattleRouteGeometry(Vector3[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException(nameof(points));
            _points = (Vector3[])points.Clone(); _lengths = new float[points.Length];
            for (var i = 1; i < points.Length; i++) _lengths[i] = _lengths[i - 1] + Vector3.Distance(points[i - 1], points[i]);
        }
        public int Segment(float progress)
        {
            var i = 1;
            while (i < _points.Length - 1 && _lengths[i] <= progress) i++;
            return i;
        }
        public Vector3 Position(float progress)
        {
            var i = Segment(progress); var length = _lengths[i] - _lengths[i - 1];
            return Vector3.Lerp(_points[i - 1], _points[i], length <= 0 ? 0 : (progress - _lengths[i - 1]) / length);
        }
        public Vector3 Tangent(float progress)
        {
            var i = Segment(progress);
            for (; i > 0; i--) if ((_points[i] - _points[i - 1]).sqrMagnitude > 0.00000001f) return (_points[i] - _points[i - 1]).normalized;
            return Vector3.right;
        }
        public float ConvertTo(BattleRouteGeometry other, float progress)
        {
            if (_points.Length != other._points.Length) throw new InvalidOperationException("路线必须具有对应节点。");
            var i = Segment(Mathf.Clamp(progress, 0, Length));
            var length = _lengths[i] - _lengths[i - 1];
            var t = length <= 0 ? 0 : Mathf.Clamp01((progress - _lengths[i - 1]) / length);
            return Mathf.Lerp(other._lengths[i - 1], other._lengths[i], t);
        }
        public float LateralOffset(BattleRouteGeometry individual, float centerProgress)
        {
            var delta = individual.Position(ConvertTo(individual, centerProgress)) - Position(centerProgress);
            var tangent = Tangent(centerProgress);
            return Vector3.Dot(delta, new Vector3(-tangent.y, tangent.x, 0));
        }
        public float PreviousCorner(float progress)
        {
            for (var i = _lengths.Length - 2; i > 0; i--) if (_lengths[i] < progress - 0.000001f) return _lengths[i];
            return 0;
        }
        public float NextCorner(float progress)
        {
            foreach (var s in _lengths) if (s > progress + 0.000001f) return s;
            return Length;
        }
    }

    public enum StaffPhase { Appearing, Rolling, Finished }

    public sealed class StaffState
    {
        private readonly float _start;
        private double _elapsed;
        public long BattleId { get; }
        public int InstanceId { get; }
        public SkillOwnerRef Source { get; }
        public BattleSide Side => Source.Side;
        public StaffConfig Config { get; }
        public float Progress { get; private set; }
        public float PreviousProgress { get; private set; }
        public float Elapsed => (float)_elapsed;
        public float RolledDistance => _start - Progress;
        public float RollingElapsed => Mathf.Max(0, Elapsed - Config.GrowTime - Config.SettleTime);
        public StaffPhase Phase { get; private set; }
        public float AppearScale => Elapsed < Config.GrowTime && Config.GrowTime > 0
            ? 1.1f * Mathf.SmoothStep(0, 1, Elapsed / Config.GrowTime)
            : Elapsed < Config.GrowTime + Config.SettleTime && Config.SettleTime > 0
                ? Mathf.Lerp(1.1f, 1, Mathf.SmoothStep(0, 1, (Elapsed - Config.GrowTime) / Config.SettleTime)) : 1;
        public StaffState(long battleId, int instanceId, SkillOwnerRef source, StaffConfig config, float progress)
        {
            EntityValidation.RequireFiniteNonNegative(progress, nameof(progress));
            BattleId = battleId; InstanceId = instanceId; Source = source; Config = config ?? throw new ArgumentNullException(nameof(config));
            _start = Progress = PreviousProgress = progress;
            Phase = config.GrowTime + config.SettleTime > 0 ? StaffPhase.Appearing : StaffPhase.Rolling;
        }
        public float TimeUntilBoundary(BattleRouteGeometry route)
        {
            var appear = (double)Config.GrowTime + Config.SettleTime;
            if (_elapsed < appear) return (float)(appear - _elapsed);
            return Mathf.Max(0, Mathf.Min(Mathf.Min(Config.RollDistance, _start) - RolledDistance,
                Progress - route.PreviousCorner(Progress)) / Config.RollSpeed);
        }
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            PreviousProgress = Progress;
            if (Phase == StaffPhase.Finished) return;
            _elapsed += deltaTime;
            var rollTime = Math.Max(0, _elapsed - (double)Config.GrowTime - Config.SettleTime);
            var travelled = Math.Min(Math.Min(Config.RollDistance, _start), rollTime * Config.RollSpeed);
            Progress = Mathf.Max(0, _start - (float)travelled);
            Phase = rollTime <= 0 && _elapsed < (double)Config.GrowTime + Config.SettleTime ? StaffPhase.Appearing : StaffPhase.Rolling;
            if (travelled >= Math.Min(Config.RollDistance, _start) - 0.0000001d && Phase == StaffPhase.Rolling) Phase = StaffPhase.Finished;
        }

        /// <summary>对整段相对运动做区间相交，防止大步长越过障碍。输入输出均为中心线坐标。</summary>
        public float Constrain(float start, float candidate, float lateralStart, float lateralEnd, float radius)
        {
            var clearance = radius + Config.Thickness * 0.5f;
            var halfWidth = Config.Width * 0.5f + radius;
            var enter = 0f; var exit = 1f;
            if (!Slab(lateralStart, lateralEnd, -halfWidth, halfWidth, ref enter, ref exit)) return candidate;
            var widthExit = exit;
            if (!Slab(start - PreviousProgress, candidate - Progress, -clearance, clearance, ref enter, ref exit)) return candidate;
            // 已经从终点侧离开的敌人不向后吸附；初始重叠仍受约束。
            if (start > PreviousProgress + clearance + 0.000001f) return candidate;
            var cap = Mathf.Lerp(PreviousProgress, Progress, widthExit) - clearance;
            var releasedMovement = (candidate - start) * (1 - widthExit);
            return Mathf.Clamp(Mathf.Min(candidate, cap + releasedMovement), 0, float.MaxValue);
        }
        private static bool Slab(float from, float to, float min, float max, ref float enter, ref float exit)
        {
            var delta = to - from;
            if (Mathf.Abs(delta) < 0.0000001f) return from >= min && from <= max;
            var a = (min - from) / delta; var b = (max - from) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b)); exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit && enter <= 1 && exit >= 0;
        }
    }
}
