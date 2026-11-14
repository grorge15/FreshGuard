using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using GameConfig.bullet;
using UnityEngine;

namespace GameLogic
{
    /// <summary>单局攻击装配；异步只准备表现，发射和命中由场景固定顺序提交。</summary>
    public sealed class RobotCombatController : IDisposable
    {
        private readonly BattleShopSceneController _host;
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;
        private readonly IDictionary<int, GameObject> _views;
        private readonly SharedEnemyTargetSelector _targets;
        private readonly Dictionary<int, Reserve> _reserves = new Dictionary<int, Reserve>();
        private readonly IBulletWorld _bulletWorld;
        private readonly Func<string, Transform, CancellationToken, UniTask<GameObject>> _load;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Transform _preparedRoot;
        private readonly Transform _flightRoot;
        private readonly Dictionary<string, Queue<GameObject>> _effectReady = new Dictionary<string, Queue<GameObject>>();
        private readonly HashSet<string> _effectLoading = new HashSet<string>();
        private readonly Dictionary<string, int> _effectCapacity = new Dictionary<string, int>();
        private readonly List<EffectEntry> _effects = new List<EffectEntry>();
        private readonly Queue<EffectRequest> _pendingEffects = new Queue<EffectRequest>();
        private sealed class EffectRequest
        {
            internal string Prefab;
            internal BattleSide Side;
            internal Vector3[] Positions;
        }
        private sealed class EffectEntry
        {
            internal string Prefab;
            internal GameObject Instance;
            internal IBattleEffectView View;
        }
        private bool _prepared;
        private bool _disposed;
        private readonly StaffBattleController _staffs;

        private sealed class Reserve
        {
            internal RobotEntity Robot;
            internal RobotView View;
            internal RobotCastAnimationView CastAnimation;
            internal RobotChargeAnimationView ChargeAnimation;
            internal readonly Dictionary<(int SkillId, string Prefab), ProjectilePool> Pools = new Dictionary<(int, string), ProjectilePool>();
            internal CancellationTokenSource Cancellation;
        }
        private sealed class ProjectilePool
        {
            internal string Prefab;
            internal readonly Queue<GameObject> Ready = new Queue<GameObject>();
            internal int Capacity = 1;
            internal bool Loading;
            internal float RetryRemaining;
        }

        public int LiveBulletCount => _context.Battle.Bullets.Count;
        private bool OwnsBattle => !_disposed && _host != null && _host.Context == _context &&
            !_context.IsEnded && !_context.IsEndRequested && _enemies != null && _enemies.OwnsContext(_context);
        private bool IsRunning => _prepared && OwnsBattle && _host.IsCombatRunning;

        public RobotCombatController(BattleShopSceneController host, BattleShopContext context,
            EnemyBattleController enemies, IDictionary<int, GameObject> views)
            : this(host, context, enemies, views, LoadAsync) { }

        internal RobotCombatController(BattleShopSceneController host, BattleShopContext context,
            EnemyBattleController enemies, IDictionary<int, GameObject> views,
            Func<string, Transform, CancellationToken, UniTask<GameObject>> load)
        {
            _host = host != null ? host : throw new ArgumentNullException(nameof(host));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _enemies = enemies != null ? enemies : throw new ArgumentNullException(nameof(enemies));
            _views = views ?? throw new ArgumentNullException(nameof(views));
            _load = load ?? throw new ArgumentNullException(nameof(load));
            _targets = new SharedEnemyTargetSelector(context, enemies);
            _bulletWorld = new EnemyBulletWorld(host, context, enemies);
            _context.Battle.CanCastNow = () => IsRunning;
            _preparedRoot = new GameObject("PreparedRobotBullets").transform;
            _preparedRoot.SetParent(host.transform, false);
            _preparedRoot.gameObject.SetActive(false);
            _flightRoot = new GameObject("RobotBullets").transform;
            _flightRoot.SetParent(host.transform, false);
            _staffs = new StaffBattleController(host, context, enemies, load);
            _enemies.AttachStaffs(_staffs);
        }

        private static UniTask<GameObject> LoadAsync(string location, Transform parent, CancellationToken token)
        {
            if (!GameModule.Resource.CheckLocationValid(location))
                throw new InvalidOperationException("子弹资源未加入 YooAsset 收集：" + location);
            return GameModule.Resource.LoadGameObjectAsync(location, parent, token);
        }

