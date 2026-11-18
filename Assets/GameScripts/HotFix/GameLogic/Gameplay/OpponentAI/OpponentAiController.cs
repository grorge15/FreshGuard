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

    /// <summary>主线程调度器：通知仅置脏，购买与拖拽独立推进，最终同步事务交给场景执行。</summary>
    public sealed class OpponentAiController : IDisposable
    {
        // 只有主线程申请工作；两个通道轮流取得后台搜索权，禁止同时推进各自的几何搜索。
        private sealed class SearchWorker
        {
            private Task _running;
            private object _lastOwner;
            private readonly HashSet<object> _waiting = new HashSet<object>();
            public bool IsBusy => _running != null && !_running.IsCompleted;
            public void Request(object owner) => _waiting.Add(owner);
            public bool TrySchedule(object owner, Action step, out Task work)
            {
                work = null;
                _waiting.Add(owner);
                if (_running != null && !_running.IsCompleted) return false;
                if (ReferenceEquals(_lastOwner, owner) && _waiting.Count > 1) return false;
                _waiting.Remove(owner);
                _lastOwner = owner;
                work = _running = Task.Run(step);
                return true;
            }
            public void Forget(object owner) => _waiting.Remove(owner);
        }

        private OpponentAiController _purchaseLane, _dragLane;
        private OpponentAiController _parent;
        private SearchWorker _worker = new SearchWorker();
        private OpponentAiActionLane _lane;
        private int _lastArmyCount;
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
        private bool _armyEstablished;
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
        private OpponentAiSnapshot _noActionSnapshot;
        private float _noActionAt;

        public bool IsExecuting => _executing || (_purchaseLane?.IsExecuting ?? false) || (_dragLane?.IsExecuting ?? false);
        public bool IsSearching => _search != null || (_purchaseLane?.IsSearching ?? false) || (_dragLane?.IsSearching ?? false);
        public bool ArmyEstablished => _armyEstablished;
        public float ActiveTime => _clock;
        public OpponentAiAction LastAction { get; private set; } = OpponentAiAction.Wait();
        public IReadOnlyList<OpponentAiMoveMemory> RecentMoves => _dragLane?.RecentMoves ?? _recentMovesView;

        public OpponentAiController(OpponentAiSettings settings, Func<OpponentAiSnapshot> snapshot,
            Func<OpponentAiAction, CancellationToken, UniTask<ShopOperationResult>> execute,
            Action<Exception> logException = null,
            Func<OpponentAiSnapshot, OpponentAiPlanningOptions, OpponentAiAction> planning = null,
            bool runSearchInBackground = false, bool splitScheduling = false)
        {
            _settings = (settings ?? new OpponentAiSettings()).Normalized();
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _planning = planning;
            _runSearchInBackground = runSearchInBackground;
            _logException = logException ?? Debug.LogException;
            _purchaseTokens = _settings.PurchaseCapacity;
            _recentMovesView = _recentMoves.AsReadOnly();
            if (splitScheduling)
            {
                // 两个子调度器保留各自的计时、额度、搜索进度、执行标记和后续目标。
                _purchaseLane = CreateLane(OpponentAiActionLane.Purchase);
                _dragLane = CreateLane(OpponentAiActionLane.Drag);
            }
        }

        private OpponentAiController CreateLane(OpponentAiActionLane lane)
        {
            var controller = new OpponentAiController(_settings, ReadSnapshot,
                (action, token) => { LastAction = action; return _execute(action, token); },
                logException: _logException, planning: _planning, runSearchInBackground: _runSearchInBackground);
            controller._lane = lane;
            controller._parent = this;
            controller._worker = _worker;
            return controller;
        }

        private bool CanSubmit(OpponentAiSnapshot state)
        {
            if (_parent == null) return true;
            var primary = OpponentAiArmyRules.NeedsRecruitment(state) ? _parent._purchaseLane : _parent._dragLane;
            if (ReferenceEquals(primary, this)) return true;
            // 独立执行：优先通道在加载、执行、冷却、重试时，不占住另一通道。
            if (primary._executing || primary._actionRemaining > 0f || primary._retryRemaining > 0f ||
                primary._lane == OpponentAiActionLane.Purchase && primary._purchaseTokens <= 0 ||
                primary._lane == OpponentAiActionLane.Drag && primary._moveRemaining > 0f) return true;
            var noAction = primary._noActionSnapshot;
            if (noAction == null || primary._clock - primary._noActionAt >= primary._settings.ScanInterval ||
                noAction.BattleId != state.BattleId || noAction.BoardRevision != state.BoardRevision) return false;
            if (primary._lane == OpponentAiActionLane.Drag) return true;
            if (noAction.EnergyCoins != state.EnergyCoins || noAction.Offers.Count != state.Offers.Count) return false;
            for (var i = 0; i < state.Offers.Count; i++)
                if (noAction.Offers[i].OfferId != state.Offers[i].OfferId || noAction.Offers[i].Price != state.Offers[i].Price) return false;
            return true;
        }

        private void RouteFollowUp(OpponentAiFollowUp follow)
        {
            var target = IsPurchase(follow.Kind) ? _purchaseLane : _dragLane;
            target._pendingFollowUp = follow;
            target._followUpExpires = target._clock + 8f;
            target.MarkDirty();
        }

        public void MarkDirty()
        {
            if (!_disposed) _dirty = true;
            _noActionSnapshot = null;
            _purchaseLane?.MarkDirty();
            _dragLane?.MarkDirty();
        }

        public void Advance(float deltaTime, bool canAct)
        {
            if (_disposed || !_settings.Enabled || !canAct || deltaTime < 0f ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _clock += deltaTime;
            if (_purchaseLane != null)
            {
                // 同一帧的调度顺序按扩军阶段切换，不跨通道共用执行/冷却门禁。
                var recruit = !_armyEstablished || _lastArmyCount < OpponentAiArmyState.MinimumRobots;
                (recruit ? _purchaseLane : _dragLane).Advance(deltaTime, canAct);
                if (!_disposed) (recruit ? _dragLane : _purchaseLane).Advance(deltaTime, canAct);
                return;
            }
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
            _lastArmyCount = OpponentAiArmyRules.Count(snapshot);
            _armyEstablished |= snapshot.Army?.Established == true || _lastArmyCount >= OpponentAiArmyState.RecruitmentTarget;
            return snapshot.WithArmyState(_armyEstablished);
        }

        private void AdvanceSearch(float deltaTime, OpponentAiSnapshot snapshot = null)
        {
            if (_runSearchInBackground && _searchWork == null && _worker.IsBusy)
            {
                if (!_search.IsComplete && _search.BestAction.Kind == OpponentAiActionKind.Wait) _worker.Request(this);
                // 等待另一通道的工作步结束时，不逐帧重复复制场景快照和碰撞轮廓。
                _searchElapsed += deltaTime;
                _partialElapsed += deltaTime;
                return;
            }
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
            if (snapshot.BoardRevision != _search.BoardRevision || !_search.MatchesOptions(liveOptions) || !_search.MatchesShop(snapshot))
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
                if ((completedWork || _searchWork == null) && (_search.IsComplete || _search.BestAction.Kind != OpponentAiActionKind.Wait))
                {
                    var ready = _search.BestAction;
                    // 已有结果后只等待提交仲裁，不再竞争计算名额，否则会阻塞优先通道继续搜索。
                    _worker.Forget(this);
                    if (ready.Kind != OpponentAiActionKind.Wait && !CanSubmit(snapshot)) return;
                    ClearSearch();
                    _scanElapsed = 0f;
                    TryRunAction(snapshot, ready, liveOptions);
                    return;
                }
                var search = _search;
                var publish = _partialElapsed >= _settings.FastScanInterval || _searchElapsed >= _settings.SearchWindow;
                if (publish) _partialElapsed = 0f;
                if (_searchElapsed >= _settings.SearchWindow) _searchElapsed = 0f;
                Task work;
                if (_worker.TrySchedule(this, () => {
                    search.Advance(_settings.SearchCandidatesPerFrame, _settings.SearchMillisecondsPerFrame);
                    if (publish && !search.IsComplete) search.PublishBestPartial();
                }, out work)) _searchWork = work;
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
            if (action.Kind != OpponentAiActionKind.Wait && !CanSubmit(snapshot)) return;
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
                Lane = _lane,
                FollowUpPurchase = _parent != null ? (bool?)(_parent._purchaseLane._purchaseTokens > 0) : null,
                FollowUpMove = _parent != null ? (bool?)(_parent._dragLane._moveRemaining <= 0f) : null,
                AllowPurchase = _lane != OpponentAiActionLane.Drag && _purchaseTokens > 0,
                AllowMove = _lane != OpponentAiActionLane.Purchase && _moveRemaining <= 0f,
                AllowRefresh = _lane != OpponentAiActionLane.Drag && _refreshRemaining <= 0f,
                PreferredOfferId = _preferredOffer,
                BlockedOfferIds = blocked,
                RecentMoves = _runSearchInBackground ? _recentMoves.ToArray() : _recentMovesView,
                ActiveTime = _clock,
                PendingFollowUp = _clock <= _followUpExpires ? _pendingFollowUp : null
            };
        }

        private void TryRunAction(OpponentAiSnapshot snapshot, OpponentAiAction action, OpponentAiPlanningOptions options)
        {
            if (action.Kind == OpponentAiActionKind.Wait)
            {
                _noActionSnapshot = snapshot;
                _noActionAt = _clock;
                return;
            }
            if (_disposed || _executing || action.Kind == OpponentAiActionKind.Wait ||
                action.BattleId != snapshot.BattleId) return;
            if (!CanSubmit(snapshot)) return;
            var purchase = IsPurchase(action.Kind) || action.Kind == OpponentAiActionKind.Refresh;
            if (_lane == OpponentAiActionLane.Purchase && !purchase || _lane == OpponentAiActionLane.Drag && purchase) return;
            if (IsPurchase(action.Kind))
            {
                if (!options.AllowPurchase) return;
                if (_failedOffers.TryGetValue(action.OfferId, out var until) && until > _clock) return;
            }
            if (OpponentAiArmyRules.IsDrag(action.Kind) && !options.AllowMove ||
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
                _pendingFollowUp = _parent == null ? action.FollowUp : null;
                _followUpExpires = _clock + 8f;
                if (_parent != null && action.FollowUp.HasValue) _parent.RouteFollowUp(action.FollowUp.Value);
                _actionRemaining = _settings.MinimumActionInterval;
                if (IsPurchase(action.Kind))
                {
                    _purchaseTokens--;
                    _preferredOffer = 0;
                }
                if (action.FreedOfferId != 0) _preferredOffer = action.FreedOfferId;
                if (OpponentAiArmyRules.IsDrag(action.Kind))
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
            _worker.Forget(this);
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
            _purchaseLane?.Dispose();
            _dragLane?.Dispose();
            ClearSearch();
            _recentMoves.Clear();
            _failedOffers.Clear();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
