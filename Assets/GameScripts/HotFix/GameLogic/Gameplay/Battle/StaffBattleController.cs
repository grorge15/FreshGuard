using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只管理召唤与生命周期。接触由Enemy移动统一求解，不走弹丸或Combat钩子。</summary>
    public sealed class StaffBattleController : IDisposable, ISingleUseTimelineAction, IReleasableTimelineAction
    {
        private readonly BattleShopSceneController _host;
        private readonly BattleShopContext _context;
        private readonly EnemyBattleController _enemies;
        private readonly Func<string, Transform, CancellationToken, UniTask<GameObject>> _load;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Transform _readyRoot;
        private readonly Transform _activeRoot;
        private readonly Dictionary<string, Queue<GameObject>> _ready = new Dictionary<string, Queue<GameObject>>();
        private readonly HashSet<string> _loading = new HashSet<string>();
        private readonly List<Entry> _active = new List<Entry>();
        private bool _disposed;
        private int _nextId;
        private sealed class Entry
        {
            internal StaffState State;
            internal StaffView View;
            internal BattleRouteGeometry Route;
        }
        public int Count => _active.Count;
        public IReadOnlyList<StaffState> States => _active.Select(e => e.State).ToArray();
        private bool OwnsBattle => !_disposed && _host != null && _host.Context == _context && !_context.IsEnded && !_context.IsEndRequested;
        internal StaffBattleController(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies,
            Func<string, Transform, CancellationToken, UniTask<GameObject>> load)
        {
            _host = host; _context = context; _enemies = enemies; _load = load;
            _readyRoot = new GameObject("PreparedStaffs").transform;
            _readyRoot.SetParent(host.transform, false); _readyRoot.gameObject.SetActive(false);
            _activeRoot = new GameObject("BattleStaffs").transform; _activeRoot.SetParent(host.transform, false);
        }
        internal async UniTask PrepareAsync(SkillCatalog catalog, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token))
                foreach (var prefab in catalog.Levels.Where(l => l.Staff != null).Select(l => l.Staff.Prefab).Distinct())
                {
                    var instance = await _load(prefab, _readyRoot, linked.Token);
                    if (!OwnsBattle || linked.IsCancellationRequested) { Release(instance); linked.Token.ThrowIfCancellationRequested(); throw new OperationCanceledException(); }
                    if (instance == null || instance.GetComponent<StaffView>() == null)
                    { Release(instance); throw new InvalidOperationException("Staff Prefab缺少StaffView：" + prefab); }
                    instance.SetActive(false);
                    _ready.Add(prefab, new Queue<GameObject>(new[] { instance }));
                }
            _context.Battle.Timelines.RegisterAction("robot.staff.summon", this);
            _context.Battle.Timelines.Register(new TimelineModel("robot.roll", 0, new[] { new TimelineNode(0, "robot.staff.summon") }));
        }
        public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute) => TryPrepare(cast, node, out execute, out _);
        public bool TryPrepare(SkillCastSnapshot cast, TimelineNode node, out Action execute, out Action release)
        {
            execute = null; release = null;
            if (!OwnsBattle || !_host.IsCombatRunning || cast.Staff == null || !_context.Battle.TryResolveOwner(cast.Owner, out var owner) || !owner.CanCast ||
                !_enemies.TryPrepareStaff(cast.Owner.Side, cast.Staff, out var route, out var progress)) return false;
            var prefab = cast.Staff.Prefab;
            if (!_ready.TryGetValue(prefab, out var pool)) return false;
            while (pool.Count > 0 && pool.Peek() == null) pool.Dequeue();
            if (pool.Count == 0) { Refill(prefab).Forget(); return false; }
            var instance = pool.Dequeue();
            var consumed = false;
            release = () => { if (!consumed) { consumed = true; Return(prefab, instance); } };
            execute = () =>
            {
                if (consumed) return;
                consumed = true;
                if (!OwnsBattle) { Release(instance); return; }
                var state = new StaffState(_context.BattleId, ++_nextId, cast.Owner, cast.Staff, progress);
                var view = instance.GetComponent<StaffView>();
                instance.transform.SetParent(_activeRoot, false);
                try
                {
                    view.Initialize(state, route);
                    BattleSideRenderGroup.BindRoot(instance, _host.GetComponent<BattleSideRenderRegistry>(), cast.Owner.Side);
                    _active.Add(new Entry { State = state, View = view, Route = route });
                    instance.SetActive(true);
                }
                catch { Return(prefab, instance); throw; }
                Refill(prefab).Forget();
            };
            return true;
        }
        private async UniTaskVoid Refill(string prefab)
        {
            if (!OwnsBattle || !_ready.TryGetValue(prefab, out var pool) || pool.Count > 0 || !_loading.Add(prefab)) return;
            GameObject instance = null;
            try
            {
                instance = await _load(prefab, _readyRoot, _lifetime.Token);
                if (!OwnsBattle) return;
                if (instance == null || instance.GetComponent<StaffView>() == null) throw new InvalidOperationException("Staff资源缺少StaffView：" + prefab);
                instance.SetActive(false); pool.Enqueue(instance); instance = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (OwnsBattle) Debug.LogException(e); }
            finally { Release(instance); _loading.Remove(prefab); }
        }
        internal float TimeUntilBoundary
        {
            get
            {
                if (_active.Count == 0) return float.PositiveInfinity;
                // 小段限制道路横向偏移的线性化误差，纵向仍采用整段扫掠。
                var step = 1f / 120f;
                foreach (var entry in _active) step = Mathf.Min(step, Mathf.Max(0.000001f, entry.State.TimeUntilBoundary(entry.Route)));
                return step;
            }
        }
        internal void BeginStep(float deltaTime)
        { foreach (var entry in _active) entry.State.Advance(deltaTime); }
        internal float Constrain(BattleSide side, BattleRouteGeometry individual, float start, float candidate, float radius)
        {
            var result = candidate;
            foreach (var entry in _active)
            {
                if (entry.State.Side != side) continue;
                var route = entry.Route;
                var from = individual.ConvertTo(route, start); var to = individual.ConvertTo(route, candidate);
                var constrained = entry.State.Constrain(from, to, route.LateralOffset(individual, from), route.LateralOffset(individual, to), radius);
                result = Mathf.Min(result, route.ConvertTo(individual, constrained));
            }
            return result;
        }
        internal void EndStep()
        {
            foreach (var entry in _active.ToArray())
            {
                if (entry.State.Phase == StaffPhase.Finished)
                { _active.Remove(entry); Return(entry.State.Config.Prefab, entry.View != null ? entry.View.gameObject : null); }
                else if (entry.View != null) entry.View.Render(entry.Route);
            }
        }
        private void Return(string prefab, GameObject instance)
        {
            if (instance == null) return;
            instance.GetComponent<StaffView>()?.Clear(); instance.SetActive(false);
            if (!OwnsBattle || !_ready.TryGetValue(prefab, out var pool)) { Release(instance); return; }
            instance.transform.SetParent(_readyRoot, false); pool.Enqueue(instance);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _lifetime.Cancel();
            foreach (var entry in _active) if (entry.View != null) Release(entry.View.gameObject);
            _active.Clear();
            foreach (var pool in _ready.Values) while (pool.Count > 0) Release(pool.Dequeue());
            _ready.Clear(); Release(_readyRoot != null ? _readyRoot.gameObject : null); Release(_activeRoot != null ? _activeRoot.gameObject : null);
            _lifetime.Dispose();
        }
        private static void Release(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false); instance.GetComponent<TEngine.AssetsReference>()?.ReleaseReferences();
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance); else UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
