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
    /// <summary>双方第一波的资源、出兵和节点移动；实例身份始终与所属对局一起使用。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemyBattleController : MonoBehaviour
    {
        [Header("我方战场路线：按数组顺序移动")]
        [Tooltip("把自行摆放的节点 Transform 按顺序拖入。Element 0 为出生点；同侧 Tower 自动作为终点，不必填入。修改在下一次初始化对局时生效。")]
        [SerializeField] private Transform[] _playerRoutePoints;
        [Header("敌方战场路线：按数组顺序移动")]
        [Tooltip("把自行摆放的节点 Transform 按顺序拖入。Element 0 为出生点；同侧 Tower 自动作为终点，不必填入。修改在下一次初始化对局时生效。")]
        [SerializeField] private Transform[] _opponentRoutePoints;
        private readonly List<SpawnGroup> _groups = new List<SpawnGroup>();
        private readonly Dictionary<int, MoveEntry> _active = new Dictionary<int, MoveEntry>();
        private BattleShopSceneController _host;
        private BattleShopContext _context;
        private Transform _preparedRoot;
        private bool _prepared;
        private bool _preparing;
        private bool _started;
        private int _generation;
        private double _elapsed;

        private sealed class SpawnGroup
        {
            internal BattleSide Side;
            internal WaveSpawn Rule;
            internal Enemy Config;
            internal Vector3[] Points;
            internal int TowerId;
            internal int Spawned;
            internal readonly Queue<GameObject> Pending = new Queue<GameObject>();
            internal double NextTime => Spawned < Rule.Count ? Spawned * (double)Rule.SpawnInterval : double.PositiveInfinity;
        }

        private sealed class MoveEntry
        {
            internal SpawnGroup Group;
            internal EnemyView View;
            internal int NextPoint = 1;
        }

        public int LiveCount => _active.Count;
        public bool IsWaveCompleted => _started && _groups.All(g => g.Spawned == g.Rule.Count) && _active.Count == 0;
        private bool OwnsBattle => this != null && _host != null && _context != null &&
            _host.Context == _context && !_context.IsEnded && !_context.IsEndRequested;
        private bool IsRunning => _prepared && _started && OwnsBattle && _host.IsCombatRunning;

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
                var rules = tables.TbWaveSpawn.DataList.Where(r => r.WaveId == 1).OrderBy(r => r.Id).ToArray();
                if (rules.Length == 0) throw new InvalidOperationException("第一波没有出兵配置。");
                foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                {
                    if (!host.TryGetTowerTarget(side, out var towerId, out var tower))
                        throw new InvalidOperationException("当前战场缺少有效 Tower：" + side);
                    var authored = side == BattleSide.Player ? _playerRoutePoints : _opponentRoutePoints;
                    var points = ReadRoute(authored, tower);
                    foreach (var rule in rules)
                    {
                        if (rule.Count <= 0 || !EntityValidation.IsFiniteNonNegative(rule.SpawnInterval) || rule.SpawnInterval <= 0f)
                            throw new InvalidOperationException("出兵数量和间隔必须为正数：" + rule.Id);
                        var config = tables.TbEnemy.Get(rule.EnemyId);
                        if (config == null || string.IsNullOrWhiteSpace(config.EnemyPrefab) || config.HpC1 <= 0 ||
                            !EntityValidation.IsFiniteNonNegative(config.EnemySpeed) || config.LeakDamage < 0)
                            throw new InvalidOperationException("第一波敌人配置无效：" + rule.EnemyId);
                        _groups.Add(new SpawnGroup { Side = side, Rule = rule, Config = config, Points = points, TowerId = towerId });
                    }
                }
                var root = new GameObject("PreparedEnemyViews");
                root.transform.SetParent(transform, false);
                root.SetActive(false);
                _preparedRoot = root.transform;
                foreach (var group in _groups.ToArray())
                    for (var i = 0; i < group.Rule.Count; i++)
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
            var points = authored.Select(p => p.position).Concat(new[] { tower.position }).ToArray();
            if (points.Any(p => !Finite(p.x) || !Finite(p.y) || !Finite(p.z)))
                throw new InvalidOperationException("路线节点位置必须为有限数值。");
            return points;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void StartFirstWave()
        {
            if (_started) return;
            if (!_prepared || !OwnsBattle) throw new InvalidOperationException("第一波尚未准备完成。");
            if (!_host.IsCombatRunning) return;
            _started = true;
            _elapsed = 0d;
            try { SpawnDue(); }
            catch { AbortBattle(); throw; }
        }

        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!IsRunning || IsWaveCompleted) return;
            try
            {
                var target = _elapsed + deltaTime;
                do
                {
                    var next = Math.Min(target, _groups.Min(g => g.NextTime));
                    // 抵塔也是时间边界，不能由集合遍历顺序决定双方先败顺序。
                    next = Math.Min(next, _elapsed + NextArrivalDelay());
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
                if (enemy.BaseSpeed > 0f) earliest = Math.Min(earliest, remaining / enemy.BaseSpeed);
            }
            return earliest;
        }

        private void SpawnDue()
        {
            foreach (var group in _groups.ToArray())
            {
                if (!IsRunning) return;
                if (group.NextTime > _elapsed + 0.0000001d) continue;
                var instance = group.Pending.Dequeue();
                var instanceId = 0;
                try
                {
                    instanceId = _context.Registry.CreateEnemyFromConfig(group.Config, group.Side, 1);
                    instance.transform.SetParent(transform, true);
                    instance.transform.position = group.Points[0];
                    instance.SetActive(true);
                    var view = instance.GetComponent<EnemyView>();
                    view.Bind(_context.Registry, instanceId);
                    view.SetMoving(true);
                    _active.Add(instanceId, new MoveEntry { Group = group, View = view });
                    group.Spawned++;
                }
                catch
                {
                    if (instanceId > 0) _context.Registry.Remove(instanceId, EntityRemovalReason.Manual, out _);
                    Release(instance);
                    throw;
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
                var distance = enemy.BaseSpeed * deltaTime;
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
                if (entry.NextPoint == points.Length) arrivals.Add(id);
            }
            // 同一时间段的所有位置先推进，再通知抵塔；回调冻结不会让后遍历者少走这段时间。
            foreach (var id in arrivals)
            {
                if (!IsRunning) return;
                if (!_active.TryGetValue(id, out var entry) || !_context.Registry.TryGet(id, out var entity) ||
                    !(entity is EnemyEntity enemy) || !enemy.IsAlive) continue;
                Arrive(id, entry, enemy.Configuration.LeakDamage);
            }
        }

        private void Arrive(int id, MoveEntry entry, int damage)
        {
            var context = _context;
            var host = _host;
            var side = entry.Group.Side;
            if (!context.Registry.Remove(id, EntityRemovalReason.Leaked, out var snapshot)) return;
            _active.Remove(id);
            try
            {
                // 先提交伤害，再通知表现，移除监听方冻结或终局不会吞掉此次抵塔。
                if (OwnsBattle && host.IsCombatRunning && host.TryGetTowerTarget(side, out var targetId, out _) && targetId == entry.Group.TowerId)
                    host.ApplyTowerDamage(context.BattleId, side, damage);
            }
            finally
            {
                if (entry.View != null)
                {
                    var instance = entry.View.gameObject;
                    try { entry.View.PlayRemoval(snapshot); }
                    finally { Release(instance); }
                }
            }
        }

        public void SetFrozen(bool frozen)
        {
            foreach (var entry in _active.Values)
                if (entry.View != null) entry.View.GetComponent<Animator>().speed = frozen ? 0f : 1f;
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
            var context = _context;
            _context = null;
            _host = null;
            foreach (var pair in _active)
            {
                context?.Registry.Remove(pair.Key, EntityRemovalReason.BattleEnded, out _);
                if (pair.Value.View != null) Release(pair.Value.View.gameObject);
            }
            _active.Clear();
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
            if (Application.isPlaying) Destroy(instance);
            else DestroyImmediate(instance);
        }

        private void OnDestroy() { Clear(); }
    }
}
