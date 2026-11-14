using System;
using System.Collections.Generic;
using TEngine;

namespace GameLogic
{
    public sealed class BattleRuntime : IDisposable
    {
        private readonly bool _ownsRegistry;
        private bool _assistsConfigured;
        public long BattleId { get; }
        public BattleEntityRegistry Registry { get; }
        public CombatRuntime Combat { get; }
        public SkillCatalog Catalog { get; private set; } = new SkillCatalog();
        public SkillManager Skills { get; }
        public RobotTriggeredSkillQueue TriggeredSkills { get; }
        public BuffBehaviorRegistry BuffBehaviors { get; } = new BuffBehaviorRegistry();
        public BuffManager Buffs { get; }
        public TimelineManager Timelines { get; }
        public BulletManager Bullets { get; } = new BulletManager();
        public BulletBehaviorRegistry BulletBehaviors { get; } = new BulletBehaviorRegistry();
        public BattlefieldRuntime Player { get; }
        public BattlefieldRuntime Opponent { get; }
        public bool IsDisposed { get; private set; }
        public event Action<SkillOwnerRef, bool, string> EnemyIsolationChanged;
        // 终局请求立即失效；暂停只影响 CanCastNow，不影响已提交伤害的通知。
        public Func<bool> IsBattleValid { get; set; } = () => true;
        public Func<bool> CanCastNow { get; set; } = () => true;
        public BattleRuntime(long battleId)
            : this(battleId, new BattleEntityRegistry(), new CombatRuntime(battleId))
        { _ownsRegistry = true; }
        public BattleRuntime(long battleId, BattleEntityRegistry registry, CombatRuntime combat)
        {
            BattleId = battleId; Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (combat.BattleId != battleId) throw new ArgumentException("结算运行时归属错误。");
            Combat.Bind(this);
            Buffs = new BuffManager(this);
            BuffBehaviors.Register("slow", () => new SlowBuffBehavior(this));
            BuffBehaviors.Register("poison", () => new PoisonBuffBehavior(this));
            BuffBehaviors.Register("store_isolation", () => new StoreIsolationBuffBehavior(this));
            BuffBehaviors.Register("OpeningBallSpeedBonus", () => new OpeningBallSpeedBonusBehavior(this));
            BuffBehaviors.Register("ComboBallSpeedBonus", () => new ComboBallSpeedBonusBehavior(this));
            BuffBehaviors.Register("ComboRobotDamageBonus", () => new ComboRobotDamageBonusBehavior(this));
            BuffBehaviors.Register("GlassDurabilityOverride", () => new GlassDurabilityOverrideBehavior(this));
            BuffBehaviors.Register("ExtraBerserkBall", () => new ExtraBerserkBallBehavior(this));
            TriggeredSkills = new RobotTriggeredSkillQueue(this);
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

        /// <summary>正式发射前装配一次；持有等级与运行时叠层分开。</summary>
        public void ConfigureAssists(GameConfig.Tables tables, IReadOnlyList<MatchBuff> player, IReadOnlyList<MatchBuff> opponent)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(BattleRuntime));
            if (_assistsConfigured) throw new InvalidOperationException("本局助力已经装配。");
            if (tables == null || player == null || opponent == null) throw new ArgumentNullException();
            var requests = new List<AddBuffInfo>();
            PrepareAssists(tables, Player, player, requests);
            PrepareAssists(tables, Opponent, opponent, requests);
            _assistsConfigured = true;
            foreach (var request in requests) Buffs.Submit(request);
        }

