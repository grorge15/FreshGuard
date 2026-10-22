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
        private SkillRuntime _skills;
        private SkillRuntimeContext _skillContext;
        private SkillCastSnapshot _cast;
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
            _skills = null; _skillContext = null; _cast = null;
            BattleId = action.BattleId; Side = action.SourceSide; TargetInstanceId = action.TargetInstanceIds[0];
            Damage = action.LockedDamage; Sequence = action.Sequence; _start = action.Origin;
            _flightTime = flightTime; _arcHeight = arcHeight;
            Elapsed = 0f; IsResolved = false; transform.position = _start;
        }

        public void InitializeSkillCast(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies,
            SkillRuntime skills, SkillRuntimeContext skillContext, SkillCastSnapshot cast)
        {
            if (host == null || context == null || enemies == null || skills == null || skillContext == null)
                throw new ArgumentNullException(nameof(context));
            if (cast == null || cast.DamageAction == null || cast.TargetInstanceIds.Count != 1)
                throw new ArgumentException("子弹必须持有本局单目标技能施放快照。", nameof(cast));
            if (cast.BattleId != context.BattleId || skillContext.Battle != context.Battle)
                throw new ArgumentException("技能施放不属于当前对局。", nameof(cast));
            if (cast.ProjectileFlightTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cast));
            _host = host; _context = context; _enemies = enemies; _skills = skills;
            _skillContext = skillContext; _cast = cast; Action = cast.DamageAction; _resolution = null;
            BattleId = cast.BattleId; Side = cast.SourceSide; TargetInstanceId = cast.TargetInstanceIds[0];
            Damage = Action.LockedDamage; Sequence = Action.Sequence; _start = cast.Origin;
            _flightTime = cast.ProjectileFlightTime; _arcHeight = cast.ProjectileArcHeight;
            Elapsed = 0f; IsResolved = false; transform.position = _start;
        }

        /// <returns>true 表示应回收；冻结期间不推进，也不补算暂停时间。</returns>
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsResolved) return true;
            if (_host == null || _context == null || _host.Context != _context || _context.BattleId != BattleId ||
                _context.IsEnded || _context.IsEndRequested || _enemies == null || !_enemies.OwnsContext(_context))
                return AbandonSkillCast();
            if (!_host.IsCombatRunning) return false;
            if (!_enemies.TryGetTargetPosition(Side, TargetInstanceId, out var target)) return AbandonSkillCast();
            Elapsed = Mathf.Min(_flightTime, Elapsed + deltaTime);
            var u = Elapsed / _flightTime;
            transform.position = Vector3.Lerp(_start, target, u) + Vector3.up * (4f * _arcHeight * u * (1f - u));
            if (Elapsed < _flightTime) return false;
            // 先封住重复结算，再扣血；击杀立即摘掉移动记录并交付不可变快照。
            Resolve();
            if (_skills != null) _skills.OnHit(_cast, _skillContext);
            else _context.Combat.Resolve(Action, _resolution);
            return true;
        }

        private bool Resolve() { IsResolved = true; return true; }

        private bool AbandonSkillCast()
        {
            Resolve();
            if (_skills != null && _cast != null) _skills.Cancel(_cast);
            return true;
        }
    }
}
