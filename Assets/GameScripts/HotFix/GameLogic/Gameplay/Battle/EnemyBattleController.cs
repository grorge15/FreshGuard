using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using GameConfig.enemy;
using GameConfig.wave;
using UnityEngine;

namespace GameLogic
{
    /// <summary>双方连续出兵、整局时钟和节点移动；实例身份始终与所属对局一起使用。</summary>
    [DisallowMultipleComponent]
    public sealed partial class EnemyBattleController : MonoBehaviour
    {
        [Header("我方战场路线：按数组顺序移动")]
        [Tooltip("按顺序拖入节点 Transform：Element 0 为出生圆心，Element 1、2 为 P1、P2 圆心。普通怪在各圆内独立选点；精英怪与 Boss 始终经过圆心。同侧 Tower 自动作为终点；下次初始化对局生效。")]
        [SerializeField] private Transform[] _playerRoutePoints;
        [Tooltip("我方普通怪出生圆半径（XY 平面，世界单位）。0 为固定点出生；精英怪与 Boss 固定从圆心出生。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _playerSpawnRadius = 0.5f;
        [Tooltip("我方普通怪 P1（路线 Element 1）圆半径，XY 平面、世界单位。0 为固定节点；精英怪与 Boss 固定经过圆心。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _playerP1Radius = 0.5f;
        [Tooltip("我方普通怪 P2（路线 Element 2）圆半径，XY 平面、世界单位。0 为固定节点；精英怪与 Boss 固定经过圆心。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _playerP2Radius = 0.5f;
        [Header("敌方战场路线：按数组顺序移动")]
        [Tooltip("按顺序拖入节点 Transform：Element 0 为出生圆心，Element 1、2 为 P1、P2 圆心。普通怪在各圆内独立选点；精英怪与 Boss 始终经过圆心。同侧 Tower 自动作为终点；下次初始化对局生效。")]
        [SerializeField] private Transform[] _opponentRoutePoints;
        [Tooltip("敌方普通怪出生圆半径（XY 平面，世界单位）。0 为固定点出生；精英怪与 Boss 固定从圆心出生。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _opponentSpawnRadius = 0.5f;
        [Tooltip("敌方普通怪 P1（路线 Element 1）圆半径，XY 平面、世界单位。0 为固定节点；精英怪与 Boss 固定经过圆心。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _opponentP1Radius = 0.5f;
        [Tooltip("敌方普通怪 P2（路线 Element 2）圆半径，XY 平面、世界单位。0 为固定节点；精英怪与 Boss 固定经过圆心。下次初始化对局生效。")]
        [SerializeField, Min(0f)] private float _opponentP2Radius = 0.5f;
        [Header("敌人生成父节点")]
        [Tooltip("我方战场的 Battle_playerBelt；敌人保留预制体局部缩放并继承该节点比例。留空沿用控制器节点。")]
        [SerializeField] private Transform _playerEnemyRoot;
        [Tooltip("敌方战场的 Battle_enemyBelt；敌人保留预制体局部缩放并继承该节点比例。留空沿用控制器节点。")]
        [SerializeField] private Transform _opponentEnemyRoot;
        [Header("战斗反馈")]
        [Tooltip("机器人命中时显示的世界空间伤害数字预制体。未指定时使用运行时后备样式。")]
        [SerializeField] private GameObject _damageNumberPrefab;
        private readonly List<SpawnGroup> _groups = new List<SpawnGroup>();
        private readonly Vector3[][] _centerRoutes = new Vector3[2][];
        private readonly Dictionary<int, MoveEntry> _active = new Dictionary<int, MoveEntry>();
        private readonly Dictionary<int, DeathEntry> _dying = new Dictionary<int, DeathEntry>();
        // 只保存本局的值快照，死亡表现回收后仍供在途子弹查询。
        private readonly Dictionary<int, (BattleSide Side, Vector3 Position)> _deathPositions =
            new Dictionary<int, (BattleSide Side, Vector3 Position)>();
        private readonly List<DamageNumberView> _damageNumbers = new List<DamageNumberView>();
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private Transform _preparedRoot;
        private bool _prepared;
        private bool _preparing;
        private bool _started;
        private bool _hasAbnormalRemoval;
        private bool _advancing;
        private int _generation;
        private double _elapsed;
        private BattleWaveTimeline _timeline;
        private readonly List<BattleWaveTimeline.SpawnCommand> _dueSpawns = new List<BattleWaveTimeline.SpawnCommand>();

        private sealed class SpawnGroup
        {
            internal BattleSide Side;
            internal BattleWaveTimeline.Group Schedule;
            internal WaveSpawn Rule => Schedule.Rule;
            internal Enemy Config => Schedule.Config;
            internal Vector3[] Points;
            internal float[] CumulativeLengths;
            internal float[] PointRadii;
            internal int TowerId;
            internal int Spawned;
            internal readonly Queue<GameObject> Pending = new Queue<GameObject>();
        }

        private sealed class MoveEntry
        {
            internal SpawnGroup Group;
            internal EnemyView View;
            internal Vector3[] Points;
            internal float[] CumulativeLengths;
            internal int NextPoint = 1;
            internal BattleRouteGeometry Geometry;
        }

        private sealed class DeathEntry
        {
            internal EnemyView View;
            internal float Remaining = 3f;
        }

        public int LiveCount => _active.Count;
        public int DeathViewCount => _dying.Count;
        public double ElapsedSeconds => _elapsed;
        public int CurrentWave => _timeline?.CurrentWave ?? 1;
        public bool IsAllSpawnsCompleted => _started && _timeline != null && _timeline.IsAllSpawnsCompleted;
        public bool IsAllEnemiesCleared => IsAllSpawnsCompleted && _active.Count == 0;
        // 时间线指令已发完不等于双方都已实际生成；清理和异常丢失也不能算正常通关。
        public bool IsWaveVictoryReady => IsRunning && !_advancing && !_hasAbnormalRemoval && IsAllEnemiesCleared &&
            _dying.Count == 0 && _groups.All(g => g.Spawned == g.Rule.EnemyCount);
        private bool OwnsBattle => this != null && _host != null && _context != null &&
            _host.Context == _context && !_context.IsEnded && !_context.IsEndRequested;
        private bool IsRunning => _prepared && _started && OwnsBattle && _host.IsCombatRunning;
        internal bool OwnsContext(BattleShopContext context) => context == _context && OwnsBattle;

        public int GetSpawnedCount(BattleSide side)
        {
            EntityValidation.RequireSide(side);
            return _groups.Where(g => g.Side == side).Sum(g => g.Spawned);
        }

        public UniTask PrepareAsync(BattleShopSceneController host, BattleShopContext context,
            Tables tables, CancellationToken token) => PrepareAsync(host, context, tables, token, LoadViewAsync);

        private static UniTask<GameObject> LoadViewAsync(string location, Transform parent, CancellationToken token)
        {
            if (!GameModule.Resource.CheckLocationValid(location))
                throw new InvalidOperationException("敌人预制体未加入 YooAsset 收集：" + location);
            return GameModule.Resource.LoadGameObjectAsync(location, parent, token);
        }

        // 加载边界允许 Editor 验证复用相同的准备、生成与清理流程。
        internal async UniTask PrepareAsync(BattleShopSceneController host, BattleShopContext context,
            Tables tables, CancellationToken token,
            Func<string, Transform, CancellationToken, UniTask<GameObject>> load)
        {
            if (_prepared && _context == context && OwnsBattle) return;
            if (_preparing) throw new InvalidOperationException("敌人资源正在准备中。");
            Clear();
            if (host == null || context == null || tables == null || load == null)
                throw new ArgumentException("敌人准备缺少场景、对局、配置或加载器。");
            _host = host;
            _context = context;
            InitializeIsolation(load, token);
            context.Combat.DamageApplying += OnDamageApplying;
            context.Combat.TargetResolved += OnTargetResolved;
            context.Combat.KnockbackRequested += OnKnockbackRequested;
            _preparing = true;
            var generation = _generation;
            GameObject pending = null;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!OwnsBattle) throw new OperationCanceledException(token);
                _timeline = BattleWaveTimeline.FromTables(tables);
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    if (!host.TryGetTowerTarget(side, out var towerId, out var tower))
                        throw new InvalidOperationException("当前战场缺少有效 Tower：" + side);
                    var authored = side == BattleSide.Player ? _playerRoutePoints : _opponentRoutePoints;
                    var points = ReadRoute(authored, tower);
                    _centerRoutes[(int)side] = points;
                    var radii = (side == BattleSide.Player
                        ? new[] { _playerSpawnRadius, _playerP1Radius, _playerP2Radius }
                        : new[] { _opponentSpawnRadius, _opponentP1Radius, _opponentP2Radius })
                        .Select(ClampRouteRadius).ToArray();
                    foreach (var schedule in _timeline.Groups)
                    {
                        var lengths = CalculateRouteLengths(points);
                        // EnemyType：1 普通、2 精英、3 Boss；只有普通怪参与圆内随机采样。
                        _groups.Add(new SpawnGroup { Side = side, Schedule = schedule, Points = points,
                            CumulativeLengths = lengths, TowerId = towerId,
                            PointRadii = schedule.Config.EnemyType == 1 ? radii : Array.Empty<float>() });
                    }
                }
                var root = new GameObject("PreparedEnemyViews");
                root.transform.SetParent(transform, false);
                root.SetActive(false);
                _preparedRoot = root.transform;
                foreach (var group in _groups.ToArray())
                    for (var i = 0; i < group.Rule.EnemyCount; i++)
                    {
                        pending = await load(group.Config.EnemyPrefab, root.transform, token);
                        token.ThrowIfCancellationRequested();
                        if (generation != _generation || !OwnsBattle) throw new OperationCanceledException(token);
                        var view = pending != null ? pending.GetComponent<EnemyView>() : null;
                        if (view == null || view.ConfigId != group.Config.Id)
                            throw new InvalidOperationException("敌人 Prefab 的 EnemyView 配置不匹配：" + group.Config.Id);
                        pending.SetActive(false);
                        group.Pending.Enqueue(pending);
                        pending = null;
                    }
                _prepared = true;
            }
            catch
            {
                Release(pending);
                if (generation == _generation) Clear();
                throw;
            }
            finally { if (generation == _generation) _preparing = false; }
        }

        private static Vector3[] ReadRoute(Transform[] authored, Transform tower)
        {
            if (authored == null || authored.Length == 0 || authored.Any(p => p == null))
                throw new InvalidOperationException("请配置双方路线节点，首节点为出生点。");
            var contactPosition = tower.position;
            var collider = tower.GetComponent<Collider2D>();
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
            {
                Physics2D.SyncTransforms();
                var contact = collider.ClosestPoint(authored[authored.Length - 1].position);
                contactPosition = new Vector3(contact.x, contact.y, tower.position.z);
            }
            var points = authored.Select(p => p.position).Concat(new[] { contactPosition }).ToArray();
            if (points.Any(p => !Finite(p.x) || !Finite(p.y) || !Finite(p.z)))
                throw new InvalidOperationException("路线节点位置必须为有限数值。");
            return points;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float[] CalculateRouteLengths(Vector3[] points)
        {
            var lengths = new float[points.Length];
            for (var p = 1; p < points.Length; p++)
                lengths[p] = lengths[p - 1] + Vector3.Distance(points[p - 1], points[p]);
            if (lengths.Any(v => !Finite(v))) throw new InvalidOperationException("路线长度必须为有限数值。");
            return lengths;
        }

        private static float ClampRouteRadius(float radius) => Finite(radius) ? Mathf.Max(0f, radius) : 0f;

        private void OnValidate()
        {
            _playerSpawnRadius = ClampRouteRadius(_playerSpawnRadius);
            _playerP1Radius = ClampRouteRadius(_playerP1Radius);
            _playerP2Radius = ClampRouteRadius(_playerP2Radius);
            _opponentSpawnRadius = ClampRouteRadius(_opponentSpawnRadius);
            _opponentP1Radius = ClampRouteRadius(_opponentP1Radius);
            _opponentP2Radius = ClampRouteRadius(_opponentP2Radius);
        }

        private void OnDrawGizmos()
        {
            var previousColor = Gizmos.color;
            DrawRouteCircle(_playerRoutePoints, 0, _playerSpawnRadius, Color.cyan);
            DrawRouteCircle(_playerRoutePoints, 1, _playerP1Radius, Color.cyan);
            DrawRouteCircle(_playerRoutePoints, 2, _playerP2Radius, Color.cyan);
            var opponentColor = new Color(1f, 0.4f, 0.2f);
            DrawRouteCircle(_opponentRoutePoints, 0, _opponentSpawnRadius, opponentColor);
            DrawRouteCircle(_opponentRoutePoints, 1, _opponentP1Radius, opponentColor);
            DrawRouteCircle(_opponentRoutePoints, 2, _opponentP2Radius, opponentColor);
            Gizmos.color = previousColor;
        }

        private static void DrawRouteCircle(Transform[] route, int pointIndex, float radius, Color color)
        {
            if (route == null || pointIndex >= route.Length || route[pointIndex] == null) return;
            var center = route[pointIndex].position;
            radius = ClampRouteRadius(radius);
            Gizmos.color = color;
            Gizmos.DrawLine(center - Vector3.right * 0.08f, center + Vector3.right * 0.08f);
            Gizmos.DrawLine(center - Vector3.up * 0.08f, center + Vector3.up * 0.08f);
            const int SEGMENTS = 64;
            var previous = center + Vector3.right * radius;
            for (var i = 1; i <= SEGMENTS; i++)
            {
                var angle = i * Mathf.PI * 2f / SEGMENTS;
                var next = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        public void StartBattle()
        {
            if (_started) return;
            if (!_prepared || !OwnsBattle) throw new InvalidOperationException("战斗出兵资源尚未准备完成。");
            if (!_host.IsCombatRunning) return;
            _started = true;
            _elapsed = 0d;
            try { SpawnDue(); }
            catch { AbortBattle(); throw; }
        }

        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning) return;
            _advancing = true;
            try
            {
                AdvanceDamageNumbers(deltaTime);
                AdvanceDeathViews(deltaTime);
                if (!IsRunning) return;
                var target = _elapsed + deltaTime;
                do
                {
                    var next = Math.Min(target, _timeline.NextEventTime);
                    // 抵塔也是时间边界，不能由集合遍历顺序决定双方先败顺序。
                    next = Math.Min(next, _elapsed + NextArrivalDelay());
                    next = Math.Min(next, _elapsed + NextBuffDelay());
                    next = Math.Min(next, _elapsed + (_staffs?.TimeUntilBoundary ?? float.PositiveInfinity));
                    next = Math.Max(_elapsed, next);
                    var step = (float)Math.Max(0d, next - _elapsed);
                    _elapsed = next;
                    _staffs?.BeginStep(step);
                    MoveAll(step);
                    _staffs?.EndStep();
                    AdvanceEnemyBuffs(step);
                    SyncIsolationEffects();
                    if (!IsRunning) return;
                    SpawnDue();
                    if (!IsRunning) return;
                } while (_elapsed < target);
            }
            catch { AbortBattle(); throw; }
            finally { _advancing = false; }
        }

        private double NextArrivalDelay()
        {
            var earliest = double.PositiveInfinity;
            foreach (var pair in _active)
            {
                var entry = pair.Value;
                if (entry.View == null || !_context.Registry.TryGet(pair.Key, out var entity) ||
                    !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                if (enemy.IsIsolated) continue;
                var position = entry.View.transform.position;
                var remaining = 0d;
                for (var i = entry.NextPoint; i < entry.Points.Length; i++)
                {
                    remaining += Vector3.Distance(position, entry.Points[i]);
                    position = entry.Points[i];
                }
                if (remaining <= 0.00001d)
                {
                    // 真正被障碍卡在终点前时等待Staff边界，不能不断生成零时步。
                    if (IsStaffBlockingArrival(entry, enemy)) continue;
                    return 0d;
                }
                if (enemy.MoveSpeed > 0f) earliest = Math.Min(earliest, remaining / enemy.MoveSpeed);
            }
            return earliest;
        }

        private double NextBuffDelay()
        {
            var earliest = double.PositiveInfinity;
            foreach (var id in _active.Keys)
                if (_context.Registry.TryGet(id, out var entity) && entity is EnemyEntity enemy && enemy.IsAlive)
                    earliest = Math.Min(earliest, enemy.Buffs.NextBoundary);
            return earliest;
        }

        private void AdvanceEnemyBuffs(float deltaTime)
        {
            foreach (var id in _active.Keys.OrderBy(id => id).ToArray())
            {
                if (!OwnsBattle) return;
                if (_context.Registry.TryGet(id, out var entity) && entity is EnemyEntity enemy && enemy.IsAlive)
                    enemy.Buffs.Advance(deltaTime);
            }
        }

        private void SpawnDue()
        {
            _timeline.CollectDue(_elapsed, _dueSpawns);
            foreach (var command in _dueSpawns)
            {
                foreach (var group in _groups)
                {
                    if (!IsRunning) return;
                    if (group.Schedule != command.Group) continue;
                    var context = _context;
                    var instance = group.Pending.Dequeue();
                    var instanceId = 0;
                    try
                    {
                        instanceId = context.Registry.CreateEnemyFromConfig(group.Config, group.Side,
                            group.Schedule.Cycle, group.Schedule.Wave, _timeline.EnemyHpGrowthPerWave);
                        var parent = group.Side == BattleSide.Player ? _playerEnemyRoot : _opponentEnemyRoot;
                        var prefabScale = instance.transform.localScale;
                        instance.transform.SetParent(parent != null ? parent : transform, true);
                        // 保留预制体自身比例，让同侧 Belt 的缩放作用于首批和后续出兵。
                        if (parent != null) instance.transform.localScale = prefabScale;
                        var points = group.Points;
                        var lengths = group.CumulativeLengths;
                        // 只采样人工配置的节点，不随机化自动追加的塔终点。
                        for (var p = 0; p < group.PointRadii.Length && p < points.Length - 1; p++)
                        {
                            if (group.PointRadii[p] <= 0f) continue;
                            if (points == group.Points) points = (Vector3[])points.Clone();
                            var offset = UnityEngine.Random.insideUnitCircle * group.PointRadii[p];
                            points[p] += new Vector3(offset.x, offset.y, 0f);
                        }
                        // 每只怪物保留整条采样路线，移动与击退共用，不在抵达或击退后重新随机。
                        if (points != group.Points) lengths = CalculateRouteLengths(points);
                        instance.transform.position = points[0];
                        BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), group.Side);
                        instance.SetActive(true);
                        var view = instance.GetComponent<EnemyView>();
                        view.Bind(context.Registry, instanceId);
                        view.FaceMovement(points[1] - points[0]);
                        view.SetMoving(true);
                        _active.Add(instanceId, new MoveEntry { Group = group, View = view,
                            Points = points, CumulativeLengths = lengths });
                        group.Spawned++;
                    }
                    catch
                    {
                        if (instanceId > 0) context.Registry.Remove(instanceId, EntityRemovalReason.Manual, out _);
                        Release(instance);
                        throw;
                    }
                }
            }
        }

        public IReadOnlyList<BulletTarget> GetBulletTargets(BattleSide side)
        {
            var targets = new List<BulletTarget>();
            if (!OwnsBattle) return targets;
            foreach (var pair in _active)
                if (pair.Value.Group.Side == side && pair.Value.View != null &&
                    _context.Registry.TryGet(pair.Key, out var entity) && entity is EnemyEntity enemy && enemy.IsAlive)
                    targets.Add(new BulletTarget(pair.Key, pair.Value.View.transform.position, enemy.Configuration?.HitRadius ?? 0.3f));
            return targets;
        }

        public bool TryKnockback(BattleSide side, int instanceId, float distance)
        {
            EntityValidation.RequireFiniteNonNegative(distance, nameof(distance));
            if (!OwnsBattle || !_active.TryGetValue(instanceId, out var entry) || entry.Group.Side != side ||
                entry.View == null || !_context.Registry.TryGet(instanceId, out var entity) ||
                !(entity is EnemyEntity enemy) || !enemy.IsAlive) return false;
            var received = Mathf.Clamp01(enemy.Configuration?.KnockbackResist ?? 1f);
            var progress = Mathf.Max(0f, enemy.RouteProgress - distance * received);
            var points = entry.Points;
            var lengths = entry.CumulativeLengths;
            var next = 1;
            while (next < lengths.Length - 1 && lengths[next] <= progress) next++;
            var segmentLength = lengths[next] - lengths[next - 1];
            var u = segmentLength <= 0f ? 0f : (progress - lengths[next - 1]) / segmentLength;
            entry.NextPoint = next;
            entry.View.transform.position = Vector3.Lerp(points[next - 1], points[next], u);
            entry.View.FaceMovement(points[next] - points[next - 1]);
            enemy.TrySetRouteProgress(progress);
            SyncIsolationEffects();
            return true;
        }

        private void OnKnockbackRequested(BattleSide side, int instanceId, float distance)
            => TryKnockback(side, instanceId, distance);

        private void MoveAll(float deltaTime)
        {
            var arrivals = new List<int>();
            foreach (var id in _active.Keys.OrderBy(id => id).ToArray())
            {
                if (!IsRunning) return;
                if (!_active.TryGetValue(id, out var entry)) continue;
                if (entry.View == null || !_context.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity enemy) || !enemy.IsAlive)
                {
                    _hasAbnormalRemoval = true;
                    _active.Remove(id);
                    if (entry.View != null) Release(entry.View.gameObject);
                    // 表现意外被销毁时不能留下永远无法抵达的逻辑实体。
                    _context.Registry.Remove(id, EntityRemovalReason.Manual, out _);
                    continue;
                }
                if (_staffs != null && _staffs.Count > 0)
                {
                    if (MoveWithStaffs(entry, enemy, deltaTime)) arrivals.Add(id);
                    continue;
                }
                entry.View.SetMoving(!enemy.IsIsolated);
                if (enemy.IsIsolated) continue;
                var distance = enemy.MoveSpeed * deltaTime;
                var position = entry.View.transform.position;
                var previousPosition = position;
                var points = entry.Points;
                while (entry.NextPoint < points.Length)
                {
                    var offset = points[entry.NextPoint] - position;
                    var length = offset.magnitude;
                    if (length > distance + 0.00001f)
                    {
                        position += offset / length * distance;
                        break;
                    }
                    position = points[entry.NextPoint++];
                    distance = Mathf.Max(0f, distance - length);
                }
                entry.View.transform.position = position;
                entry.View.FaceMovement(entry.NextPoint < points.Length
                    ? points[entry.NextPoint] - position : position - previousPosition);
                var lengths = entry.CumulativeLengths;
                var progress = entry.NextPoint == points.Length ? lengths[lengths.Length - 1] :
                    lengths[entry.NextPoint] - Vector3.Distance(position, points[entry.NextPoint]);
                enemy.TrySetRouteProgress(Mathf.Clamp(progress, 0f, lengths[lengths.Length - 1]));
                if (entry.NextPoint == points.Length) arrivals.Add(id);
            }
            // 同一抵塔时刻一次提交双方伤害，不能由敌人实例 ID 的顺序决定胜负。
            ArriveBatch(arrivals);
        }

        private void ArriveBatch(List<int> arrivals)
        {
            if (arrivals.Count == 0 || !IsRunning) return;
            var context = _context;
            var host = _host;
            var committed = new List<(MoveEntry entry, EntityRemovalSnapshot snapshot)>();
            var playerDamage = 0;
            var opponentDamage = 0;
            foreach (var id in arrivals)
            {
                if (!_active.TryGetValue(id, out var entry) || !context.Registry.TryGet(id, out var entity) ||
                    !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                var side = entry.Group.Side;
                var leakDamage = enemy.Configuration.LeakDamage;
                var validTarget = host.TryGetTowerTarget(side, out var targetId, out _) && targetId == entry.Group.TowerId;
                if (!context.Registry.Remove(id, EntityRemovalReason.Leaked, out var snapshot)) continue;
                _active.Remove(id);
                committed.Add((entry, snapshot));
                if (!validTarget) continue;
                if (side == BattleSide.Player) playerDamage = (int)Math.Min(int.MaxValue, (long)playerDamage + leakDamage);
                else opponentDamage = (int)Math.Min(int.MaxValue, (long)opponentDamage + leakDamage);
            }
            try
            {
                if (OwnsBattle) host.ApplyTowerDamageBatch(context.BattleId, playerDamage, opponentDamage);
            }
            finally
            {
                // 已经提交的抵塔表现都必须释放；某个表现回调抛异常也不能遗留其余对象。
                Exception failure = null;
                foreach (var arrival in committed)
                {
                    if (arrival.entry.View == null) continue;
                    var instance = arrival.entry.View.gameObject;
                    try { arrival.entry.View.PlayRemoval(arrival.snapshot); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
                    finally { Release(instance); }
                }
                if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private void OnDamageApplying(CombatDamageContext context)
        {
            if (OwnsBattle && context.Action.BattleId == _context.BattleId)
                TryPlayDamageFeedback(context.Target.Side, context.Target.EntityId.Value, context.Outcome.GetFinalDamage());
        }

        private void OnTargetResolved(CombatDamageContext context)
        {
            if (OwnsBattle && context.Action.BattleId == _context.BattleId && context.Result.IsKilled)
                NotifyKilled(context.Removal);
        }

        /// <summary>只提交受击表现；逻辑伤害由CombatRuntime统一提交。</summary>
        public bool TryPlayDamageFeedback(BattleSide side, int instanceId, int damage)
        {
            if (!OwnsBattle || damage <= 0 || !_active.TryGetValue(instanceId, out var entry) ||
                entry.Group.Side != side || entry.View == null || !entry.View.PlayHitFeedback()) return false;
            var bounds = entry.View.GetVisualBounds();
            var outward = Vector2.left;
            if (_host.TryGetBoardCenter(side, out var boardCenter))
            {
                var offset = (Vector2)(bounds.center - boardCenter);
                if (offset.sqrMagnitude > 0.0001f) outward = offset.normalized;
            }
            var extent = Mathf.Abs(outward.x) * bounds.extents.x + Mathf.Abs(outward.y) * bounds.extents.y;
            var start = bounds.center + (Vector3)outward * Mathf.Min(0.18f, extent * 0.35f);
            if (_damageNumberPrefab != null)
            {
                var instance = Instantiate(_damageNumberPrefab, start, Quaternion.identity, transform);
                var number = instance.GetComponent<DamageNumberView>();
                if (number == null) number = instance.AddComponent<DamageNumberView>();
                number.Initialize(damage, start, outward);
                BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), side);
                _damageNumbers.Add(number);
            }
            else
            {
                var instance = new GameObject("DamageNumber");
                instance.transform.SetParent(transform, true);
                var number = instance.AddComponent<DamageNumberView>();
                number.Initialize(damage, start, outward);
                BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), side);
                _damageNumbers.Add(number);
            }
            return true;
        }

        private void AdvanceDamageNumbers(float deltaTime)
        {
            foreach (var number in _damageNumbers.ToArray())
            {
                if (number == null || number.Advance(deltaTime))
                {
                    _damageNumbers.Remove(number);
                    if (number != null) Release(number.gameObject);
                }
            }
        }

        public void SetFrozen(bool frozen)
        {
            foreach (var entry in _active.Values)
                if (entry.View != null) entry.View.GetComponent<Animator>().speed = frozen ? 0f : 1f;
            foreach (var entry in _dying.Values)
                if (entry.View != null) entry.View.GetComponent<Animator>().speed = frozen ? 0f : 1f;
        }

        /// <summary>只提供当前对局正在移动的存活敌人；不会改变移动状态。</summary>
        public void CopyTargetCandidates(BattleSide side, List<int> destination)
        {
            EntityValidation.RequireSide(side);
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!OwnsBattle) return;
            foreach (var pair in _active)
                if (TryGetTargetPosition(side, pair.Key, out _)) destination.Add(pair.Key);
        }

        public IReadOnlyList<Vector3> GetJuiceDropPositions(BattleSide side, Vector3 target, int count, float spacing)
        {
            EntityValidation.RequireSide(side);
            return OwnsBattle ? BattleRouteSampler.SampleDrops(_centerRoutes[(int)side], target, count, spacing) : Array.Empty<Vector3>();
        }

        public bool TryGetTargetPosition(BattleSide side, int instanceId, out Vector3 position)
        {
            position = default;
            if (!OwnsBattle || !_active.TryGetValue(instanceId, out var entry) || entry.Group.Side != side ||
                entry.View == null || !entry.View.isActiveAndEnabled || entry.View.InstanceId != instanceId ||
                !_context.Registry.TryGet(instanceId, out var entity) || !(entity is EnemyEntity enemy) ||
                !enemy.IsAlive || enemy.Side != side) return false;
            position = entry.View.transform.position;
            return true;
        }

        internal bool TryGetDeathPosition(BattleSide side, int instanceId, out Vector3 position)
        {
            position = default;
            if (!OwnsBattle || !_deathPositions.TryGetValue(instanceId, out var death) || death.Side != side) return false;
            position = death.Position;
            return true;
        }

        /// <summary>击杀回调顺序可能先于死亡动画；两种状态都按实例身份读取位置。</summary>
        internal Vector3? GetRewardOrigin(BattleSide side, int instanceId)
        {
            if (!OwnsBattle) return null;
            if (TryGetDeathPosition(side, instanceId, out var position)) return position;
            if (_active.TryGetValue(instanceId, out var entry) && entry.Group.Side == side &&
                entry.View != null && entry.View.InstanceId == instanceId)
                return entry.View.transform.position;
            return null;
        }

        /// <summary>逻辑实体已回收，死亡动画单独保留，不能再抵塔或被索敌。</summary>
        public void NotifyKilled(EntityRemovalSnapshot snapshot)
        {
            if (!OwnsBattle || snapshot.RemovalReason != EntityRemovalReason.Killed || snapshot.Kind != EntityKind.Enemy ||
                _context.Registry.TryGet(snapshot.InstanceId, out _) ||
                !_active.TryGetValue(snapshot.InstanceId, out var entry) || entry.Group.Side != snapshot.Side) return;
            ReleaseIsolationEffect(snapshot.InstanceId);
            _active.Remove(snapshot.InstanceId);
            if (entry.View == null) return;
            _deathPositions[snapshot.InstanceId] = (snapshot.Side, entry.View.transform.position);
            _dying.Add(snapshot.InstanceId, new DeathEntry { View = entry.View });
            entry.View.RemovalAnimationCompleted += OnDeathCompleted;
            try
            {
                if (!entry.View.PlayRemoval(snapshot)) ReleaseDeathView(snapshot.InstanceId);
            }
            catch { ReleaseDeathView(snapshot.InstanceId); throw; }
        }

        private void OnDeathCompleted(int id) => ReleaseDeathView(id);

        private void AdvanceDeathViews(float deltaTime)
        {
            foreach (var id in _dying.Keys.ToArray())
            {
                if (!IsRunning) return;
                if (!_dying.TryGetValue(id, out var entry)) continue;
                entry.Remaining -= deltaTime;
                if (entry.View == null || !entry.View.isActiveAndEnabled || entry.Remaining <= 0f) ReleaseDeathView(id);
            }
        }

        private void ReleaseDeathView(int id)
        {
            if (!_dying.TryGetValue(id, out var entry)) return;
            _dying.Remove(id);
            if (entry.View == null) return;
            entry.View.RemovalAnimationCompleted -= OnDeathCompleted;
            Release(entry.View.gameObject);
        }

        private void AbortBattle()
        {
            var host = _host;
            Clear();
            if (host != null) host.EndBattle();
        }

        public void Clear()
        {
            _staffs?.Dispose(); _staffs = null;
            _generation++;
            ClearIsolation();
            _prepared = _preparing = _started = false;
            _hasAbnormalRemoval = false;
            _elapsed = 0d;
            _timeline = null;
            _dueSpawns.Clear();
            var context = _context;
            _context = null;
            if (context != null)
            {
                context.Combat.DamageApplying -= OnDamageApplying;
                context.Combat.TargetResolved -= OnTargetResolved;
                context.Combat.KnockbackRequested -= OnKnockbackRequested;
            }
            _host = null;
            foreach (var pair in _active)
            {
                context?.Registry.Remove(pair.Key, EntityRemovalReason.BattleEnded, out _);
                if (pair.Value.View != null) Release(pair.Value.View.gameObject);
            }
            _active.Clear();
            _deathPositions.Clear();
            foreach (var id in _dying.Keys.ToArray()) ReleaseDeathView(id);
            foreach (var number in _damageNumbers)
                if (number != null) Release(number.gameObject);
            _damageNumbers.Clear();
            foreach (var group in _groups)
                while (group.Pending.Count > 0) Release(group.Pending.Dequeue());
            _groups.Clear();
            if (_preparedRoot != null) Release(_preparedRoot.gameObject);
            _preparedRoot = null;
        }

        private static void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.GetComponent<EnemyView>()?.Unbind();
            instance.SetActive(false);
            // 开战前准备的视图可能从未激活，不能只依赖 OnDestroy 归还资源引用。
            instance.GetComponent<TEngine.AssetsReference>()?.ReleaseReferences();
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }

        private void OnDestroy() { Clear(); }
    }
}