        private void PrepareAssists(GameConfig.Tables tables, BattlefieldRuntime owner, IReadOnlyList<MatchBuff> carried,
            List<AddBuffInfo> requests)
        {
            var seen = new HashSet<int>();
            foreach (var carry in carried)
            {
                if (carry == null || !seen.Add(carry.BuffId)) throw new ArgumentException("携带助力重复或为空。");
                var row = tables.TbBuff.GetOrDefault(carry.BuffId);
                if (row == null || carry.Layers < 1 || carry.Layers > row.MaxLevel || row.GrantType != "HeldBuff")
                    throw new InvalidOperationException("携带助力配置或等级无效：" + carry.BuffId);
                var i = carry.Layers - 1;
                var parameters = new Dictionary<string, float> { { "level", carry.Layers } };
                var duration = 0f;
                switch (row.BehaviorKey)
                {
                    case "OpeningBallSpeedBonus":
                        parameters.Add("speedRate", AssistValue(row.SpeedBonusRates, i, row.Id));
                        duration = AssistValue(row.DurationSeconds, i, row.Id);
                        if (duration <= 0f) throw new InvalidOperationException("开局助力持续时间必须大于0。");
                        break;
                    case "ComboBallSpeedBonus":
                    case "ComboRobotDamageBonus":
                        var step = AssistValue(row.ComboSteps, i, row.Id);
                        if (step <= 0) throw new InvalidOperationException("Combo档位必须大于0。");
                        parameters.Add("comboStep", step);
                        parameters.Add("comboCap", AssistValue(row.ComboBonusCaps, i, row.Id));
                        parameters.Add(row.BehaviorKey == "ComboBallSpeedBonus" ? "speedRate" : "damageRate",
                            AssistValue(row.BehaviorKey == "ComboBallSpeedBonus" ? row.SpeedBonusRates : row.DamageBonusRates, i, row.Id));
                        break;
                    case "GlassDurabilityOverride":
                        var loss = AssistValue(row.GlassDurabilityPerHit, i, row.Id);
                        if (loss <= 0) throw new InvalidOperationException("玻璃助力扣耐久必须大于0。");
                        parameters.Add("glassLoss", loss);
                        break;
                    case "ExtraBerserkBall":
                        parameters.Add("extraBalls", AssistValue(row.ExtraBerserkBallCount, i, row.Id));
                        break;
                    default: throw new InvalidOperationException("未实现的战场助力：" + row.BehaviorKey);
                }
                var model = new BuffModel(row.Id, row.BehaviorKey, 1, row.Priority);
                requests.Add(new AddBuffInfo(model, owner.OwnerRef, owner.OwnerRef, duration, true,
                    param: parameters, permanent: duration == 0f));
            }
        }

        private static float AssistValue(float[] values, int index, int id)
        {
            if (values == null || index >= values.Length || !EntityValidation.IsFiniteNonNegative(values[index]))
                throw new InvalidOperationException("助力等级参数无效：" + id);
            return values[index];
        }