        public async UniTask PrepareAsync(Tables tables, CancellationToken token)
        {
            if (_prepared) return;
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token))
            {
                var catalog = SkillConfigAdapter.FromTables(tables);
                foreach (var config in tables.TbRobot.DataList)
                    if (catalog.TryGet(config.SkillId, out _)) RobotEntity.ValidateAttackConfig(config, _context.Rules.MaxRobotLevel);
                foreach (var location in catalog.Bullets.Select(r => r.Prefab).Distinct())
                {
                    GameObject instance = null;
                    try
                    {
                        instance = await _load(location, _preparedRoot, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        if (!OwnsBattle) throw new OperationCanceledException(linked.Token);
                        if (instance == null || instance.GetComponent<RobotBulletView>() == null)
                            throw new InvalidOperationException("子弹 Prefab 缺少 RobotBulletView：" + location);
                    }
                    finally { Release(instance); }
                }
                foreach (var location in catalog.Bullets.Select(r => r.HitEffectPrefab).Where(r => !string.IsNullOrEmpty(r)).Distinct())
                {
                    var capacity = tables.TbSkillLevel.DataList.Where(level =>
                        catalog.TryGet(level.SkillId, out var skill) && catalog.TryGetBullet(skill.BulletId, out var bullet) &&
                        bullet.HitEffectPrefab == location).Select(level => level.DropCount).DefaultIfEmpty(1).Max();
                    _effectCapacity[location] = Math.Max(1, capacity);
                    for (var i = 0; i < _effectCapacity[location]; i++)
                    {
                        var effect = await _load(location, _preparedRoot, linked.Token);
                        if (effect == null) throw new InvalidOperationException("命中表现资源为空：" + location);
                        if (linked.IsCancellationRequested || !OwnsBattle) { Release(effect); linked.Token.ThrowIfCancellationRequested(); throw new OperationCanceledException(); }
                        if (GetEffectView(effect) == null)
                        { Release(effect); throw new InvalidOperationException("命中表现缺少IBattleEffectView：" + location); }
                        effect.SetActive(false);
                        if (!_effectReady.TryGetValue(location, out var pool)) _effectReady.Add(location, pool = new Queue<GameObject>());
                        pool.Enqueue(effect);
                    }
                }
                _context.Battle.Configure(catalog);
                await _staffs.PrepareAsync(catalog, linked.Token);
            }
            _context.Battle.Timelines.RegisterAction("robot.bullet", new RobotBulletAction(this));
            _context.Battle.Timelines.RegisterAction("robot.cast.animation", new CastAnimationAction(this));
            _context.Battle.Timelines.RegisterFactory("robot.chain", CreateChainTimeline);
            _context.Battle.Timelines.RegisterAction("robot.burst.shot", new BurstBulletAction(this));
            _context.Battle.Timelines.RegisterAction("robot.juice.drops", new JuiceBulletAction(this));
            _context.Battle.Timelines.Register(new TimelineModel("robot.juice", 0f,
                new[] { new TimelineNode(0f, "robot.juice.drops") }));
            _context.Battle.Timelines.Register(new TimelineModel("robot.single", 0f,
                new[] { new TimelineNode(0f, "robot.bullet") }));
            foreach (var key in new[] { "robot.scatter", "robot.pierce", "robot.impact", "robot.store" })
                _context.Battle.Timelines.Register(new TimelineModel(key, 0f,
                    new[] { new TimelineNode(0f, "robot.bullet") }));
            _context.Battle.Timelines.RegisterFactory("robot.burst", CreateBurstTimeline);
            _prepared = true;
        }

