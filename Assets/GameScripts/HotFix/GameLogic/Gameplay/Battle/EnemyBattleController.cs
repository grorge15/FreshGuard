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
    public sealed class EnemyBattleController : MonoBehaviour
    {
        [Header("我方战场路线：按数组顺序移动")]
        [Tooltip("把自行摆放的节点 Transform 按顺序拖入。Element 0 为出生点；同侧 Tower 自动作为终点，不必填入。修改在下一次初始化对局时生效。")]
        [SerializeField] private Transform[] _playerRoutePoints;
        [Header("敌方战场路线：按数组顺序移动")]
        [Tooltip("把自行摆放的节点 Transform 按顺序拖入。Element 0 为出生点；同侧 Tower 自动作为终点，不必填入。修改在下一次初始化对局时生效。")]
        [SerializeField] private Transform[] _opponentRoutePoints;
        [Header("战斗反馈")]
        [Tooltip("机器人命中时显示的世界空间伤害数字预制体。未指定时使用运行时后备样式。")]
        [SerializeField] private GameObject _damageNumberPrefab;
        private readonly List<SpawnGroup> _groups = new List<SpawnGroup>();
        private readonly Dictionary<int, MoveEntry> _active = new Dictionary<int, MoveEntry>();
        private readonly Dictionary<int, DeathEntry> _dying = new Dictionary<int, DeathEntry>();
        private readonly List<DamageNumberView> _damageNumbers = new List<DamageNumberView>();
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private Transform _preparedRoot;
        private bool _prepared;
        private bool _preparing;
        private bool _started;
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
            internal int TowerId;
            internal int Spawned;
            internal readonly Queue<GameObject> Pending = new Queue<GameObject>();
        }

        private sealed class MoveEntry
        {
            internal SpawnGroup Group;
            internal EnemyView View;
            internal int NextPoint = 1;
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
                    foreach (var schedule in _timeline.Groups)
                    {
                        var lengths = new float[points.Length];
                        for (var p = 1; p < points.Length; p++)
                            lengths[p] = lengths[p - 1] + Vector3.Distance(points[p - 1], points[p]);
                        if (lengths.Any(v => !Finite(v))) throw new InvalidOperationException("路线长度必须为有限数值。");
                        _groups.Add(new SpawnGroup { Side = side, Schedule = schedule, Points = points,
                            CumulativeLengths = lengths, TowerId = towerId });
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
            AdvanceDamageNumbers(deltaTime);
            AdvanceDeathViews(deltaTime);
            if (!IsRunning) return;
            try
            {
                var target = _elapsed + deltaTime;
                do
                {
                    var next = Math.Min(target, _timeline.NextEventTime);
                    // 抵塔也是时间边界，不能由集合遍历顺序决定双方先败顺序。
                    next = Math.Min(next, _elapsed + NextArrivalDelay());
                    next = Math.Max(_elapsed, next);
                    var step = (float)Math.Max(0d, next - _elapsed);
                    _elapsed = next;
                    MoveAll(step);
                    if (!IsRunning) return;
                    SpawnDue();
                    if (!IsRunning) return;
                } while (_elapsed < target);
            }
            catch { AbortBattle(); throw; }
        }

        private double NextArrivalDelay()
        {
            var earliest = double.PositiveInfinity;
            foreach (var pair in _active)
            {
                var entry = pair.Value;
                if (entry.View == null || !_context.Registry.TryGet(pair.Key, out var entity) ||
                    !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                var position = entry.View.transform.position;
                var remaining = 0d;
                for (var i = entry.NextPoint; i < entry.Group.Points.Length; i++)
                {
                    remaining += Vector3.Distance(position, entry.Group.Points[i]);
                    position = entry.Group.Points[i];
                }
                if (remaining <= 0.00001d) return 0d;
                if (enemy.EffectiveSpeed > 0f) earliest = Math.Min(earliest, remaining / enemy.EffectiveSpeed);
            }
            return earliest;
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
                        instanceId = context.Registry.CreateEnemyFromConfig(group.Config, group.Side, group.Schedule.Cycle);
                        instance.transform.SetParent(transform, true);
                        instance.transform.position = group.Points[0];
                        instance.SetActive(true);
                        var view = instance.GetComponent<EnemyView>();
                        view.Bind(context.Registry, instanceId);
                        view.SetMoving(true);
                        _active.Add(instanceId, new MoveEntry { Group = group, View = view });
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

        private void MoveAll(float deltaTime)
        {
            var arrivals = new List<int>();
            foreach (var id in _active.Keys.OrderBy(id => id).ToArray())
            {
                if (!IsRunning) return;
                if (!_active.TryGetValue(id, out var entry)) continue;
                if (entry.View == null || !_context.Registry.TryGet(id, out var entity) || !(entity is EnemyEntity enemy) || !enemy.IsAlive)
                {
                    _active.Remove(id);
                    if (entry.View != null) Release(entry.View.gameObject);
                    // 表现意外被销毁时不能留下永远无法抵达的逻辑实体。
                    _context.Registry.Remove(id, EntityRemovalReason.Manual, out _);
                    continue;
                }
                var distance = enemy.EffectiveSpeed * deltaTime;
                var position = entry.View.transform.position;
                var points = entry.Group.Points;
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
                var lengths = entry.Group.CumulativeLengths;
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

        /// <summary>子弹命中后统一提交受击表现，逻辑伤害仍由调用方提交。</summary>
        public bool TryPlayDamageFeedback(BattleSide side, int instanceId, int damage)
        {
            if (!OwnsBattle || damage <= 0 || !_active.TryGetValue(instanceId, out var entry) ||
                entry.Group.Side != side || entry.View == null || !entry.View.PlayHitFeedback()) return false;
            if (_damageNumberPrefab != null)
            {
                var instance = Instantiate(_damageNumberPrefab, entry.View.transform.position + Vector3.up * 0.25f,
                    Quaternion.identity, transform);
                var number = instance.GetComponent<DamageNumberView>();
                if (number == null) number = instance.AddComponent<DamageNumberView>();
                number.Initialize(damage, instance.transform.position);
                _damageNumbers.Add(number);
            }
            else
            {
                var instance = new GameObject("DamageNumber");
                instance.transform.SetParent(transform, true);
                var number = instance.AddComponent<DamageNumberView>();
                number.Initialize(damage, entry.View.transform.position + Vector3.up * 0.25f);
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

        /// <summary>逻辑实体已回收，死亡动画单独保留，不能再抵塔或被索敌。</summary>
        public void NotifyKilled(EntityRemovalSnapshot snapshot)
        {
            if (!OwnsBattle || snapshot.RemovalReason != EntityRemovalReason.Killed || snapshot.Kind != EntityKind.Enemy ||
                _context.Registry.TryGet(snapshot.InstanceId, out _) ||
                !_active.TryGetValue(snapshot.InstanceId, out var entry) || entry.Group.Side != snapshot.Side) return;
            _active.Remove(snapshot.InstanceId);
            if (entry.View == null) return;
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
            _generation++;
            _prepared = _preparing = _started = false;
            _elapsed = 0d;
            _timeline = null;
            _dueSpawns.Clear();
            var context = _context;
            _context = null;
            _host = null;
            foreach (var pair in _active)
            {
                context?.Registry.Remove(pair.Key, EntityRemovalReason.BattleEnded, out _);
                if (pair.Value.View != null) Release(pair.Value.View.gameObject);
            }
            _active.Clear();
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