        private static int AssistValue(int[] values, int index, int id)
        {
            if (values == null || index >= values.Length || values[index] < 0)
                throw new InvalidOperationException("助力等级参数无效：" + id);
            return values[index];
        }
        internal void Attach(Entity entity)
        {
            if (IsDisposed) return;
            entity.BindSkillOwner(BattleId);
            Buffs.Bind(entity.Buffs);
            if (entity is RobotEntity robot && robot.SkillId > 0 && Catalog.TryGet(robot.SkillId, out var model))
            {
                Grant(entity, model).SetLevel(Math.Min(robot.Level, model.MaxLevel));
                robot.AttackCycle.Configure(this, robot.Configuration?.AttackCycleSkillId ?? 0);
            }
        }
        public SkillObj Grant(ISkillOwner owner, SkillModel model)
        {
            if (!TryResolveOwner(owner.OwnerRef, out var current) || !ReferenceEquals(current, owner))
                throw new ArgumentException("技能持有者不属于本局。");
            var existed = owner.Skills.TryGet(model.Id, out var existing);
            if (existed) return existing;
            var obj = owner.Skills.Add(model);
            if (owner is RobotEntity robot) obj.SetLevel(Math.Min(robot.Level, model.MaxLevel));
            foreach (var buffId in model.PassiveBuffIds)
                if (Catalog.TryGetBuff(buffId, out var buff))
                {
                    if (buff.TriggerSkillId > 0)
                    {
                        if (!Catalog.TryGet(buff.TriggerSkillId, out var triggered))
                            throw new InvalidOperationException("缺少被动触发技能：" + buff.TriggerSkillId);
                        Grant(owner, triggered);
                    }
                    Buffs.Submit(new AddBuffInfo(buff, owner.OwnerRef, owner.OwnerRef, permanent: true));
                }
                else throw new InvalidOperationException("缺少被动Buff配置：" + buffId);
            return obj;
        }
        public void AddBuff(ISkillOwner target, BuffModel model, SkillOwnerRef source, float duration,
            System.Collections.Generic.IReadOnlyDictionary<string, float> parameters = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (IsDisposed || !Combat.AllowsGameplayTriggers) return;
            EntityValidation.RequireFiniteNonNegative(duration, nameof(duration));
            if (duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration));
            Buffs.Submit(new AddBuffInfo(model, source, target.OwnerRef, duration,
                durationSetTo: true, param: BuffObj.Copy(parameters)));
        }
        internal void SetEnemyIsolation(EnemyEntity enemy, bool isolated, string prefab)
        {
            if (enemy.IsIsolated == isolated) return;
            enemy.SetIsolated(isolated);
            if (!IsDisposed) EnemyIsolationChanged?.Invoke(enemy.OwnerRef, isolated, prefab);
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
        internal void AdvanceBeforeEnemyMovement(float deltaTime, bool advanceTimelines = true) => AdvanceCore(deltaTime, false, advanceTimelines);
        private void AdvanceCore(float deltaTime, bool advanceEnemyBuffs, bool advanceTimelines = true)
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
            if (advanceTimelines) Timelines.Advance(deltaTime);
        }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            TriggeredSkills.Clear();
            Exception failure = null;
            Action<Action> clean = action =>
            {
                try { action(); }
                catch (Exception exception) { if (failure == null) failure = exception; }
            };
            clean(Timelines.Dispose); clean(Bullets.Dispose); clean(Player.Clear); clean(Opponent.Clear); clean(Combat.Dispose);
            foreach (var id in Registry.CopyIds())
                if (Registry.TryGet(id, out var entity))
                {
                    if (entity is RobotEntity robot) clean(robot.AttackCycle.Reset);
                    entity.Skills.Clear(); clean(entity.Buffs.Dispose);
                }
            EnemyIsolationChanged = null;
            if (_ownsRegistry) clean(Registry.Dispose);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}

namespace GameLogic
{
    /// <summary>战场不是实体：没有伪造主角身份，也不承担网格占用。</summary>
    public sealed class BattlefieldRuntime : ISkillOwner
    {
        private readonly BattleRuntime _battle;
        public SkillOwnerRef OwnerRef { get; }
        public long BattleId => OwnerRef.BattleId;
        public BattleSide Side => OwnerRef.Side;
        public SkillContainer Skills { get; } = new SkillContainer();
        public BuffContainer Buffs { get; } = new BuffContainer();
        public BattleAssistState Assists { get; }
        public bool CanCast => !_battle.IsDisposed;
        internal BattlefieldRuntime(BattleRuntime battle, BattleSide side)
        {
            _battle = battle; OwnerRef = SkillOwnerRef.Battlefield(battle.BattleId, side); Buffs.Owner = OwnerRef;
            Assists = new BattleAssistState(battle.BattleId, side);
            battle.Buffs.Bind(Buffs);
        }
        internal void Clear() { Skills.Clear(); Buffs.Dispose(); Assists.Clear(); }
    }
}



namespace GameLogic
{
    /// <summary>按对局标识隔离的展示数据；时间唯一来自战斗时间轴。</summary>
    public readonly struct BattleProgressSnapshot
    {
        public long BattleId { get; }
        public int CurrentWave { get; }
        public double ElapsedSeconds { get; }

        public BattleProgressSnapshot(long battleId, int currentWave, double elapsedSeconds)
        {
            BattleId = battleId;
            CurrentWave = currentWave;
            ElapsedSeconds = elapsedSeconds;
        }
    }

    public static class BattleProgressEvents
    {
        public static readonly int Changed = RuntimeId.ToRuntimeId("BattleProgressEvents.Changed");
    }
}
