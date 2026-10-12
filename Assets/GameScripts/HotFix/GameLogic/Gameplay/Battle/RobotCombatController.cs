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
        private readonly List<RobotBulletView> _bullets = new List<RobotBulletView>();
        private readonly Func<string, Transform, CancellationToken, UniTask<GameObject>> _load;
        private readonly Func<RobotEntity, bool> _tryFire;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Transform _preparedRoot;
        private readonly Transform _flightRoot;
        private bool _prepared;
        private bool _disposed;
        private long _sequence;

        private sealed class Reserve
        {
            internal RobotEntity Robot;
            internal RobotView View;
            internal GameObject Ready;
            internal bool Loading;
            internal float RetryRemaining;
            internal CancellationTokenSource Cancellation;
        }

        public int LiveBulletCount => _bullets.Count;
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
            _tryFire = TryFire;
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
            foreach (var bullet in _bullets.ToArray())
            {
                if (!IsRunning) return;
                var completed = bullet == null || bullet.Advance(deltaTime);
                if (_disposed) return;
                if (completed)
                {
                    _bullets.Remove(bullet);
                    if (bullet != null) Release(bullet.gameObject);
                }
            }
        }

        public void AdvanceAttacks(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning) return;
            SyncRobots();
            var player = _targets.GetTarget(BattleSide.Player);
            var opponent = _targets.GetTarget(BattleSide.Opponent);
            foreach (var pair in _reserves.OrderBy(p => p.Key).ToArray())
            {
                if (!IsRunning) return;
                var reserve = pair.Value;
                reserve.RetryRemaining = Mathf.Max(0f, reserve.RetryRemaining - deltaTime);
                if (!reserve.Loading && reserve.Ready == null && reserve.RetryRemaining <= 0f)
                    PrepareNextAsync(reserve).Forget();
                var hasTarget = reserve.Robot.Side == BattleSide.Player ? player.HasValue : opponent.HasValue;
                reserve.Robot.TickCombat(hasTarget && reserve.View != null && reserve.View.isActiveAndEnabled, _tryFire);
            }
        }

        private bool TryFire(RobotEntity robot)
        {
            if (!IsRunning || !robot.CanParticipate || robot.TriggerCooldownRemaining > 0f ||
                !_reserves.TryGetValue(robot.InstanceId, out var reserve) || reserve.Ready == null ||
                reserve.View == null || !reserve.View.isActiveAndEnabled ||
                !_context.Registry.TryGet(robot.InstanceId, out var current) || !ReferenceEquals(current, robot)) return false;
            var target = _targets.GetTarget(robot.Side);
            if (!target.HasValue || !_enemies.TryGetTargetPosition(robot.Side, target.Value, out _)) return false;
            var bullet = reserve.Ready.GetComponent<RobotBulletView>();
            var config = robot.Configuration;
            bullet.Initialize(_host, _context, _enemies, robot.Side, target.Value, robot.GetAttackDamage(),
                reserve.View.GetAttackOrigin(), config.BulletFlightTime, config.BulletArcHeight, checked(++_sequence));
            if (!robot.TryConsumeAttackTrigger(config.AttackInterval)) return false;
            var instance = reserve.Ready;
            reserve.Ready = null;
            instance.transform.SetParent(_flightRoot, true);
            _bullets.Add(bullet);
            instance.SetActive(true);
            PrepareNextAsync(reserve).Forget();
            return true;
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
            foreach (var bullet in _bullets) if (bullet != null) Release(bullet.gameObject);
            _bullets.Clear();
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
