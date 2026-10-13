using System;
using GameConfig.robot;
using TEngine;

namespace GameLogic
{
    public partial class RobotEntity
    {
        private IFsm<RobotEntity> _combatFsm;
        private long _combatBattleId;
        public string CombatState => _combatFsm?.CurrentState is RobotAttackState ? "Attack" : "Idle";

        internal void InitializeCombat(long battleId)
        {
            if (IsRemoved || battleId <= 0) return;
            if (_combatFsm != null && !_combatFsm.IsDestroyed && _combatBattleId == battleId) return;
            ClearCombat();
            _combatBattleId = battleId;
            _combatFsm = GameModule.Fsm.CreateFsm<RobotEntity>("RobotCombat_" + battleId + "_" + InstanceId,
                this, new RobotIdleState(), new RobotAttackState());
            _combatFsm.Start<RobotIdleState>();
        }

        internal void TickCombat(bool canAttack)
        {
            if (_combatFsm == null || _combatFsm.IsDestroyed) return;
            ((RobotCombatState)_combatFsm.CurrentState).Tick(_combatFsm, canAttack && CanParticipate);
        }

        internal void ClearCombat()
        {
            var fsm = _combatFsm;
            _combatFsm = null; _combatBattleId = 0;
            if (fsm != null && !fsm.IsDestroyed) GameModule.Fsm.DestroyFsm(fsm);
        }

        internal static void ValidateAttackConfig(Robot config, int maxLevel)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            EntityValidation.RequireFiniteNonNegative(config.MinTriggerInterval, nameof(config.MinTriggerInterval));
            EntityValidation.RequireFiniteNonNegative(config.BulletFlightTime, nameof(config.BulletFlightTime));
            EntityValidation.RequireFiniteNonNegative(config.BulletArcHeight, nameof(config.BulletArcHeight));
            EntityValidation.RequirePositive(config.AttackDamage, nameof(config.AttackDamage));
            if (config.MinTriggerInterval <= 0f || config.BulletFlightTime <= 0f || config.DamagePerLevel < 0 ||
                string.IsNullOrWhiteSpace(config.BulletPrefab)) throw new ArgumentException("机器人攻击配置无效。", nameof(config));
            checked { var maxDamage = config.AttackDamage + (maxLevel - 1) * config.DamagePerLevel; }
        }

        public int GetAttackDamage()
        {
            if (Configuration == null) throw new InvalidOperationException("机器人缺少攻击配置。");
            return checked(Configuration.AttackDamage + (Level - 1) * Configuration.DamagePerLevel);
        }

        private abstract class RobotCombatState : FsmState<RobotEntity>
        {
            // 框架 OnUpdate 保持空实现；Tick 只维护状态，发射由接触入口显式完成。
            public abstract void Tick(IFsm<RobotEntity> fsm, bool canAttack);
        }

        private sealed class RobotIdleState : RobotCombatState
        {
            public override void Tick(IFsm<RobotEntity> fsm, bool canAttack)
            {
                if (!canAttack) return;
                ChangeState<RobotAttackState>(fsm);
            }
        }

        private sealed class RobotAttackState : RobotCombatState
        {
            public override void Tick(IFsm<RobotEntity> fsm, bool canAttack)
            {
                if (!canAttack) { ChangeState<RobotIdleState>(fsm); return; }
            }
        }
    }
}
