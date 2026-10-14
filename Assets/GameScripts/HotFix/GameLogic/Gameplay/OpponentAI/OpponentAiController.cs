using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    public sealed class OpponentAiSettings
    {
        public bool Enabled = true;
        public float InitialDelay = 0.8f;
        public float ThinkMin = 2f;
        public float ThinkMax = 4f;
        public float ActionMin = 0.8f;
        public float ActionMax = 1.5f;
        public float BusyRetryDelay = 0.5f;
        public int ActionsPerRound = 3;
        public int PurchasesPerRound = 2;
        public int InitialRobotTarget = 3;
        public float MoveCooldown = 10f;
        public float RefreshCooldown = 8f;
        public float ResourceRetryDelay = 5f;

        internal OpponentAiSettings Normalized()
        {
            var copy = (OpponentAiSettings)MemberwiseClone();
            copy.InitialDelay = Finite(InitialDelay, 0.8f, 0f);
            copy.ThinkMin = Finite(ThinkMin, 2f, 0.1f);
            copy.ThinkMax = Finite(ThinkMax, 4f, copy.ThinkMin);
            copy.ActionMin = Finite(ActionMin, 0.8f, 0.1f);
            copy.ActionMax = Finite(ActionMax, 1.5f, copy.ActionMin);
            copy.BusyRetryDelay = Finite(BusyRetryDelay, 0.5f, 0.1f);
            copy.MoveCooldown = Finite(MoveCooldown, 10f, 0.1f);
            copy.RefreshCooldown = Finite(RefreshCooldown, 8f, 0.1f);
            copy.ResourceRetryDelay = Finite(ResourceRetryDelay, 5f, 0.1f);
            copy.ActionsPerRound = Math.Max(1, ActionsPerRound);
            copy.PurchasesPerRound = Math.Max(0, PurchasesPerRound);
            copy.InitialRobotTarget = Math.Max(3, InitialRobotTarget);
            return copy;
        }

        private static float Finite(float value, float fallback, float minimum)
            => Math.Max(minimum, float.IsNaN(value) || float.IsInfinity(value) ? fallback : value);
    }

    public interface IOpponentAiRandom
    {
        float Range(float minInclusive, float maxInclusive);
    }

    internal sealed class OpponentAiRandom : IOpponentAiRandom
    {
        private readonly System.Random _random = new System.Random();
        public float Range(float minInclusive, float maxInclusive)
            => minInclusive + (float)_random.NextDouble() * (maxInclusive - minInclusive);
    }

    /// <summary>主线程调度器：通知仅作快照失效，执行只由 Advance 发起。</summary>
    public sealed class OpponentAiController : IDisposable
    {
        private readonly OpponentAiSettings _settings;
        private readonly Func<OpponentAiSnapshot> _snapshot;
        private readonly Func<OpponentAiAction, CancellationToken, UniTask<ShopOperationResult>> _execute;
        private readonly IOpponentAiRandom _random;
        private readonly Action<Exception> _logException;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<long, float> _failedOffers = new Dictionary<long, float>();
        private float _clock;
        private float _waitRemaining;
        private float _moveRemaining;
        private float _refreshRemaining;
        private int _actions;
        private int _purchases;
        private bool _moved;
        private bool _refreshed;
        private bool _executing;
        private bool _disposed;
        private long _preferredOffer;
        private long? _battleId;
        private OpponentAiSnapshot _cachedSnapshot;

        public bool IsExecuting => _executing;

        public OpponentAiController(OpponentAiSettings settings, Func<OpponentAiSnapshot> snapshot,
            Func<OpponentAiAction, CancellationToken, UniTask<ShopOperationResult>> execute,
            IOpponentAiRandom random = null, Action<Exception> logException = null)
        {
            _settings = (settings ?? new OpponentAiSettings()).Normalized();
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _random = random ?? new OpponentAiRandom();
            _logException = logException ?? Debug.LogException;
            _waitRemaining = _settings.InitialDelay;
        }

        public void MarkDirty() { _cachedSnapshot = null; }

        public void Advance(float deltaTime, bool canAct)
        {
            if (_disposed || !_settings.Enabled || !canAct || deltaTime < 0f ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _clock += deltaTime;
            _moveRemaining = Math.Max(0f, _moveRemaining - deltaTime);
            _refreshRemaining = Math.Max(0f, _refreshRemaining - deltaTime);
            // 加载中计入冷却，但不能提前消耗执行完成后的动作间隔。
            if (_executing) return;
            _waitRemaining = Math.Max(0f, _waitRemaining - deltaTime);
            if (_waitRemaining > 0f) return;
            var snapshot = _cachedSnapshot ?? (_cachedSnapshot = _snapshot());
            if (snapshot == null) { StartRound(); return; }
            if (_battleId.HasValue && snapshot.BattleId != _battleId.Value) { Dispose(); return; }
            _battleId = snapshot.BattleId;
            var blocked = new List<long>();
            foreach (var pair in _failedOffers) if (pair.Value > _clock) blocked.Add(pair.Key);
            var action = OpponentAiPlanner.Plan(snapshot, new OpponentAiPlanningOptions {
                AllowPurchase = _purchases < _settings.PurchasesPerRound,
                AllowMove = !_moved && _moveRemaining <= 0f,
                AllowRefresh = !_refreshed && _refreshRemaining <= 0f,
                InitialRobotTarget = _settings.InitialRobotTarget,
                PreferredOfferId = _preferredOffer,
                BlockedOfferIds = blocked
            });
            if (action.Kind == OpponentAiActionKind.Wait) { StartRound(); return; }
            RunActionAsync(action).Forget();
        }

        private async UniTask RunActionAsync(OpponentAiAction action)
        {
            _executing = true;
            ShopOperationResult result;
            try { result = await _execute(action, _lifetime.Token); }
            catch (OperationCanceledException) { result = ShopOperationResult.BattleEnded; }
            catch (Exception e) { _logException(e); result = ShopOperationResult.ResourceFailed; }
            finally { _executing = false; MarkDirty(); }
            if (_disposed) return;
            if (result == ShopOperationResult.BattleEnded || result == ShopOperationResult.WrongBattle) { Dispose(); return; }
            if (result == ShopOperationResult.Success)
            {
                _actions++;
                if (action.Kind == OpponentAiActionKind.PurchaseAndDeploy || action.Kind == OpponentAiActionKind.PurchaseAndMerge)
                {
                    _purchases++;
                    _preferredOffer = 0;
                }
                if (action.FreedOfferId != 0) _preferredOffer = action.FreedOfferId;
                if (action.Kind == OpponentAiActionKind.Move) { _moved = true; _moveRemaining = _settings.MoveCooldown; }
                if (action.Kind == OpponentAiActionKind.Refresh) { _refreshed = true; _refreshRemaining = _settings.RefreshCooldown; }
                if (_actions >= _settings.ActionsPerRound) StartRound();
                else _waitRemaining = _random.Range(_settings.ActionMin, _settings.ActionMax);
            }
            else
            {
                if (result == ShopOperationResult.ResourceFailed && action.OfferId != 0)
                    _failedOffers[action.OfferId] = _clock + _settings.ResourceRetryDelay;
                if (result != ShopOperationResult.Busy) _preferredOffer = 0;
                _waitRemaining = _settings.BusyRetryDelay;
            }
        }

        private void StartRound()
        {
            _actions = 0;
            _purchases = 0;
            _moved = false;
            _refreshed = false;
            _waitRemaining = _random.Range(_settings.ThinkMin, _settings.ThinkMax);
            MarkDirty();
            var expired = new List<long>();
            foreach (var pair in _failedOffers) if (pair.Value <= _clock) expired.Add(pair.Key);
            foreach (var id in expired) _failedOffers.Remove(id);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            MarkDirty();
        }
    }
}
