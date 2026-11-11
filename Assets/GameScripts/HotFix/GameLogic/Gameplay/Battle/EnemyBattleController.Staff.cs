using System;
using System.Linq;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class EnemyBattleController
    {
        private StaffBattleController _staffs;
        public StaffBattleController Staffs => _staffs;
        internal void AttachStaffs(StaffBattleController staffs)
        {
            if (_staffs != null && !ReferenceEquals(_staffs, staffs)) _staffs.Dispose();
            _staffs = staffs;
        }
        internal bool TryPrepareStaff(BattleSide side, StaffConfig config, out BattleRouteGeometry route, out float progress)
        {
            route = null; progress = 0;
            if (!IsRunning || _centerRoutes[(int)side] == null) return false;
            route = new BattleRouteGeometry(_centerRoutes[(int)side]);
            var front = float.NegativeInfinity; var bestId = int.MaxValue; var radius = 0f;
            foreach (var pair in _active.OrderBy(p => p.Key))
            {
                var entry = pair.Value;
                if (entry.Group.Side != side || !TryGetTargetPosition(side, pair.Key, out _) ||
                    !_context.Registry.TryGet(pair.Key, out var entity) || !(entity is EnemyEntity enemy)) continue;
                if (entry.Geometry == null) entry.Geometry = new BattleRouteGeometry(entry.Points);
                var position = entry.Geometry.ConvertTo(route, enemy.RouteProgress);
                if (position < front || position == front && pair.Key >= bestId) continue;
                front = position; bestId = pair.Key; radius = enemy.Configuration?.HitRadius ?? 0.3f;
            }
            if (bestId == int.MaxValue) return false;
            progress = Mathf.Min(route.Length, front + radius + config.Thickness * 0.5f + config.SpawnGap);
            return true;
        }
        private bool MoveWithStaffs(MoveEntry entry, EnemyEntity enemy, float deltaTime)
        {
            if (entry.Geometry == null) entry.Geometry = new BattleRouteGeometry(entry.Points);
            var previous = enemy.RouteProgress;
            var candidate = Mathf.Min(entry.Geometry.Length, previous + (enemy.IsIsolated ? 0 : enemy.MoveSpeed * deltaTime));
            // 与NextArrivalDelay及旧路线移动保持相同的终点吸附容差；随后仍必须求Staff约束。
            if (!enemy.IsIsolated && entry.Geometry.Length - candidate <= 0.00001f) candidate = entry.Geometry.Length;
            var constrained = _staffs.Constrain(enemy.Side, entry.Geometry, previous, candidate, enemy.Configuration?.HitRadius ?? 0.3f);
            enemy.TrySetRouteProgress(constrained);
            entry.View.transform.position = entry.Geometry.Position(constrained);
            entry.NextPoint = constrained >= entry.Geometry.Length ? entry.Points.Length : entry.Geometry.Segment(constrained);
            entry.View.SetMoving(!enemy.IsIsolated && constrained > previous + 0.000001f);
            return entry.NextPoint == entry.Points.Length;
        }
        private bool IsStaffBlockingArrival(MoveEntry entry, EnemyEntity enemy)
        {
            if (_staffs == null || _staffs.Count == 0) return false;
            if (entry.Geometry == null) entry.Geometry = new BattleRouteGeometry(entry.Points);
            return _staffs.Constrain(enemy.Side, entry.Geometry, enemy.RouteProgress, entry.Geometry.Length,
                enemy.Configuration?.HitRadius ?? 0.3f) < entry.Geometry.Length;
        }
    }
}