        /// <summary>部署成功并绑定视图后调用；同一个实体只创建一个FSM和一个待发槽。</summary>
        public void SyncRobots()
        {
            if (!_prepared || !OwnsBattle) return;
            foreach (var id in _reserves.Keys.ToArray())
            {
                if (!_views.TryGetValue(id, out var view) || view == null ||
                    !_context.Registry.TryGet(id, out var entity) || !(entity is RobotEntity robot) || robot.IsRemoved)
                    RemoveReserve(id);
            }
            foreach (var pair in _views.ToArray())
            {
                if (pair.Value == null || !_context.Registry.TryGet(pair.Key, out var entity) ||
                    !(entity is RobotEntity robot) || robot.IsRemoved) continue;
                var view = pair.Value.GetComponent<RobotView>();
                if (view == null || view.InstanceId != robot.InstanceId) continue;
                if (_reserves.TryGetValue(pair.Key, out var existing)) { existing.View = view; continue; }
                if (robot.Location != RobotLocation.Board || !_context.Battle.Catalog.TryGet(robot.SkillId, out _)) continue;
                robot.InitializeCombat(_context.BattleId);
                var reserve = new Reserve { Robot = robot, View = view, CastAnimation = view.GetComponent<RobotCastAnimationView>(),
                    ChargeAnimation = view.GetComponent<RobotChargeAnimationView>(),
                    Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) };
                _reserves.Add(pair.Key, reserve);
                reserve.ChargeAnimation?.Bind(_context.Battle, robot, reserve.CastAnimation);
                PrepareSkillPools(reserve, robot.SkillId);
                if (robot.AttackCycle.TriggerSkillId > 0)
                    PrepareSkillPools(reserve, robot.AttackCycle.TriggerSkillId);
                if (_context.Battle.Catalog.TryGet(robot.SkillId, out var primary))
                    foreach (var id in primary.PassiveBuffIds)
                        if (_context.Battle.Catalog.TryGetBuff(id, out var buff) && buff.TriggerSkillId > 0)
                            PrepareSkillPools(reserve, buff.TriggerSkillId);
            }
        }

        public void AdvanceExistingBullets(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning) return;
            foreach (var reserve in _reserves.Values.ToArray())
            {
                reserve.CastAnimation?.Advance(deltaTime);
                reserve.ChargeAnimation?.Advance(deltaTime);
            }
            // 先推进已有表现，本段命中新创建的闪电不额外消耗整段时间。
            foreach (var effect in _effects.ToArray())
            {
                if (effect.Instance != null && effect.View != null && !effect.View.Advance(deltaTime)) continue;
                _effects.Remove(effect);
                if (effect.Instance == null) continue;
                effect.Instance.SetActive(false); effect.Instance.transform.SetParent(_preparedRoot, false);
                _effectReady[effect.Prefab].Enqueue(effect.Instance);
            }
            _context.Battle.Bullets.Advance(deltaTime);
            PlayPendingEffects();
        }

        public void AdvanceAttacks(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning) return;
            SyncRobots();
            foreach (var pair in _reserves.OrderBy(p => p.Key).ToArray())
            {
                if (!IsRunning) return;
                var reserve = pair.Value;
                foreach (var pool in reserve.Pools.Values.ToArray())
                {
                    pool.RetryRemaining = Mathf.Max(0f, pool.RetryRemaining - deltaTime);
                    if (!pool.Loading && pool.Ready.Count < pool.Capacity && pool.RetryRemaining <= 0f)
                        PrepareNextAsync(reserve, pool).Forget();
                }
            }
            _context.Battle.TriggeredSkills.Process();
        }

        /// <summary>每次接触只尝试一次；冷却、无目标或资源未就绪时直接丢弃，不排队补发。</summary>
        public bool TryAttackFromContact(long battleId, BattleSide side, int robotId)
        {
            if (!IsRunning || battleId != _context.BattleId ||
                (side != BattleSide.Player && side != BattleSide.Opponent) ||
                !_context.Registry.TryGet(robotId, out var entity) || !(entity is RobotEntity robot) ||
                robot.Side != side || !robot.CanParticipate || robot.TriggerCooldownRemaining > 0f) return false;
            var fired = TryFire(robot);
            robot.TickCombat(fired);
            if (IsRunning) _context.Battle.TriggeredSkills.Process();
            if (OwnsBattle && _reserves.TryGetValue(robotId, out var reserve)) reserve.ChargeAnimation?.Refresh();
            return fired;
        }

        private bool TryFire(RobotEntity robot)
        {
            if (!IsRunning || !robot.CanParticipate || robot.TriggerCooldownRemaining > 0f ||
                !_reserves.TryGetValue(robot.InstanceId, out var reserve) ||
                !ReferenceEquals(reserve.Robot, robot) || reserve.View == null ||
                reserve.View.InstanceId != robot.InstanceId || !reserve.View.isActiveAndEnabled ||
                !_context.Registry.TryGet(robot.InstanceId, out var current) || !ReferenceEquals(current, robot)) return false;
            var target = _targets.GetTarget(robot.Side);
            if (!target.HasValue || !_enemies.TryGetTargetPosition(robot.Side, target.Value, out var aimPoint)) return false;
            return _context.Battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef, robot.SkillId,
                SkillTriggerSource.Contact, SkillTarget.Entity(robot.Side, target.Value, aimPoint)),
                () => robot.TryConsumeAttackTrigger(robot.Configuration.MinTriggerInterval)).Accepted;
        }

        private sealed class CastAnimationAction : ISingleUseTimelineAction, IReleasableTimelineAction
        {
            private readonly RobotCombatController _controller;
            internal CastAnimationAction(RobotCombatController controller) { _controller = controller; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) =>
                TryPrepare(cast, node, out execute, out _);
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release)
            {
                execute = null; release = null;
                if (!cast.Owner.EntityId.HasValue || !_controller._reserves.TryGetValue(cast.Owner.EntityId.Value, out var reserve) ||
                    reserve.CastAnimation == null || !reserve.CastAnimation.IsPrepared) return false;
                var view = reserve.CastAnimation;
                execute = () =>
                {
                    if (view == null) return;
                    reserve.ChargeAnimation?.Suspend();
                    view.Play(cast, cast.Get("castWindup"));
                };
                release = () => { if (view != null) view.Stop(cast); };
                return true;
            }
        }

        private static TimelineModel CreateChainTimeline(SkillCastSnapshot cast)
        {
            var windup = cast.Get("castWindup");
            if (!EntityValidation.IsFiniteNonNegative(windup) || windup <= 0f) return null;
            return new TimelineModel(cast.TimelineKey, windup, new[]
            {
                new TimelineNode(0f, "robot.cast.animation"),
                new TimelineNode(windup, "robot.bullet")
            });
        }

        private sealed class RobotBulletAction : ISingleUseTimelineAction, IReleasableTimelineAction
        {
            private readonly RobotCombatController _controller;
            internal RobotBulletAction(RobotCombatController controller) { _controller = controller; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) =>
                _controller.TryPrepareBullet(cast, node, out execute, out _);
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release) =>
                _controller.TryPrepareBullet(cast, node, out execute, out release);
        }

        private sealed class BurstBulletAction : IReleasableTimelineAction
        {
            private readonly RobotCombatController _controller;
            internal BurstBulletAction(RobotCombatController controller) { _controller = controller; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) =>
                _controller.TryPrepareBullet(cast, node, out execute, out _);
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release) =>
                _controller.TryPrepareBullet(cast, node, out execute, out release);
        }

        private sealed class JuiceBulletAction : ISingleUseTimelineAction, IReleasableTimelineAction
        {
            private readonly RobotCombatController _controller;
            internal JuiceBulletAction(RobotCombatController controller) { _controller = controller; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) =>
                _controller.TryPrepareJuice(cast, out execute, out _);
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release) =>
                _controller.TryPrepareJuice(cast, out execute, out release);
        }

        private static bool TryCount(float value, out int count)
        {
            count = 0;
            if (!EntityValidation.IsFiniteNonNegative(value) || value < 1f || value >= int.MaxValue || value != (int)value) return false;
            count = (int)value;
            return true;
        }

        private static TimelineModel CreateBurstTimeline(SkillCastSnapshot cast)
        {
            if (!TryCount(cast.Get("burstCount"), out var count)) return null;
            var interval = cast.Get("burstInterval");
            var duration = (count - 1) * interval;
            if (!EntityValidation.IsFiniteNonNegative(interval) || interval <= 0f ||
                !EntityValidation.IsFiniteNonNegative(duration)) return null;
            var nodes = new List<TimelineNode>();
            for (var i = 0; i < count; i++)
                nodes.Add(new TimelineNode(i * interval, "robot.burst.shot",
                    new Dictionary<string, float> { { "shotIndex", i } }));
            return new TimelineModel(cast.TimelineKey, duration, nodes);
        }

        private int GetReadyCapacity(int skillId)
        {
            if (!_context.Battle.Catalog.TryGet(skillId, out var skill)) return 1;
            var count = 1;
            for (var i = 1; i <= skill.MaxLevel; i++)
                if (_context.Battle.Catalog.TryGetLevel(skill.Id, i, out var level))
                    foreach (var key in new[] { "scatterCount", "burstCount", "dropCount" })
                        if (level.Parameters.TryGetValue(key, out var value) && TryCount(value, out var configured))
                            count = Math.Max(count, configured);
            return count;
        }

        private ProjectilePool GetPool(Reserve reserve, int skillId, BulletModel model)
        {
            var key = (skillId, model.Prefab);
            if (!reserve.Pools.TryGetValue(key, out var pool))
            {
                pool = new ProjectilePool { Prefab = model.Prefab, Capacity = GetReadyCapacity(skillId) };
                reserve.Pools.Add(key, pool);
            }
            return pool;
        }
        private void PrepareSkillPools(Reserve reserve, int skillId)
        {
            if (!_context.Battle.Catalog.TryGet(skillId, out var skill) ||
                !_context.Battle.Catalog.TryGetBullet(skill.BulletId, out var model)) return;
            PrepareNextAsync(reserve, GetPool(reserve, skillId, model)).Forget();

        }

        private bool TryPrepareBullet(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release)
        {
            execute = null; release = null;
            if (!IsRunning || cast.Owner.Kind != SkillOwnerKind.Entity || !cast.Owner.EntityId.HasValue ||
                cast.Target.Kind != SkillTargetKind.Entity || cast.Target.Side != cast.Owner.Side ||
                !_context.Registry.TryGet(cast.Owner.EntityId.Value, out var entity) || !(entity is RobotEntity robot) ||
                !robot.CanParticipate || !_reserves.TryGetValue(robot.InstanceId, out var reserve) ||
                !ReferenceEquals(reserve.Robot, robot) || reserve.View == null ||
                reserve.View.InstanceId != robot.InstanceId || !reserve.View.isActiveAndEnabled ||
                !_enemies.TryGetTargetPosition(robot.Side, cast.Target.EntityId, out _) ||
                !_context.Battle.Catalog.TryGet(cast.SkillId, out var model) ||
                !_context.Battle.Catalog.TryGetBullet(cast.BulletId, out var bulletModel)) return false;
            var pool = GetPool(reserve, cast.SkillId, bulletModel);
            _context.Battle.BulletBehaviors.Create(bulletModel.BehaviorKey);
            if (bulletModel.BehaviorKey == "chain")
                new ChainBulletSnapshot(checked((int)cast.Get("chainTargetCount")), cast.Get("chainRange"), cast.Get("chainDamageRatio"));
            var burst = cast.TimelineKey == "robot.burst";
            var scatter = cast.TimelineKey == "robot.scatter";
            var count = 1;
            if (scatter && !TryCount(cast.Get("scatterCount"), out count)) return false;
            var total = count;
            if (burst && !TryCount(cast.Get("burstCount"), out total)) return false;
            var shotIndex = node.Parameters.TryGetValue("shotIndex", out var shot) ? (int)shot : 0;
            var angle = scatter ? cast.Get("scatterAngle") : 0f;
            var knockback = cast.Get("knockbackDistance");
            var maxHits = 1;
            if (bulletModel.HitMode == BulletHitMode.PierceEnemies && !TryCount(cast.Get("pierceTargetCount"), out maxHits)) return false;
            if (!EntityValidation.IsFiniteNonNegative(angle) || angle > 360f ||
                !EntityValidation.IsFiniteNonNegative(knockback) ||
                !EntityValidation.IsFiniteNonNegative(cast.Get("damage")) || cast.Get("damage") >= int.MaxValue) return false;
            if (shotIndex == 0 && pool.Ready.Count < total)
            {
                pool.Capacity = Math.Max(pool.Capacity, total);
                PrepareNextAsync(reserve, pool).Forget();
                if (pool.Ready.Count < total) return false;
            }
            if (pool.Ready.Count < count) return false;
            var sourceId = robot.InstanceId;
            var damage = model.Type == SkillType.NormalAttack ? robot.GetAttackDamage() : checked((int)cast.Get("damage"));
            var ready = new GameObject[count];
            for (var i = 0; i < count; i++) ready[i] = pool.Ready.Dequeue();
            // 闭包只捕获标识、只读配置与预留表现；不捕获池化实体。
            execute = () => FirePrepared(sourceId, cast, damage, bulletModel, ready, burst, angle, maxHits, knockback);
            release = () =>
            {
                foreach (var instance in ready)
                    ReturnPrepared(sourceId, cast.SkillId, bulletModel.Prefab, instance);
                Array.Clear(ready, 0, ready.Length);
            };
            return true;
        }

        private bool TryPrepareJuice(SkillCastSnapshot cast, out Action execute, out Action release)
        {
            execute = null; release = null;
            if (!IsRunning || cast.Target.Kind != SkillTargetKind.Position || cast.Owner.Kind != SkillOwnerKind.Entity ||
                !cast.Owner.EntityId.HasValue || !_reserves.TryGetValue(cast.Owner.EntityId.Value, out var reserve) ||
                !reserve.Robot.CanParticipate || reserve.View == null || !reserve.View.isActiveAndEnabled ||
                !_context.Battle.Catalog.TryGetBullet(cast.BulletId, out var model) || model.HitMode != BulletHitMode.AreaAtLanding ||
                !TryCount(cast.Get("dropCount"), out var count) || cast.Get("dropSpacing") <= 0 || cast.Get("splashRadius") <= 0 ||
                !EntityValidation.IsFiniteNonNegative(cast.Get("damage")) || cast.Get("damage") >= int.MaxValue) return false;
            var drops = _enemies.GetJuiceDropPositions(cast.Owner.Side, cast.Target.Position, count, cast.Get("dropSpacing")).ToArray();
            if (drops.Length == 0) return false;
            var pool = GetPool(reserve, cast.SkillId, model);
            if (pool.Ready.Count < drops.Length)
            {
                pool.Capacity = Math.Max(pool.Capacity, drops.Length);
                PrepareNextAsync(reserve, pool).Forget();
                if (pool.Ready.Count < drops.Length) return false;
            }
            var ready = new GameObject[drops.Length];
            for (var i = 0; i < ready.Length; i++) ready[i] = pool.Ready.Dequeue();
            var sourceId = cast.Owner.EntityId.Value;
            var damage = checked((int)cast.Get("damage"));
            var radius = cast.Get("splashRadius");
            execute = () => FireJuice(sourceId, cast, model, ready, drops, damage, radius);
            release = () =>
            {
                foreach (var instance in ready) ReturnPrepared(sourceId, cast.SkillId, model.Prefab, instance);
                Array.Clear(ready, 0, ready.Length);
            };
            return true;
        }

        private void FireJuice(int sourceId, SkillCastSnapshot cast, BulletModel model, GameObject[] ready,
            Vector3[] drops, int damage, float radius)
        {
            if (!_reserves.TryGetValue(sourceId, out var reserve) || reserve.View == null) return;
            var origin = reserve.View.GetAttackOrigin();
            for (var i = 0; i < ready.Length; i++)
            {
                if (!CanEmit(cast)) return;
                var instance = ready[i]; ready[i] = null;
                var launched = false;
                try
                {
                    if (instance == null) throw new InvalidOperationException("果汁预留弹丸丢失。");
                    var attack = _context.Combat.CaptureProjectileAttack(new CombatActionSnapshot(_context.BattleId,
                        sourceId, cast.Owner.Side, cast.SkillId, Array.Empty<int>(), CombatOutcomeTag.SingleDamage,
                        damage, origin, drops[i], i + 1, hitBuffs: cast.HitBuffs));
                    if (!CanEmit(cast)) return;
                    var behavior = _context.Battle.BulletBehaviors.Create(model.BehaviorKey);
                    var state = new BulletState(new BulletLauncher(model, attack.Action), _bulletWorld,
                        (hit, id) => behavior.OnHit(new BulletHitContext(hit, id, attack, _context.Combat,
                            (IProjectileWorld)_bulletWorld, present: positions => PlayHitEffect(model.HitEffectPrefab, cast.Owner.Side, positions),
                            splashRadius: radius)));
                    Launch(reserve, instance, state); launched = true;
                    if (OwnsBattle && !state.IsResolved && instance != null) instance.SetActive(true);
                }
                finally { if (!launched) ReturnPrepared(sourceId, cast.SkillId, model.Prefab, instance); }
            }
            PrepareNextAsync(reserve, GetPool(reserve, cast.SkillId, model)).Forget();
        }

        private void FirePrepared(int sourceId, SkillCastSnapshot cast, int damage, BulletModel model,
            GameObject[] ready, bool burst, float angle, int maxHits, float knockback)
        {
            if (!IsRunning || !_reserves.TryGetValue(sourceId, out var reserve) || reserve.View == null) return;
            var target = cast.Target.EntityId;
            if (!_enemies.TryGetTargetPosition(cast.Owner.Side, target, out var aimPoint))
            {
                var replacement = burst ? _targets.GetTarget(cast.Owner.Side) : null;
                if (!replacement.HasValue || !_enemies.TryGetTargetPosition(cast.Owner.Side, replacement.Value, out aimPoint))
                { _context.Battle.Timelines.Cancel(cast.Owner); return; }
                target = replacement.Value;
            }
            var origin = reserve.View.GetAttackOrigin();
            var heading = aimPoint - origin;
            heading.z = 0;
            if (model.FlightType == BulletFlightType.Straight && heading.sqrMagnitude <= 0f) heading = Vector3.up;
            var baseDirection = heading.normalized;
            for (var i = 0; i < ready.Length; i++)
            {
                if (!CanEmit(cast)) return;
                var instance = ready[i];
                if (instance == null) throw new InvalidOperationException("预留弹丸在发射前已丢失。");
                // 在执行用户Buff钩子前移出时间轴的释放集合，避免取消时同一实例被归还并继续发射。
                ready[i] = null;
                var launched = false;
                try
                {
                    var offset = ready.Length <= 1 ? 0f : -angle * 0.5f + i * angle / (ready.Length - 1);
                    var direction = Quaternion.AngleAxis(offset, Vector3.forward) * baseDirection;
                    var projectile = _context.Combat.CaptureProjectileAttack(new CombatActionSnapshot(_context.BattleId,
                        sourceId, cast.Owner.Side, cast.SkillId, new[] { target },
                        CombatOutcomeTag.SingleDamage | (knockback > 0f ? CombatOutcomeTag.Knockback : CombatOutcomeTag.None),
                        damage, origin, aimPoint, sequence: i + 1, hitBuffs: cast.HitBuffs, knockbackDistance: knockback));
                    if (!CanEmit(cast)) return;
                    var behavior = _context.Battle.BulletBehaviors.Create(model.BehaviorKey);
                    var chain = model.BehaviorKey == "chain" ? new ChainBulletSnapshot(
                        checked((int)cast.Get("chainTargetCount")), cast.Get("chainRange"), cast.Get("chainDamageRatio")) : null;
                    var state = new BulletState(new BulletLauncher(model, projectile.Action, direction, maxHits), _bulletWorld,
                        (hit, id) => behavior.OnHit(new BulletHitContext(hit, id, projectile, _context.Combat,
                            (IProjectileWorld)_bulletWorld, chain, positions => PlayHitEffect(model.HitEffectPrefab, cast.Owner.Side, positions))));
                    Launch(reserve, instance, state);
                    launched = true;
                    // 登记真实弹丸后才计数；同次散射只在首颗登记时提交一次。
                    if (i == 0) reserve.Robot.NotifyAttackLaunched(cast);
                    reserve.ChargeAnimation?.Refresh();
                    if (OwnsBattle && !state.IsResolved && instance != null) instance.SetActive(true);
                }
                finally { if (!launched) ReturnPrepared(sourceId, cast.SkillId, model.Prefab, instance); }
            }
            PrepareNextAsync(reserve, GetPool(reserve, cast.SkillId, model)).Forget();
        }

        private void PlayHitEffect(string prefab, BattleSide side, IReadOnlyList<Vector3> positions)
        {
            if (!IsRunning || string.IsNullOrEmpty(prefab)) return;
            // 仅延迟表现；伤害已在命中时提交，队列不持有敌人或机器人引用。
            _pendingEffects.Enqueue(new EffectRequest { Prefab = prefab, Side = side, Positions = positions.ToArray() });
            PlayPendingEffects();
        }
        private void PlayPendingEffects()
        {
            if (!IsRunning) return;
            var count = _pendingEffects.Count;
            for (var i = 0; i < count; i++)
            {
                var request = _pendingEffects.Dequeue();
                if (!_effectReady.TryGetValue(request.Prefab, out var pool) || pool.Count == 0)
                {
                    _pendingEffects.Enqueue(request);
                    PrepareHitEffectAsync(request.Prefab).Forget();
                    continue;
                }
                var instance = pool.Dequeue();
                if (instance == null)
                {
                    _pendingEffects.Enqueue(request);
                    PrepareHitEffectAsync(request.Prefab).Forget();
                    continue;
                }
                instance.transform.SetParent(_flightRoot, false);
                BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), request.Side);
                var view = GetEffectView(instance);
                view.Play(request.Positions); instance.SetActive(true);
                _effects.Add(new EffectEntry { Prefab = request.Prefab, Instance = instance, View = view });
                if (pool.Count == 0) PrepareHitEffectAsync(request.Prefab).Forget();
            }
        }
        private async UniTaskVoid PrepareHitEffectAsync(string prefab)
        {
            if (!OwnsBattle || !_effectLoading.Add(prefab)) return;
            GameObject instance = null;
            try
            {
                var capacity = _effectCapacity.TryGetValue(prefab, out var target) ? target : 1;
                if (!_effectReady.TryGetValue(prefab, out var pool)) _effectReady.Add(prefab, pool = new Queue<GameObject>());
                while (pool.Count < capacity)
                {
                    instance = await _load(prefab, _preparedRoot, _lifetime.Token);
                    if (!OwnsBattle || _lifetime.IsCancellationRequested) return;
                    if (instance == null || GetEffectView(instance) == null)
                        throw new InvalidOperationException("命中表现预备失败：" + prefab);
                    instance.SetActive(false);
                    pool.Enqueue(instance); instance = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { if (OwnsBattle) Debug.LogWarning("命中表现预备失败：" + exception.Message, _host); }
            finally { Release(instance); _effectLoading.Remove(prefab); }
        }

        private bool CanEmit(SkillCastSnapshot cast) => IsRunning && _context.Battle.Timelines.IsActive(cast) &&
            _context.Battle.TryResolveOwner(cast.Owner, out var owner) && owner.CanCast;

        private static IBattleEffectView GetEffectView(GameObject instance) => instance == null ? null :
            instance.GetComponents<MonoBehaviour>().OfType<IBattleEffectView>().FirstOrDefault();

        private void ReturnPrepared(int sourceId, int skillId, string prefab, GameObject instance)
        {
            if (instance == null) return;
            if (OwnsBattle && _reserves.TryGetValue(sourceId, out var reserve) &&
                reserve.Pools.TryGetValue((skillId, prefab), out var pool) && pool.Ready.Count < pool.Capacity)
                pool.Ready.Enqueue(instance);
            else Release(instance);
        }

        private void Launch(Reserve reserve, GameObject instance, BulletState state)
        {
            var bullet = instance.GetComponent<RobotBulletView>();
            bullet.Bind(state);
            instance.transform.SetParent(_flightRoot, true);
            BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), reserve.Robot.Side);
            _context.Battle.Bullets.Add(state, bullet.Present, () => Release(instance));
        }

        private async UniTaskVoid PrepareNextAsync(Reserve reserve, ProjectilePool pool)
        {
            if (pool.Loading || pool.Ready.Count >= pool.Capacity || pool.RetryRemaining > 0f || reserve.Cancellation.IsCancellationRequested || !OwnsBattle) return;
            pool.Loading = true;
            GameObject instance = null;
            var token = reserve.Cancellation.Token;
            try
            {
                while (pool.Ready.Count < pool.Capacity)
                {
                    instance = await _load(pool.Prefab, _preparedRoot, token);
                    token.ThrowIfCancellationRequested();
                    if (!OwnsBattle || !_reserves.TryGetValue(reserve.Robot.InstanceId, out var current) ||
                        !ReferenceEquals(current, reserve) || reserve.Robot.IsRemoved) return;
                    if (instance == null || instance.GetComponent<RobotBulletView>() == null)
                        throw new InvalidOperationException("子弹加载失败或缺少 RobotBulletView。");
                    instance.SetActive(false);
                    pool.Ready.Enqueue(instance);
                    instance = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (OwnsBattle && !token.IsCancellationRequested)
                {
                    pool.RetryRemaining = 1f;
                    Debug.LogError("机器人子弹准备失败，1秒战斗时间后重试：" + e.Message, _host);
                }
            }
            finally { Release(instance); pool.Loading = false; }
        }

        private void RemoveReserve(int id)
        {
            if (!_reserves.TryGetValue(id, out var reserve)) return;
            _reserves.Remove(id);
            _context.Battle.Timelines.Cancel(reserve.Robot.OwnerRef);
            reserve.ChargeAnimation?.Clear();
            reserve.Robot.ClearCombat();
            reserve.Cancellation.Cancel(); reserve.Cancellation.Dispose();
            foreach (var pool in reserve.Pools.Values) while (pool.Ready.Count > 0) Release(pool.Ready.Dequeue());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _prepared = false;
            _lifetime.Cancel();
            _staffs.Dispose();
            foreach (var effect in _effects) Release(effect.Instance);
            _effects.Clear();
            _pendingEffects.Clear();
            foreach (var pool in _effectReady.Values) while (pool.Count > 0) Release(pool.Dequeue());
            _effectReady.Clear();
            _targets.Clear();
            foreach (var id in _reserves.Keys.ToArray()) RemoveReserve(id);
            _context.Battle.Bullets.Clear();
            if (_preparedRoot != null) Release(_preparedRoot.gameObject);
            if (_flightRoot != null) Release(_flightRoot.gameObject);
            _lifetime.Dispose();
        }

        private static void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            // 待发和预检实例从未激活，不能只依赖框架 OnDestroy 归还资源。
            instance.GetComponent<TEngine.AssetsReference>()?.ReleaseReferences();
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}

