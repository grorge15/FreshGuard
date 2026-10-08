using System;
using UnityEngine;

namespace GameLogic
{
    public static class BallBounceMath
    {
        public static Vector2 Resolve(Vector2 incoming, Vector2 normal, float offset, float threshold, bool positiveSide)
        {
            if (threshold < 0f || threshold > 45f || float.IsNaN(threshold))
                throw new ArgumentOutOfRangeException(nameof(threshold), "防轴向阈值必须在 0 到 45 度之间。");
            normal.Normalize();
            Vector2 reflected = Vector2.Reflect(incoming.normalized, normal);
            float angle = Mathf.Atan2(reflected.y, reflected.x) * Mathf.Rad2Deg + offset;
            Vector2 candidate = FromAngle(ClampAxis(angle, threshold, positiveSide));
            if (Vector2.Dot(candidate, normal) < 0f) candidate = Vector2.Reflect(candidate, normal);
            // 镜像斜墙可能再次靠轴；切线方向的镜像也没有离墙分量。
            // 枚举约束区间边界，选取距候选角最近的合法方向，同时满足两个条件。
            if (IsValid(candidate, normal, threshold)) return candidate.normalized;
            float target = Mathf.Atan2(candidate.y, candidate.x) * Mathf.Rad2Deg;
            float normalAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            float bestDistance = float.MaxValue;
            Vector2 best = Vector2.zero;
            for (int axis = 0; axis < 4; axis++)
            {
                Consider(axis * 90f + threshold, target, normal, threshold, ref bestDistance, ref best);
                Consider(axis * 90f - threshold, target, normal, threshold, ref bestDistance, ref best);
            }
            Consider(normalAngle + 89.9f, target, normal, threshold, ref bestDistance, ref best);
            Consider(normalAngle - 89.9f, target, normal, threshold, ref bestDistance, ref best);
            if (best.sqrMagnitude == 0f) throw new InvalidOperationException("无法得到合法离墙方向。");
            return best;
        }

        public static float ClampAxis(float angle, float threshold, bool positiveSide)
        {
            angle = Mathf.Repeat(angle, 360f);
            float axis = Mathf.Round(angle / 90f) * 90f;
            float delta = Mathf.DeltaAngle(axis, angle);
            if (Mathf.Abs(delta) >= threshold) return angle;
            float sign = Mathf.Abs(delta) < 0.00001f ? (positiveSide ? 1f : -1f) : Mathf.Sign(delta);
            return Mathf.Repeat(axis + sign * threshold, 360f);
        }

        public static float AxisDistance(Vector2 direction)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 90f);
            return Mathf.Min(angle, 90f - angle);
        }

        private static bool IsValid(Vector2 direction, Vector2 normal, float threshold)
        {
            return Vector2.Dot(direction, normal) >= 0.001f && AxisDistance(direction) >= threshold - 0.0001f;
        }

        private static void Consider(float angle, float target, Vector2 normal, float threshold, ref float distance, ref Vector2 best)
        {
            Vector2 candidate = FromAngle(angle);
            float delta = Mathf.Abs(Mathf.DeltaAngle(target, angle));
            if (delta < distance && IsValid(candidate, normal, threshold)) { distance = delta; best = candidate; }
        }

        private static Vector2 FromAngle(float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}
