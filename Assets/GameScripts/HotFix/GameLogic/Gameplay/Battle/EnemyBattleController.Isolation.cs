using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    public sealed partial class EnemyBattleController
    {
        private sealed class IsolationEffect
        {
            internal SkillOwnerRef Owner;
            internal string Prefab;
            internal bool Isolated;
            internal double StartedAt;
            internal GameObject Instance;
            internal StoreIsolationEffectView View;
        }
        private readonly Dictionary<int, IsolationEffect> _isolationEffects = new Dictionary<int, IsolationEffect>();
        private readonly Dictionary<string, Queue<GameObject>> _isolationReady = new Dictionary<string, Queue<GameObject>>();
        private readonly HashSet<string> _isolationLoading = new HashSet<string>();
        private readonly Dictionary<string, double> _isolationRetry = new Dictionary<string, double>();
        private Func<string, Transform, CancellationToken, UniTask<GameObject>> _loadIsolation;
        private CancellationTokenSource _isolationLifetime;
        public int IsolationEffectCount => _isolationEffects.Values.Count(e => e.Instance != null);

        private void InitializeIsolation(Func<string, Transform, CancellationToken, UniTask<GameObject>> load, CancellationToken token)
        {
            _loadIsolation = load;
            _isolationLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _context.Battle.EnemyIsolationChanged += OnEnemyIsolationChanged;
        }

        private void OnEnemyIsolationChanged(SkillOwnerRef owner, bool isolated, string prefab)
        {
            if (!OwnsBattle || owner.BattleId != _context.BattleId || !owner.EntityId.HasValue ||
                !_active.TryGetValue(owner.EntityId.Value, out var enemy) || enemy.Group.Side != owner.Side) return;
            enemy.View?.SetMoving(!isolated);
            var id = owner.EntityId.Value;
            if (!_isolationEffects.TryGetValue(id, out var effect))
            {
                if (!isolated || string.IsNullOrWhiteSpace(prefab)) return;
                effect = new IsolationEffect { Owner = owner, Prefab = prefab };
                _isolationEffects.Add(id, effect);
            }
            effect.Isolated = isolated;
            effect.StartedAt = _elapsed;
            SyncIsolationEffects();
        }

        private void SyncIsolationEffects()
        {
            if (!OwnsBattle) return;
            foreach (var pair in _isolationEffects.ToArray())
            {
                var effect = pair.Value;
                if (!_active.TryGetValue(pair.Key, out var entry) || entry.View == null ||
                    !_context.Battle.TryResolveOwner(effect.Owner, out var owner) || !(owner is EnemyEntity enemy) || !enemy.IsAlive)
                { ReleaseIsolationEffect(pair.Key); continue; }
                if (effect.Instance == null && _isolationReady.TryGetValue(effect.Prefab, out var ready) && ready.Count > 0)
                {
                    effect.Instance = ready.Dequeue();
                    effect.View = effect.Instance.GetComponent<StoreIsolationEffectView>();
                    effect.Instance.transform.SetParent(entry.View.transform.parent, false);
                    BattleSideRenderGroup.BindRoot(effect.Instance, _host.GetComponent<BattleSideRenderRegistry>(), effect.Owner.Side);
                    effect.Instance.SetActive(true);
                }
                if (effect.Instance == null) continue;
                effect.View.Follow(entry.View);
                if (effect.View.SampleState(effect.Isolated, (float)Math.Max(0d, _elapsed - effect.StartedAt)))
                    ReleaseIsolationEffect(pair.Key);
            }
            foreach (var prefab in _isolationEffects.Values.Where(e => e.Instance == null).Select(e => e.Prefab).Distinct().ToArray())
                if (!_isolationLoading.Contains(prefab) && (!_isolationRetry.TryGetValue(prefab, out var retry) || _elapsed >= retry))
                    LoadIsolationEffectsAsync(prefab).Forget();
        }

        private async UniTaskVoid LoadIsolationEffectsAsync(string prefab)
        {
            if (_loadIsolation == null || _isolationLifetime == null || !_isolationLoading.Add(prefab)) return;
            var generation = _generation;
            var token = _isolationLifetime.Token;
            GameObject instance = null;
            try
            {
                while (OwnsBattle && generation == _generation && _isolationEffects.Values.Any(e => e.Prefab == prefab && e.Instance == null))
                {
                    instance = await _loadIsolation(prefab, _preparedRoot, token);
                    token.ThrowIfCancellationRequested();
                    if (!OwnsBattle || generation != _generation) return;
                    if (instance == null || instance.GetComponent<StoreIsolationEffectView>() == null ||
                        !instance.GetComponent<StoreIsolationEffectView>().IsValid)
                        throw new InvalidOperationException("装盒表现缺少有效StoreIsolationEffectView：" + prefab);
                    instance.SetActive(false);
                    if (!_isolationReady.TryGetValue(prefab, out var pool))
                        _isolationReady.Add(prefab, pool = new Queue<GameObject>());
                    pool.Enqueue(instance); instance = null;
                    SyncIsolationEffects();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (OwnsBattle && generation == _generation)
                {
                    _isolationRetry[prefab] = _elapsed + 1d;
                    Debug.LogError("装盒表现准备失败，1秒战斗时间后重试：" + exception.Message);
                }
            }
            finally
            {
                Release(instance);
                if (generation == _generation) _isolationLoading.Remove(prefab);
            }
        }

        private void ReleaseIsolationEffect(int id)
        {
            if (!_isolationEffects.TryGetValue(id, out var effect)) return;
            _isolationEffects.Remove(id);
            if (effect.Instance == null) return;
            effect.View.Clear(); effect.Instance.SetActive(false);
            effect.Instance.transform.SetParent(_preparedRoot, false);
            if (!_isolationReady.TryGetValue(effect.Prefab, out var pool))
                _isolationReady.Add(effect.Prefab, pool = new Queue<GameObject>());
            pool.Enqueue(effect.Instance);
        }

        private void ClearIsolation()
        {
            if (_context != null) _context.Battle.EnemyIsolationChanged -= OnEnemyIsolationChanged;
            var lifetime = _isolationLifetime; _isolationLifetime = null;
            lifetime?.Cancel(); lifetime?.Dispose();
            foreach (var effect in _isolationEffects.Values) Release(effect.Instance);
            _isolationEffects.Clear();
            foreach (var pool in _isolationReady.Values) while (pool.Count > 0) Release(pool.Dequeue());
            _isolationReady.Clear(); _isolationLoading.Clear(); _isolationRetry.Clear(); _loadIsolation = null;
        }
    }
}
