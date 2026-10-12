using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只保存对局和目标ID、发射数值快照；不依赖来源机器人的生命周期。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotBulletView : MonoBehaviour
    {
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private EnemyBattleController _enemies;
        private Vector3 _start;
        private float _flightTime;
        private float _arcHeight;
        public long BattleId { get; private set; }
        public BattleSide Side { get; private set; }
        public int TargetInstanceId { get; private set; }
        public int Damage { get; private set; }
        public long Sequence { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsResolved { get; private set; } = true;

        public void Initialize(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies,
            BattleSide side, int targetInstanceId, int damage, Vector3 start, float flightTime, float arcHeight, long sequence)
        {
            if (host == null || context == null || enemies == null) throw new ArgumentNullException(nameof(context));
            EntityValidation.RequireSide(side);
            EntityValidation.RequirePositive(targetInstanceId, nameof(targetInstanceId));
            EntityValidation.RequirePositive(damage, nameof(damage));
            EntityValidation.RequireFiniteNonNegative(flightTime, nameof(flightTime));
            EntityValidation.RequireFiniteNonNegative(arcHeight, nameof(arcHeight));
            if (flightTime <= 0f || !Finite(start.x) || !Finite(start.y) || !Finite(start.z))
                throw new ArgumentOutOfRangeException(nameof(flightTime));
            _host = host; _context = context; _enemies = enemies;
            BattleId = context.BattleId; Side = side; TargetInstanceId = targetInstanceId;
            Damage = damage; Sequence = sequence; _start = start; _flightTime = flightTime; _arcHeight = arcHeight;
            Elapsed = 0f; IsResolved = false; transform.position = start;
        }

        /// <returns>true 表示应回收；冻结期间不推进，也不补算暂停时间。</returns>
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsResolved) return true;
            if (_host == null || _context == null || _host.Context != _context || _context.BattleId != BattleId ||
                _context.IsEnded || _context.IsEndRequested || _enemies == null || !_enemies.OwnsContext(_context))
                return Resolve();
            if (!_host.IsCombatRunning) return false;
            if (!_enemies.TryGetTargetPosition(Side, TargetInstanceId, out var target)) return Resolve();
            Elapsed = Mathf.Min(_flightTime, Elapsed + deltaTime);
            var u = Elapsed / _flightTime;
            transform.position = Vector3.Lerp(_start, target, u) + Vector3.up * (4f * _arcHeight * u * (1f - u));
            if (Elapsed < _flightTime) return false;
            // 先封住重复结算，再扣血；击杀立即摘掉移动记录并交付不可变快照。
            Resolve();
            var result = _context.Registry.ApplyDamage(TargetInstanceId, Damage, out var removal);
            if (result == DamageResult.Killed) _enemies.NotifyKilled(removal);
            return true;
        }

        private bool Resolve() { IsResolved = true; return true; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
