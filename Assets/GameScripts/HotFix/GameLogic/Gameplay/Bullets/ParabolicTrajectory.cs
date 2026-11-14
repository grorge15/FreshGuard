using UnityEngine;

namespace GameLogic
{
    /// <summary>连续过顶的偏心抛射轨道；下坠沿轨道受重力加速，命中时间仍由配置决定。</summary>
    internal sealed class ParabolicTrajectory
    {
        private const int SAMPLES = 64;
        private const float EPSILON = 0.000001f;
        private const float LANDING_SPEED_RATIO = 2.3f;
        private readonly Vector3 _origin;
        private readonly float _height;
        private readonly float _bendSign;
        private readonly float[] _riseDistances = new float[SAMPLES + 1];
        private readonly float[] _fallTimes = new float[SAMPLES + 1];
        private readonly float _riseLength;
        private readonly float _duration;
        internal float ApexProgress => _duration > EPSILON ? _riseLength / _duration : 0.5f;

        internal ParabolicTrajectory(Vector3 origin, Vector3 target, float height)
        {
            _origin = origin;
            _height = height;
            _bendSign = target.x < origin.x ? -1f : 1f;
            CreateCurves(target, out var rise, out var fall);
            var previous = rise.Start;
            for (var i = 1; i <= SAMPLES; i++)
            {
                var point = rise.Position(SampleParameter(i, true));
                _riseDistances[i] = _riseDistances[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }
            _riseLength = _riseDistances[SAMPLES];
            var drop = fall.Start.y - fall.End.y;
            previous = fall.Start;
            var previousSpeed = 1f;
            for (var i = 1; i <= SAMPLES; i++)
            {
                var point = fall.Position(SampleParameter(i, false));
                var descent = drop > EPSILON ? Mathf.Clamp01((fall.Start.y - point.y) / drop) : 0f;
                // v² = v_top² + 2*g*下降高度；将v_top归一化为1，落地速度为2.3倍。
                var speed = Mathf.Sqrt(1f + (LANDING_SPEED_RATIO * LANDING_SPEED_RATIO - 1f) * descent);
                _fallTimes[i] = _fallTimes[i - 1] +
                    2f * Vector3.Distance(previous, point) / (previousSpeed + speed);
                previous = point;
                previousSpeed = speed;
            }
            _duration = _riseLength + _fallTimes[SAMPLES];
        }

        internal Vector3 Evaluate(Vector3 target, float progress, out Vector3 direction)
        {
            CreateCurves(target, out var rise, out var fall);
            if (progress <= 0f) { direction = rise.Direction(0f); return _origin; }
            if (progress >= 1f) { direction = fall.Direction(1f); return target; }
            if (_duration <= EPSILON)
            {
                direction = (target - _origin).normalized;
                return Vector3.Lerp(_origin, target, progress);
            }
            // 时钟按发射快照固定；追踪只更新曲线终点，不重置飞行进度。
            var time = progress * _duration;
            var ascending = time <= _riseLength;
            var parameter = ascending ? FindParameter(_riseDistances, time, true) :
                FindParameter(_fallTimes, time - _riseLength, false);
            var curve = ascending ? rise : fall;
            direction = curve.Direction(parameter);
            return curve.Position(parameter);
        }

        private void CreateCurves(Vector3 target, out Curve rise, out Curve fall)
        {
            var span = target.x - _origin.x;
            // 竖直目标保留小幅侧弧，避免最高点发生原地折返或出现零切线。
            if (Mathf.Abs(span) < _height * 0.25f) span = _bendSign * _height * 0.25f;
            var peak = Vector3.LerpUnclamped(_origin, target, 0.83f);
            peak.x = _origin.x + span * 0.83f;
            peak.y = Mathf.Max(_origin.y, target.y) + _height;
            var first = Vector3.LerpUnclamped(_origin, target, 0.235294118f);
            first.x = _origin.x + span * 0.235294118f;
            first.y = Mathf.Lerp(_origin.y, peak.y, 0.719814241f);
            var second = Vector3.LerpUnclamped(_origin, target, 0.668235294f);
            second.x = _origin.x + span * 0.668235294f;
            second.y = peak.y;
            rise = new Curve(_origin, first, second, peak);
            first = Vector3.LerpUnclamped(_origin, target, 0.940294118f);
            first.x = _origin.x + span * 0.940294118f;
            first.y = peak.y;
            second = target;
            second.y = Mathf.Lerp(target.y, peak.y, 0.534055728f);
            fall = new Curve(peak, first, second, target);
        }

        // 在顶点两侧加密采样，短水平跨度也能保持连续速度。
        private static float SampleParameter(int index, bool ascending)
        {
            var u = (float)index / SAMPLES;
            return ascending ? u * (2f - u) : u * u;
        }

        private static float FindParameter(float[] values, float value, bool ascending)
        {
            var low = 0;
            var high = SAMPLES;
            while (high - low > 1)
            {
                var middle = (low + high) / 2;
                if (values[middle] < value) low = middle;
                else high = middle;
            }
            var length = values[high] - values[low];
            var fraction = length > EPSILON ? Mathf.Clamp01((value - values[low]) / length) : 0f;
            return Mathf.Lerp(SampleParameter(low, ascending), SampleParameter(high, ascending), fraction);
        }

        private readonly struct Curve
        {
            internal readonly Vector3 Start;
            private readonly Vector3 _first;
            private readonly Vector3 _second;
            internal readonly Vector3 End;
            internal Curve(Vector3 start, Vector3 first, Vector3 second, Vector3 end)
            { Start = start; _first = first; _second = second; End = end; }
            internal Vector3 Position(float u)
            {
                var v = 1f - u;
                return v * v * v * Start + 3f * v * v * u * _first +
                    3f * v * u * u * _second + u * u * u * End;
            }
            internal Vector3 Direction(float u)
            {
                var v = 1f - u;
                return (3f * v * v * (_first - Start) + 6f * v * u * (_second - _first) +
                    3f * u * u * (End - _second)).normalized;
            }
        }
    }
}
