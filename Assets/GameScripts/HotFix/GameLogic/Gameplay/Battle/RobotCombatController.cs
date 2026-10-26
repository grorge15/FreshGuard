using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
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
            internal GameObject Ready;
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
                foreach (var location in tables.TbRobot.DataList.Select(r => r.BulletPrefab).Distinct())
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
            _context.Battle.Timelines.Register(new TimelineModel("robot.single", 0f,
                new[] { new TimelineNode(0f, "robot.bullet") }));
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
                var reserve = new Reserve { Robot = robot, View = view,
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
                if (!reserve.Loading && reserve.Ready == null && reserve.RetryRemaining <= 0f)
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
                !_reserves.TryGetValue(robot.InstanceId, out var reserve) || reserve.Ready == null ||
                !ReferenceEquals(reserve.Robot, robot) || reserve.View == null ||
                reserve.View.InstanceId != robot.InstanceId || !reserve.View.isActiveAndEnabled ||
                !_context.Registry.TryGet(robot.InstanceId, out var current) || !ReferenceEquals(current, robot)) return false;
            var target = _targets.GetTarget(robot.Side);
            if (!target.HasValue || !_enemies.TryGetTargetPosition(robot.Side, target.Value, out var aimPoint)) return false;
            return _context.Battle.Skills.TryCast(new SkillCastRequest(robot.OwnerRef, robot.SkillId,
                SkillTriggerSource.Contact, SkillTarget.Entity(robot.Side, target.Value)),
                () => robot.TryConsumeAttackTrigger(robot.Configuration.MinTriggerInterval)).Accepted;
        }

        private sealed class RobotBulletAction : ISingleUseTimelineAction
        {
            private readonly RobotCombatController _controller;
            internal RobotBulletAction(RobotCombatController controller) { _controller = controller; }
            public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) =>
                _controller.TryPrepareBullet(cast, out execute);
        }

        private bool TryPrepareBullet(SkillCastSnapshot cast, out Action execute)
        {
            execute = null;
            if (!IsRunning || cast.Owner.Kind != SkillOwnerKind.Entity || !cast.Owner.EntityId.HasValue ||
                cast.Target.Kind != SkillTargetKind.Entity || cast.Target.Side != cast.Owner.Side ||
                !_context.Registry.TryGet(cast.Owner.EntityId.Value, out var entity) || !(entity is RobotEntity robot) ||
                !robot.CanParticipate || !_reserves.TryGetValue(robot.InstanceId, out var reserve) ||
                !ReferenceEquals(reserve.Robot, robot) || reserve.Ready == null || reserve.View == null ||
                reserve.View.InstanceId != robot.InstanceId || !reserve.View.isActiveAndEnabled ||
                !_enemies.TryGetTargetPosition(robot.Side, cast.Target.EntityId, out var aimPoint) ||
                !_context.Battle.Catalog.TryGet(cast.SkillId, out var model)) return false;
            var sourceId = robot.InstanceId;
            var damage = model.Type == SkillType.NormalAttack ? robot.GetAttackDamage() : checked((int)cast.Get("damage"));
            var origin = reserve.View.GetAttackOrigin();
            var bulletModel = new BulletModel(robot.Configuration.BulletFlightTime, robot.Configuration.BulletArcHeight);
            var ready = reserve.Ready;
            // 延迟执行只保存实例ID、不可变施法参数和表现槽的版本，不捕获Robot/Enemy引用。
            execute = () => FirePrepared(sourceId, cast, damage, origin, aimPoint, bulletModel, ready);
            return true;
        }

        private void FirePrepared(int sourceId, SkillCastSnapshot cast, int damage, Vector3 origin,
            Vector3 aimPoint, BulletModel model, GameObject ready)
        {
            if (!IsRunning || !_reserves.TryGetValue(sourceId, out var reserve) || reserve.Ready != ready || ready == null) return;
            var action = _context.Combat.CaptureAttack(new CombatActionSnapshot(_context.BattleId,
                sourceId, cast.Owner.Side, cast.SkillId, new[] { cast.Target.EntityId }, CombatOutcomeTag.SingleDamage,
                damage, origin, aimPoint, sequence: 1, hitBuffs: cast.HitBuffs));
            var state = new BulletState(new BulletLauncher(model, action), _bulletWorld,
                hit => _context.Combat.Resolve(hit.Action));
            Launch(reserve, ready.GetComponent<RobotBulletView>(), state);
        }

        private void Launch(Reserve reserve, RobotBulletView bullet, BulletState state)
        {
            var instance = reserve.Ready;
            reserve.Ready = null;
            bullet.Bind(state);
            instance.transform.SetParent(_flightRoot, true);
            _context.Battle.Bullets.Add(state, bullet.Present, () => Release(instance));
            instance.SetActive(true);
            PrepareNextAsync(reserve).Forget();
        }

        private async UniTaskVoid PrepareNextAsync(Reserve reserve)
        {
            if (reserve.Loading || reserve.Ready != null || reserve.Cancellation.IsCancellationRequested || !OwnsBattle) return;
            reserve.Loading = true;
            GameObject instance = null;
            var token = reserve.Cancellation.Token;
            try
            {
                instance = await _load(reserve.Robot.Configuration.BulletPrefab, _preparedRoot, token);
                token.ThrowIfCancellationRequested();
                if (!OwnsBattle || !_reserves.TryGetValue(reserve.Robot.InstanceId, out var current) ||
                    !ReferenceEquals(current, reserve) || reserve.Robot.IsRemoved) return;
                if (instance == null || instance.GetComponent<RobotBulletView>() == null)
                    throw new InvalidOperationException("子弹加载失败或缺少 RobotBulletView。");
                instance.SetActive(false);
                reserve.Ready = instance;
                instance = null;
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
            reserve.Robot.ClearCombat();
            reserve.Cancellation.Cancel(); reserve.Cancellation.Dispose();
            Release(reserve.Ready); reserve.Ready = null;
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
