using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>敌人结算适配：同步读池化实体，保存数值后扣血，不让修改管线接触实体或表现。</summary>
    public sealed class EnemyCombatResolutionPort : ICombatResolutionPort, ICombatTargetQuery
    {
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;

        public EnemyCombatResolutionPort(BattleShopContext context, EnemyBattleController enemies)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _enemies = enemies != null ? enemies : throw new ArgumentNullException(nameof(enemies));
        }

        public IReadOnlyList<int> GetValidTargets(CombatActionSnapshot action)
        {
            var targets = new List<int>();
            if (!Owns(action)) return targets;
            foreach (var id in action.TargetInstanceIds)
                if (_enemies.TryGetTargetPosition(action.SourceSide, id, out _)) targets.Add(id);
            return targets;
        }

        public CombatResolutionResult Resolve(CombatOutcomeDraft outcome)
        {
            var targets = new List<CombatTargetResolution>();
            var damage = outcome.GetFinalDamage();
            foreach (var id in outcome.TargetInstanceIds)
            {
                if (damage <= 0 || !Owns(outcome.Action) ||
                    !_enemies.TryGetTargetPosition(outcome.Action.SourceSide, id, out _) ||
                    !_context.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity enemy))
                { targets.Add(new CombatTargetResolution(id, damage, DamageResult.Ignored)); continue; }
                var applied = Math.Min(enemy.CurrentHp, damage);
                var eliteOrBoss = enemy.Configuration != null && enemy.Configuration.EnemyType >= 2;
                _enemies.TryPlayDamageFeedback(outcome.Action.SourceSide, id, damage);
                var result = _context.Registry.ApplyDamage(id, damage, out var removal);
                if (result == DamageResult.Killed) _enemies.NotifyKilled(removal);
                targets.Add(new CombatTargetResolution(id, damage, result,
                    result == DamageResult.Ignored ? 0 : applied, eliteOrBoss));
            }
            return new CombatResolutionResult(outcome, targets);
        }

        private bool Owns(CombatActionSnapshot action) => action.BattleId == _context.BattleId &&
            !_context.IsEnded && !_context.IsEndRequested && _enemies != null && _enemies.OwnsContext(_context);
    }
}
