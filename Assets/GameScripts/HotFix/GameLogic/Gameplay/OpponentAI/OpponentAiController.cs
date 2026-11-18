using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    public sealed class OpponentAiSettings
    {
        public bool Enabled = true;
        public float InitialDelay = 0.8f;
        public float ScanInterval = 0.5f;
        public float FastScanInterval = 0.15f;
        public float MinimumActionInterval = 0.8f;
        public float MovementInterval = 3f;
        public float PurchaseRefillInterval = 3f;
        public int PurchaseCapacity = 2;
        public int SearchCandidatesPerFrame = 64;
        public float SearchMillisecondsPerFrame = 2f;
        public float SearchWindow = 0.5f;
        public float BusyRetryDelay = 0.5f;
        public float RefreshCooldown = 8f;
        public float ResourceRetryDelay = 5f;

        // 只保留旧场景序列化数据及源码兼容；V2 调度不读取这些字段。
        [HideInInspector] public float ThinkMin = 2f;
        [HideInInspector] public float ThinkMax = 4f;
        [HideInInspector] public float ActionMin = 0.8f;
        [HideInInspector] public float ActionMax = 1.5f;
        [HideInInspector] public int ActionsPerRound = 3;
        [HideInInspector] public int PurchasesPerRound = 2;
        [HideInInspector] public int InitialRobotTarget = 3;
        [HideInInspector] public float MoveCooldown = 10f;

        internal OpponentAiSettings Normalized()
        {
            var copy = (OpponentAiSettings)MemberwiseClone();
            copy.InitialDelay = Finite(InitialDelay, 0.8f, 0f);
            copy.ScanInterval = Finite(ScanInterval, 0.5f, 0.01f);
            copy.FastScanInterval = Math.Min(copy.ScanInterval, Finite(FastScanInterval, 0.15f, 0.01f));
            copy.MinimumActionInterval = Finite(MinimumActionInterval, 0.8f, 0.8f);
            copy.MovementInterval = Finite(MovementInterval, 3f, 0.1f);
            copy.PurchaseRefillInterval = Finite(PurchaseRefillInterval, 3f, 0.1f);
            copy.PurchaseCapacity = Math.Max(0, PurchaseCapacity);
            copy.SearchCandidatesPerFrame = Math.Max(1, SearchCandidatesPerFrame);
            copy.SearchMillisecondsPerFrame = Finite(SearchMillisecondsPerFrame, 2f, 0.01f);
            copy.SearchWindow = Finite(SearchWindow, 0.5f, 0.01f);
            copy.BusyRetryDelay = Finite(BusyRetryDelay, 0.5f, 0.1f);
            copy.RefreshCooldown = Finite(RefreshCooldown, 8f, 0.1f);
            copy.ResourceRetryDelay = Finite(ResourceRetryDelay, 5f, 0.1f);
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

    /// <summary>主线程调度器：通知仅置脏，规划和唯一事务均由 Advance 发起。</summary>
    public sealed class OpponentAiController : IDisposable
    {
        private readonly OpponentAiSettings _settings;
        private readonly Func<OpponentAiSnapshot> _snapshot;
        private readonly Func<OpponentAiAction, CancellationToken, UniTask<ShopOperationResult>> _execute;
        private readonly Func<OpponentAiSnapshot, OpponentAiPlanningOptions, OpponentAiAction> _planning;
        private readonly Action<Exception> _logException;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<long, float> _failedOffers = new Dictionary<long, float>();
        private readonly List<OpponentAiMoveMemory> _recentMoves = new List<OpponentAiMoveMemory>(2);
        private readonly IReadOnlyList<OpponentAiMoveMemory> _recentMovesView;
        private float _clock;
        private float _scanElapsed;
        private float _actionRemaining;
        private float _retryRemaining;
        private float _moveRemaining;
        private float _refreshRemaining;
        private double _purchaseRefillElapsed;
        private int _purchaseTokens;
        private bool _hasScanned;
        private bool _dirty;
        private bool _executing;
        private bool _disposed;
        private long _preferredOffer;
        private long? _battleId;
        private OpponentAiSearchSession _search;
        private Task _searchWork;
        private readonly bool _runSearchInBackground;
        private float _searchElapsed;
        private float _partialElapsed;
        private OpponentAiFollowUp? _pendingFollowUp;
        private float _followUpExpires;

        public bool IsExecuting => _executing;
        public bool IsSearching => _search != null;
        public float ActiveTime => _clock;
        public OpponentAiAction LastAction { get; private set; } = OpponentAiAction.Wait();
        public IReadOnlyList<OpponentAiMoveMemory> RecentMoves => _recentMovesView;

        // random 参数保留旧调用签名；独立节奏不再使用随机轮次。
        public OpponentAiController(OpponentAiSettings settings, Func<OpponentAiSnapshot> snapshot,
            Func<OpponentAiAction, CancellationToken, UniTask<ShopOperationResult>> execute,
            IOpponentAiRandom random = null, Action<Exception> logException = null,
            Func<OpponentAiSnapshot, OpponentAiPlanningOptions, OpponentAiAction> planning = null,
            bool runSearchInBackground = false)
        {
            _settings = (settings ?? new OpponentAiSettings()).Normalized();
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _planning = planning;
            _runSearchInBackground = runSearchInBackground;
            _logException = logException ?? Debug.LogException;
            _purchaseTokens = _settings.PurchaseCapacity;
            _recentMovesView = _recentMoves.AsReadOnly();
        }

        public void MarkDirty()
        {
            if (!_disposed) _dirty = true;
        }

        public void Advance(float deltaTime, bool canAct)
        {
            if (_disposed || !_settings.Enabled || !canAct || deltaTime < 0f ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _clock += deltaTime;
            _scanElapsed += deltaTime;
            _actionRemaining = Math.Max(0f, _actionRemaining - deltaTime);
            _retryRemaining = Math.Max(0f, _retryRemaining - deltaTime);
            _moveRemaining = Math.Max(0f, _moveRemaining - deltaTime);
            _refreshRemaining = Math.Max(0f, _refreshRemaining - deltaTime);
            RefillPurchases(deltaTime);
            // 异步加载可计入已有冷却，但成功后的全局间隔只能在完成时开始。
            if (_executing || _actionRemaining > 0f || _retryRemaining > 0f) return;
            if (_search != null) { AdvanceSearch(deltaTime); return; }
            var interval = !_hasScanned ? _settings.InitialDelay :
                _dirty ? _settings.FastScanInterval : _settings.ScanInterval;
            if (_scanElapsed < interval) return;
            _hasScanned = true;
            _scanElapsed = 0f;
            _dirty = false;
            var snapshot = ReadSnapshot();
            if (snapshot == null) return;
            var options = CreateOptions();
            // 注入策略和无 World 的纯数据场景保留同步入口，生产准备期也走分帧搜索。
            if (_planning != null || snapshot.World == null)
            {
                var action = _planning != null ? _planning(snapshot, options) : OpponentAiPlanner.Plan(snapshot, options);
                TryRunAction(snapshot, action, options);
                return;
            }
            _search = OpponentAiPlanner.BeginSearch(snapshot, options);
            _searchElapsed = 0f;
            _partialElapsed = 0f;
            AdvanceSearch(0f, snapshot);
        }

        private OpponentAiSnapshot ReadSnapshot()
        {
            // 球会持续运动，不能用“未收到结构通知”作为缓存快照的依据。
            var snapshot = _snapshot();
            if (snapshot == null) { ClearSearch(); return null; }
            if (_battleId.HasValue && snapshot.BattleId != _battleId.Value) { Dispose(); return null; }
            _battleId = snapshot.BattleId;
            return snapshot;
        }

        private void AdvanceSearch(float deltaTime, OpponentAiSnapshot snapshot = null)
        {
            // 精检是不可抢占的纯几何计算；2ms只能约束粗筛，不能在渲染线程运行整批精检。
            // 同一会话只允许一个后台步进，完成后主线程才可读取或更新其快照。
            var completedWork = false;
            if (_searchWork != null)
            {
                if (!_searchWork.IsCompleted)
                {
                    _searchElapsed += deltaTime;
                    _partialElapsed += deltaTime;
                    return;
                }
                var error = _searchWork.Exception;
                _searchWork = null;
                if (error != null)
                {
                    _logException(error.GetBaseException());
                    ClearSearch();
                    _retryRemaining = _settings.BusyRetryDelay;
                    return;
                }
                completedWork = true;
            }
            snapshot = snapshot ?? ReadSnapshot();
            if (snapshot == null) return;
            var liveOptions = CreateOptions();
            if (snapshot.BoardRevision != _search.BoardRevision || !_search.MatchesOptions(liveOptions))
            {
                ClearSearch();
                _search = OpponentAiPlanner.BeginSearch(snapshot, CreateOptions());
                _searchElapsed = 0f;
                _partialElapsed = 0f;
                _dirty = false;
                completedWork = false;
            }
            else
            {
                _searchElapsed += deltaTime;
                _partialElapsed += deltaTime;
            }
            // 必须先推进，再读取 BestAction；结构不变时保留已有候选，球运动不重启。
            _search.UpdateLiveSnapshot(snapshot, liveOptions);
            if (_runSearchInBackground)
            {
                if (completedWork && (_search.IsComplete || _search.BestAction.Kind != OpponentAiActionKind.Wait))
                {
                    var ready = _search.BestAction;
                    ClearSearch();
                    _scanElapsed = 0f;
                    TryRunAction(snapshot, ready, liveOptions);
                    return;
                }
                var search = _search;
                var publish = _partialElapsed >= _settings.FastScanInterval || _searchElapsed >= _settings.SearchWindow;
                if (publish) _partialElapsed = 0f;
                if (_searchElapsed >= _settings.SearchWindow) _searchElapsed = 0f;
                _searchWork = Task.Run(() => {
                    search.Advance(_settings.SearchCandidatesPerFrame, _settings.SearchMillisecondsPerFrame);
                    if (publish && !search.IsComplete) search.PublishBestPartial();
                });
                return;
            }
            _search.Advance(_settings.SearchCandidatesPerFrame, _settings.SearchMillisecondsPerFrame);
            if (!_search.IsComplete && (_partialElapsed >= _settings.FastScanInterval ||
                _searchElapsed >= _settings.SearchWindow))
            {
                _search.PublishBestPartial();
                _partialElapsed = 0f;
                if (_searchElapsed >= _settings.SearchWindow) _searchElapsed = 0f;
            }
            var action = _search.BestAction;
            // 窗口到期仍没有精检结果时继续推进，不能反复丢弃尚未搜索完的会话。
            if (!_search.IsComplete && action.Kind == OpponentAiActionKind.Wait) return;
            ClearSearch();
            _scanElapsed = 0f;
            TryRunAction(snapshot, action, CreateOptions());
        }

        private OpponentAiPlanningOptions CreateOptions()
        {
            var blocked = new List<long>();
            var expired = new List<long>();
            foreach (var pair in _failedOffers)
            {
                if (pair.Value > _clock) blocked.Add(pair.Key);
                else expired.Add(pair.Key);
            }
            foreach (var id in expired) _failedOffers.Remove(id);
            return new OpponentAiPlanningOptions {
                AllowPurchase = _purchaseTokens > 0,
                AllowMove = _moveRemaining <= 0f,
                AllowRefresh = _refreshRemaining <= 0f,
                PreferredOfferId = _preferredOffer,
                BlockedOfferIds = blocked,
                RecentMoves = _runSearchInBackground ? _recentMoves.ToArray() : _recentMovesView,
                ActiveTime = _clock,
                PendingFollowUp = _clock <= _followUpExpires ? _pendingFollowUp : null
            };
        }

        private void TryRunAction(OpponentAiSnapshot snapshot, OpponentAiAction action, OpponentAiPlanningOptions options)
        {
            if (_disposed || _executing || action.Kind == OpponentAiActionKind.Wait ||
                action.BattleId != snapshot.BattleId) return;
            if (IsPurchase(action.Kind))
            {
                if (!options.AllowPurchase) return;
                if (_failedOffers.TryGetValue(action.OfferId, out var until) && until > _clock) return;
            }
            if (action.Kind == OpponentAiActionKind.Move && !options.AllowMove ||
                action.Kind == OpponentAiActionKind.Refresh && !options.AllowRefresh) return;
            BoardCoordinate? moveFrom = null;
            if (action.Kind == OpponentAiActionKind.Move)
                foreach (var robot in snapshot.Robots)
                    if (robot.InstanceId == action.SourceInstanceId && robot.IsOnBoard) { moveFrom = robot.Anchor; break; }
            LastAction = action;
            RunActionAsync(action, moveFrom).Forget();
        }

        private async UniTask RunActionAsync(OpponentAiAction action, BoardCoordinate? moveFrom)
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
                _pendingFollowUp = action.FollowUp;
                _followUpExpires = _clock + 8f;
                _actionRemaining = _settings.MinimumActionInterval;
                if (IsPurchase(action.Kind))
                {
                    _purchaseTokens--;
                    _preferredOffer = 0;
                }
                if (action.FreedOfferId != 0) _preferredOffer = action.FreedOfferId;
                if (action.Kind == OpponentAiActionKind.Move)
                {
                    _moveRemaining = _settings.MovementInterval;
                    if (moveFrom.HasValue)
                    {
                        if (_recentMoves.Count == 2) _recentMoves.RemoveAt(0);
                        _recentMoves.Add(new OpponentAiMoveMemory(action.SourceInstanceId,
                            moveFrom.Value, action.Anchor, _clock));
                    }
                }
                if (action.Kind == OpponentAiActionKind.Refresh) _refreshRemaining = _settings.RefreshCooldown;
            }
            else
            {
                if (result == ShopOperationResult.ResourceFailed && action.OfferId != 0)
                    _failedOffers[action.OfferId] = _clock + _settings.ResourceRetryDelay;
                if (result != ShopOperationResult.Busy) _preferredOffer = 0;
                _retryRemaining = _settings.BusyRetryDelay;
            }
        }

        private void RefillPurchases(float deltaTime)
        {
            // 从有效时间开始每三秒一次；容量满时丢弃多余额度，不把暂停或长帧排成动作队列。
            _purchaseRefillElapsed += deltaTime;
            if (_purchaseRefillElapsed < _settings.PurchaseRefillInterval) return;
            var restored = Math.Floor(_purchaseRefillElapsed / _settings.PurchaseRefillInterval);
            _purchaseRefillElapsed %= _settings.PurchaseRefillInterval;
            _purchaseTokens += (int)Math.Min(_settings.PurchaseCapacity - _purchaseTokens, restored);
        }

        private static bool IsPurchase(OpponentAiActionKind kind)
            => kind == OpponentAiActionKind.PurchaseAndDeploy || kind == OpponentAiActionKind.PurchaseAndMerge;

        private void ClearSearch()
        {
            var search = _search;
            var work = _searchWork;
            _search = null;
            _searchWork = null;
            if (search == null) return;
            if (work != null && !work.IsCompleted)
            {
                // 结束对局不等待工作线程，也不与仍在MoveNext的枚举器并发Dispose。
                work.ContinueWith(finished => {
                    var observed = finished.Exception;
                    search.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                var observed = work?.Exception;
                search.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ClearSearch();
            _recentMoves.Clear();
            _failedOffers.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
