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
        private ICombatResolutionPort _resolution;
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
        public CombatActionSnapshot Action { get; private set; }

        public void Initialize(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies,
            CombatActionSnapshot action, ICombatResolutionPort resolution, float flightTime, float arcHeight)
        {
            if (host == null || context == null || enemies == null) throw new ArgumentNullException(nameof(context));
            if (action == null || resolution == null) throw new ArgumentNullException(nameof(action));
            if (action.BattleId != context.BattleId || action.TargetInstanceIds.Count != 1)
                throw new ArgumentException("子弹必须持有本局单目标攻击快照。", nameof(action));
            EntityValidation.RequireFiniteNonNegative(flightTime, nameof(flightTime));
            EntityValidation.RequireFiniteNonNegative(arcHeight, nameof(arcHeight));
            if (flightTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(flightTime));
            _host = host; _context = context; _enemies = enemies;
            Action = action; _resolution = resolution;
            BattleId = action.BattleId; Side = action.SourceSide; TargetInstanceId = action.TargetInstanceIds[0];
            Damage = action.LockedDamage; Sequence = action.Sequence; _start = action.Origin;
            _flightTime = flightTime; _arcHeight = arcHeight;
            Elapsed = 0f; IsResolved = false; transform.position = _start;
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
            _context.Combat.Resolve(Action, _resolution);
            return true;
        }

        private bool Resolve() { IsResolved = true; return true; }
    }
}
