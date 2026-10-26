using System;

namespace GameLogic
{
    public sealed class BattleRuntime : IDisposable
    {
        public long BattleId { get; }
        public BattleEntityRegistry Registry { get; }
        public CombatRuntime Combat { get; }
        public SkillCatalog Catalog { get; private set; } = new SkillCatalog();
        public SkillManager Skills { get; }
        public BuffBehaviorRegistry BuffBehaviors { get; } = new BuffBehaviorRegistry();
        public TimelineManager Timelines { get; }
        public BulletManager Bullets { get; } = new BulletManager();
        public BattlefieldRuntime Player { get; }
        public BattlefieldRuntime Opponent { get; }
        public bool IsDisposed { get; private set; }
        public Func<bool> CanCastNow { get; set; } = () => true;
        public BattleRuntime(long battleId, BattleEntityRegistry registry, CombatRuntime combat)
        {
            BattleId = battleId; Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (combat.BattleId != battleId) throw new ArgumentException("结算运行时归属错误。");
            Skills = new SkillManager(this); Timelines = new TimelineManager(this);
            Timelines.RegisterAction("buff.add", new AddBuffTimelineAction(this));
            Player = new BattlefieldRuntime(this, BattleSide.Player); Opponent = new BattlefieldRuntime(this, BattleSide.Opponent);
            Registry.BindBattle(this);
        }
        public void Configure(SkillCatalog catalog)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(BattleRuntime));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            foreach (var id in Registry.CopyIds()) if (Registry.TryGet(id, out var entity)) Attach(entity);
        }
        internal void Attach(Entity entity)
        {
            if (IsDisposed) return;
            entity.BindSkillOwner(BattleId);
            if (entity is RobotEntity robot && robot.SkillId > 0 && Catalog.TryGet(robot.SkillId, out var model))
                Grant(entity, model);
        }
        public SkillObj Grant(ISkillOwner owner, SkillModel model)
        {
            if (!TryResolveOwner(owner.OwnerRef, out var current) || !ReferenceEquals(current, owner))
                throw new ArgumentException("技能持有者不属于本局。");
            var existed = owner.Skills.TryGet(model.Id, out var existing);
            if (existed) return existing;
            var obj = owner.Skills.Add(model);
            foreach (var buffId in model.PassiveBuffIds)
                if (Catalog.TryGetBuff(buffId, out var buff)) AddBuff(owner, buff, owner.OwnerRef, float.MaxValue);
                else throw new InvalidOperationException("缺少被动Buff配置：" + buffId);
            return obj;
        }
        public void AddBuff(ISkillOwner target, BuffModel model, SkillOwnerRef source, float duration,
            System.Collections.Generic.IReadOnlyDictionary<string, float> parameters = null)
        {
            if (IsDisposed || !TryResolveOwner(target.OwnerRef, out var resolved) || !ReferenceEquals(target, resolved) ||
                source.BattleId != BattleId) return;
            target.Buffs.Add(model, source, duration, parameters, BuffBehaviors.Create(model.BehaviorKey));
        }
        public bool TryResolveOwner(SkillOwnerRef reference, out ISkillOwner owner)
        {
            owner = null;
            if (IsDisposed || reference.BattleId != BattleId ||
                (reference.Side != BattleSide.Player && reference.Side != BattleSide.Opponent)) return false;
            if (reference.Kind == SkillOwnerKind.Battlefield && !reference.EntityId.HasValue)
                owner = reference.Side == BattleSide.Player ? Player : Opponent;
            else if (reference.Kind == SkillOwnerKind.Entity && reference.EntityId.HasValue &&
                Registry.TryGet(reference.EntityId.Value, out var entity) && !entity.IsRemoved && entity.Side == reference.Side && entity.OwnerRef.Equals(reference))
                owner = entity;
            return owner != null;
        }
        public void Advance(float deltaTime)
            => AdvanceCore(deltaTime, true);

        // 场景移动阶段按到期/Tick边界推进敌人Buff；避免先过期再把整帧按原速移动。
        internal void AdvanceBeforeEnemyMovement(float deltaTime) => AdvanceCore(deltaTime, false);
        private void AdvanceCore(float deltaTime, bool advanceEnemyBuffs)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (IsDisposed) return;
            Registry.AdvanceTime(deltaTime, advanceEnemyBuffs);
            if (IsDisposed) return;
            Combat.AdvanceTime(deltaTime);
            if (IsDisposed) return;
            Player.Skills.Advance(deltaTime); Opponent.Skills.Advance(deltaTime);
            Player.Buffs.Advance(deltaTime); if (IsDisposed) return;
            Opponent.Buffs.Advance(deltaTime); if (IsDisposed) return;
            Timelines.Advance(deltaTime);
        }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            Exception failure = null;
            Action<Action> clean = action =>
            {
                try { action(); }
                catch (Exception exception) { if (failure == null) failure = exception; }
            };
            clean(Timelines.Dispose); clean(Bullets.Dispose); clean(Player.Clear); clean(Opponent.Clear); clean(Combat.Dispose);
            foreach (var id in Registry.CopyIds())
                if (Registry.TryGet(id, out var entity)) { entity.Skills.Clear(); clean(entity.Buffs.Dispose); }
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