namespace GameLogic
{
    /// <summary>每侧共享一个锁定目标；只同步读取存活实体和路线进度。</summary>
    public sealed class SharedEnemyTargetSelector
    {
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;
        private readonly int?[] _targets = new int?[2];
        private readonly List<int> _candidates = new List<int>();

        public SharedEnemyTargetSelector(BattleShopContext context, EnemyBattleController enemies)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _enemies = enemies != null ? enemies : throw new ArgumentNullException(nameof(enemies));
        }

        public int? GetTarget(BattleSide side)
        {
            EntityValidation.RequireSide(side);
            var index = (int)side;
            if (!_enemies.OwnsContext(_context)) { Clear(); return null; }
            var locked = _targets[index];
            if (locked.HasValue && _enemies.TryGetTargetPosition(side, locked.Value, out _)) return locked;
            _targets[index] = null;
            _enemies.CopyTargetCandidates(side, _candidates);
            var best = -1f;
            foreach (var id in _candidates)
            {
                if (!_context.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                if (enemy.RouteProgress > best || enemy.RouteProgress == best &&
                    (!_targets[index].HasValue || id < _targets[index].Value))
                {
                    best = enemy.RouteProgress;
                    _targets[index] = id;
                }
            }
            return _targets[index];
        }

        public void Clear() { _targets[0] = _targets[1] = null; _candidates.Clear(); }
    }
}
