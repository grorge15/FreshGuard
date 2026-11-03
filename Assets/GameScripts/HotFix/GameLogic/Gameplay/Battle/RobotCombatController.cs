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
        private bool _prepared;
        private bool _disposed;

        private sealed class Reserve
        {
            internal RobotEntity Robot;
            internal RobotView View;
            internal readonly Queue<GameObject> Ready = new Queue<GameObject>();
            internal int Capacity = 1;
            internal bool Loading;
            internal float RetryRemaining;
            internal CancellationTokenSource Cancellation;
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
                foreach (var config in tables.TbRobot.DataList)
                    RobotEntity.ValidateAttackConfig(config, _context.Rules.MaxRobotLevel);
                foreach (var location in tables.TbBullet.DataList.Select(r => r.Prefab).Distinct())
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
            }
            _context.Battle.Configure(SkillConfigAdapter.FromTables(tables));
            _context.Battle.Timelines.RegisterAction("robot.bullet", new RobotBulletAction(this));
            _context.Battle.Timelines.RegisterAction("robot.burst.shot", new BurstBulletAction(this));
            _context.Battle.Timelines.Register(new TimelineModel("robot.single", 0f,
                new[] { new TimelineNode(0f, "robot.bullet") }));
            foreach (var key in new[] { "robot.scatter", "robot.pierce", "robot.impact" })
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
                if (robot.Location != RobotLocation.Board) continue;
                robot.InitializeCombat(_context.BattleId);
                var reserve = new Reserve { Robot = robot, View = view, Capacity = GetReadyCapacity(robot),
                    Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) };
                _reserves.Add(pair.Key, reserve);
                PrepareNextAsync(reserve).Forget();
            }
        }

        public void AdvanceExistingBullets(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning) return;
            _context.Battle.Bullets.Advance(deltaTime);
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
                reserve.RetryRemaining = Mathf.Max(0f, reserve.RetryRemaining - deltaTime);
                if (!reserve.Loading && reserve.Ready.Count < reserve.Capacity && reserve.RetryRemaining <= 0f)
                    PrepareNextAsync(reserve).Forget();
            }
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
            return fired;
        }

        private bool TryFire(RobotEntity robot)
        {
            if (!IsRunning || !robot.CanParticipate || robot.TriggerCooldownRemaining > 0f ||
                !_reserves.TryGetValue(robot.InstanceId, out var reserve) || reserve.Ready.Count == 0 ||
                !ReferenceEquals(reserve.Robot, robot) || reserve.View == null ||
                reserve.View.InstanceId != robot.InstanceId || !reserve.View.isActiveAndEnabled ||
                !_context.Registry.TryGet(robot.InstanceId, out var current) || !ReferenceEquals(current, robot)) return false;
            var target = _targets.GetTarget(robot.Side);
            if (!target.HasValue || !_enemies.TryGetTargetPosition(robot.Side, target.Value, out var aimPoint)) return false;
            return _context.Battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef, robot.SkillId,
                SkillTriggerSource.Contact, SkillTarget.Entity(robot.Side, target.Value)),
                () => robot.TryConsumeAttackTrigger(robot.Configuration.MinTriggerInterval)).Accepted;
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

        private int GetReadyCapacity(RobotEntity robot)
        {
            if (!_context.Battle.Catalog.TryGet(robot.SkillId, out var skill)) return 1;
            var count = 1;
            for (var i = 1; i <= skill.MaxLevel; i++)
                if (_context.Battle.Catalog.TryGetLevel(skill.Id, i, out var level))
                    foreach (var key in new[] { "scatterCount", "burstCount" })
                        if (level.Parameters.TryGetValue(key, out var value) && TryCount(value, out var configured))
                            count = Math.Max(count, configured);
            return count;
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
                !_context.Battle.Catalog.TryGetBullet(model.BulletId, out var bulletModel)) return false;
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
            if (shotIndex == 0 && reserve.Ready.Count < total)
            {
                reserve.Capacity = Math.Max(reserve.Capacity, total);
                PrepareNextAsync(reserve).Forget();
                if (reserve.Ready.Count < total) return false;
            }
            if (reserve.Ready.Count < count) return false;
            var sourceId = robot.InstanceId;
            var damage = model.Type == SkillType.NormalAttack ? robot.GetAttackDamage() : checked((int)cast.Get("damage"));
            var ready = new GameObject[count];
            for (var i = 0; i < count; i++) ready[i] = reserve.Ready.Dequeue();
            // 闭包只捕获标识、只读配置与预留表现；不捕获池化实体。
            execute = () => FirePrepared(sourceId, cast, damage, bulletModel, ready, burst, angle, maxHits, knockback);
            release = () =>
            {
                foreach (var instance in ready)
                    ReturnPrepared(sourceId, instance);
                Array.Clear(ready, 0, ready.Length);
            };
            return true;
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
                    var state = new BulletState(new BulletLauncher(model, projectile.Action, direction, maxHits), _bulletWorld,
                        (hit, id) => _context.Combat.ResolveProjectileHit(projectile, id));
                    Launch(reserve, instance, state);
                    launched = true;
                }
                finally { if (!launched) ReturnPrepared(sourceId, instance); }
            }
            PrepareNextAsync(reserve).Forget();
        }

        private bool CanEmit(SkillCastSnapshot cast) => IsRunning && _context.Battle.Timelines.IsActive(cast) &&
            _context.Battle.TryResolveOwner(cast.Owner, out var owner) && owner.CanCast;

        private void ReturnPrepared(int sourceId, GameObject instance)
        {
            if (instance == null) return;
            if (OwnsBattle && _reserves.TryGetValue(sourceId, out var reserve) && reserve.Ready.Count < reserve.Capacity)
                reserve.Ready.Enqueue(instance);
            else Release(instance);
        }

        private void Launch(Reserve reserve, GameObject instance, BulletState state)
        {
            var bullet = instance.GetComponent<RobotBulletView>();
            bullet.Bind(state);
            instance.transform.SetParent(_flightRoot, true);
            BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), reserve.Robot.Side);
            _context.Battle.Bullets.Add(state, bullet.Present, () => Release(instance));
            instance.SetActive(true);
        }

        private async UniTaskVoid PrepareNextAsync(Reserve reserve)
        {
            if (reserve.Loading || reserve.Ready.Count >= reserve.Capacity || reserve.Cancellation.IsCancellationRequested || !OwnsBattle) return;
            reserve.Loading = true;
            GameObject instance = null;
            var token = reserve.Cancellation.Token;
            try
            {
                if (!_context.Battle.Catalog.TryGet(reserve.Robot.SkillId, out var skill) ||
                    !_context.Battle.Catalog.TryGetBullet(skill.BulletId, out var model))
                    throw new InvalidOperationException("机器人技能缺少弹丸配置。");
                while (reserve.Ready.Count < reserve.Capacity)
                {
                    instance = await _load(model.Prefab, _preparedRoot, token);
                    token.ThrowIfCancellationRequested();
                    if (!OwnsBattle || !_reserves.TryGetValue(reserve.Robot.InstanceId, out var current) ||
                        !ReferenceEquals(current, reserve) || reserve.Robot.IsRemoved) return;
                    if (instance == null || instance.GetComponent<RobotBulletView>() == null)
                        throw new InvalidOperationException("子弹加载失败或缺少 RobotBulletView。");
                    instance.SetActive(false);
                    reserve.Ready.Enqueue(instance);
                    instance = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (OwnsBattle && !token.IsCancellationRequested)
                {
                    reserve.RetryRemaining = 1f;
                    Debug.LogError("机器人子弹准备失败，1秒战斗时间后重试：" + e.Message, _host);
                }
            }
            finally { Release(instance); reserve.Loading = false; }
        }

        private void RemoveReserve(int id)
        {
            if (!_reserves.TryGetValue(id, out var reserve)) return;
            _reserves.Remove(id);
            _context.Battle.Timelines.Cancel(reserve.Robot.OwnerRef);
            reserve.Robot.ClearCombat();
            reserve.Cancellation.Cancel(); reserve.Cancellation.Dispose();
            while (reserve.Ready.Count > 0) Release(reserve.Ready.Dequeue());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _prepared = false;
            _lifetime.Cancel();
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
