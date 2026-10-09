using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GameConfig.robot;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// 单局双方商店事务。购买即扣费并创建实体，交互不移除来源；失败由调用方取消交互。
    /// 主线程使用；Registry用于读取和战斗实体管理，已购机器人的移除由本Context完成。
    /// </summary>
    public sealed class BattleShopContext : IDisposable
    {
        private static long _lastBattleId;
        private readonly BattleShopState[] _states;
        private readonly int[][] _candidates;
        private readonly Dictionary<int, double>[][] _slotWeights;
        private readonly Dictionary<int, Robot> _configs = new Dictionary<int, Robot>();
        private readonly Dictionary<int, RobotEntity> _owned = new Dictionary<int, RobotEntity>();
        private readonly Dictionary<int, BoardModel> _boards = new Dictionary<int, BoardModel>();
        private readonly Dictionary<BoardModel, BattleSide> _boardSides = new Dictionary<BoardModel, BattleSide>();
        private readonly HashSet<RewardKey> _rewardKeys = new HashSet<RewardKey>();
        private readonly IShopRandom _random;
        private long _nextOfferId = 1;
        private int? _interactionId;
        private BattleSide _interactionSide;
        private bool _committing;
        private bool _endRequested;

        public long BattleId { get; } = Interlocked.Increment(ref _lastBattleId);
        public bool IsEnded { get; private set; }
        public bool IsBusy => _interactionId.HasValue;
        public ShopRules Rules { get; }
        public int RefreshPrice => Rules.RefreshPrice;
        public BattleEntityRegistry Registry { get; } = new BattleEntityRegistry();
        public ShopRewardService Rewards { get; }

        public BattleShopContext(ShopRules rules, IReadOnlyList<int> playerCandidates,
            IReadOnlyList<int> opponentCandidates, Func<int, Robot> robotResolver, IShopRandom random = null)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (robotResolver == null) throw new ArgumentNullException(nameof(robotResolver));
            _random = random ?? new ShopRandom();
            _candidates = new[] { CopyCandidates(playerCandidates), CopyCandidates(opponentCandidates) };
            _slotWeights = new[] { MapSlotWeights(_candidates[0]), MapSlotWeights(_candidates[1]) };
            foreach (var candidates in _candidates)
                foreach (var id in candidates)
                    if (!_configs.ContainsKey(id))
                    {
                        var config = robotResolver(id);
                        ValidateConfig(id, config);
                        _configs.Add(id, config);
                    }
            _states = new[]
            {
                new BattleShopState(BattleSide.Player, rules),
                new BattleShopState(BattleSide.Opponent, rules)
            };
            Rewards = new ShopRewardService(this);
            foreach (var state in _states) ApplyBatch(state, GenerateBatch(state, true));
        }

        public BattleShopState GetState(BattleSide side)
        {
            EntityValidation.RequireSide(side);
            return _states[(int)side];
        }

        public ShopOperationResult TryPurchase(BattleSide side, int slotId, long offerId, out RobotEntity robot)
        {
            robot = null;
            var gate = CheckMutation(side);
            if (gate != ShopOperationResult.Success) return gate;
            var state = GetState(side);
            if (slotId < 0 || slotId >= state.Slots.Count) return ShopOperationResult.InvalidSlot;
            var slot = state.Slots[slotId];
            if (slot.IsPurchased) return ShopOperationResult.AlreadyPurchased;
            var offer = slot.Offer;
            if (offer == null || offer.OfferId != offerId) return ShopOperationResult.StaleOffer;
            if (state.EnergyCoins < offer.Price) return ShopOperationResult.InsufficientCoins;
            _committing = true;
            try
            {
                try { robot = Registry.CreateRobotFromConfig(_configs[offer.RobotId], side, offer.Level, Rules.MaxRobotLevel); }
                catch (ArgumentException) { return ShopOperationResult.InvalidConfiguration; }
                robot.TrySetLocation(RobotLocation.PurchasedShopSlot, null);
                _owned.Add(robot.InstanceId, robot);
                state.EnergyCoins -= offer.Price;
                slot.Offer = null;
                slot.InstanceId = robot.InstanceId;
                Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryDeploy(BattleSide side, int instanceId, BattleSide boardSide,
            BoardModel board, BoardCoordinate anchor)
        {
            var gate = CheckMutation(side, instanceId);
            if (gate != ShopOperationResult.Success) return gate;
            if (!IsValidSide(boardSide) || board == null) return ShopOperationResult.InvalidArgument;
            if (boardSide != side) return ShopOperationResult.WrongSide;
            BattleSide registeredSide;
            if (_boardSides.TryGetValue(board, out registeredSide) && registeredSide != side)
                return ShopOperationResult.WrongSide;
            RobotEntity robot;
            var source = FindOwned(side, instanceId, out robot);
            if (source != ShopOperationResult.Success) return source;
            if (!IsMovableSource(robot)) return ShopOperationResult.InvalidLocation;
            _committing = true;
            try
            {
                if (!board.TryPlace(anchor, robot.Definition))
                    return ShopOperationResult.InvalidPlacement;
                DetachSource(robot);
                _boards[instanceId] = board;
                _boardSides[board] = side;
                robot.TrySetLocation(RobotLocation.Board, anchor);
                CompleteInteraction();
                Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryMerge(BattleSide side, int instanceId, int targetInstanceId)
        {
            var gate = CheckMutation(side, instanceId);
            if (gate != ShopOperationResult.Success) return gate;
            RobotEntity sourceRobot;
            RobotEntity targetRobot;
            var source = FindOwned(side, instanceId, out sourceRobot);
            if (source != ShopOperationResult.Success) return source;
            if (!IsMovableSource(sourceRobot)) return ShopOperationResult.InvalidLocation;
            var target = FindOwned(side, targetInstanceId, out targetRobot);
            if (target != ShopOperationResult.Success) return target;
            if (targetRobot.Location != RobotLocation.Board) return ShopOperationResult.InvalidLocation;
            if (instanceId == targetInstanceId || sourceRobot.ConfigId != targetRobot.ConfigId || sourceRobot.Level != targetRobot.Level)
                return ShopOperationResult.IncompatibleMerge;
            if (targetRobot.Level >= Rules.MaxRobotLevel || targetRobot.Level >= targetRobot.MaxLevel)
                return ShopOperationResult.MaxLevelReached;
            _committing = true;
            try
            {
                if (!targetRobot.TryUpgrade()) return ShopOperationResult.IncompatibleMerge;
                DetachSource(sourceRobot);
                EntityRemovalSnapshot removal;
                Registry.Remove(instanceId, EntityRemovalReason.Consumed, out removal);
                _owned.Remove(instanceId);
                CompleteInteraction();
                Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryStoreOrSwap(BattleSide side, int instanceId)
        {
            var gate = CheckMutation(side, instanceId);
            if (gate != ShopOperationResult.Success) return gate;
            RobotEntity robot;
            var source = FindOwned(side, instanceId, out robot);
            if (source != ShopOperationResult.Success) return source;
            if (!IsMovableSource(robot)) return ShopOperationResult.InvalidLocation;
            var state = GetState(side);
            var sourceSlot = FindSlot(state, instanceId);
            RobotEntity stored = null;
            if (state.StorageInstanceId.HasValue && robot.Location != RobotLocation.Storage)
            {
                if (sourceSlot == null) return ShopOperationResult.StorageFull;
                var storage = FindOwned(side, state.StorageInstanceId.Value, out stored);
                if (storage != ShopOperationResult.Success) return storage;
            }
            _committing = true;
            try
            {
                if (robot.Location == RobotLocation.Storage)
                {
                    CompleteInteraction();
                    Notify(side);
                    return ShopOperationResult.Success;
                }
                DetachSource(robot);
                if (stored != null)
                {
                    sourceSlot.InstanceId = stored.InstanceId;
                    stored.TrySetLocation(RobotLocation.PurchasedShopSlot, null);
                }
                state.StorageInstanceId = instanceId;
                robot.TrySetLocation(RobotLocation.Storage, null);
                CompleteInteraction();
                Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryRefresh(BattleSide side)
        {
            var gate = CheckMutation(side);
            if (gate != ShopOperationResult.Success) return gate;
            var state = GetState(side);
            if (state.EnergyCoins < RefreshPrice) return ShopOperationResult.InsufficientCoins;
            _committing = true;
            try
            {
                ShopOffer[] batch;
                try { batch = GenerateBatch(state, false); }
                catch (InvalidOperationException) { return ShopOperationResult.InvalidConfiguration; }
                // 抽取成功前不扣币、不销毁；抽取失败可以重试。
                foreach (var slot in state.Slots)
                {
                    if (slot.InstanceId.HasValue)
                    {
                        EntityRemovalSnapshot removal;
                        Registry.Remove(slot.InstanceId.Value, EntityRemovalReason.Manual, out removal);
                        _owned.Remove(slot.InstanceId.Value);
                    }
                    slot.InstanceId = null;
                    slot.Offer = null;
                }
                state.EnergyCoins -= RefreshPrice;
                ApplyBatch(state, batch);
                Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public bool TryBeginInteraction(BattleSide side, int instanceId)
        {
            if (CheckMutation(side) != ShopOperationResult.Success) return false;
            RobotEntity robot;
            if (FindOwned(side, instanceId, out robot) != ShopOperationResult.Success) return false;
            if (!IsMovableSource(robot)) return false;
            _committing = true;
            try
            {
                _interactionSide = side;
                _interactionId = instanceId;
                robot.TrySetDragging(true);
                Notify(side);
                return true;
            }
            finally { FinishMutation(); }
        }

        public void CancelInteraction()
        {
            if (_committing || !_interactionId.HasValue) return;
            var side = _interactionSide;
            _committing = true;
            try
            {
                CompleteInteraction();
                Notify(side);
            }
            finally { FinishMutation(); }
        }

        public void EndBattle()
        {
            if (IsEnded) return;
            // 事件回调中请求终局时，等当前事务通知完成后统一清理。
            if (_committing) { _endRequested = true; return; }
            _committing = true;
            IsEnded = true;
            try
            {
                CompleteInteraction();
                foreach (var pair in _boards)
                    pair.Value.Release(pair.Key.ToString(CultureInfo.InvariantCulture));
                _boards.Clear();
                _boardSides.Clear();
                foreach (var state in _states)
                {
                    state.StorageInstanceId = null;
                    foreach (var slot in state.Slots) { slot.InstanceId = null; slot.Offer = null; }
                }
                Registry.Dispose();
                _owned.Clear();
                _rewardKeys.Clear();
                foreach (var state in _states) Notify(state.Side);
            }
            finally { FinishMutation(); }
        }

        public void Dispose() { EndBattle(); }

        internal ShopOperationResult TryGrantReward(long battleId, ShopRewardKind kind, string sourceId,
            BattleSide recipient, int coins)
        {
            var gate = CheckMutation(recipient, allowInteraction: true);
            if (gate != ShopOperationResult.Success) return gate;
            if (battleId != BattleId) return ShopOperationResult.WrongBattle;
            if (string.IsNullOrWhiteSpace(sourceId) || coins <= 0) return ShopOperationResult.InvalidArgument;
            int enemyId;
            if (kind == ShopRewardKind.EnemyKill && (!int.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out enemyId) || enemyId <= 0))
                return ShopOperationResult.InvalidArgument;
            var key = new RewardKey(battleId, kind, sourceId);
            if (_rewardKeys.Contains(key)) return ShopOperationResult.DuplicateReward;
            var state = GetState(recipient);
            if (state.EnergyCoins > int.MaxValue - coins) return ShopOperationResult.CoinOverflow;
            _committing = true;
            try
            {
                state.EnergyCoins += coins;
                _rewardKeys.Add(key);
                Notify(recipient);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        private ShopOperationResult CheckMutation(BattleSide side, int? instanceId = null, bool allowInteraction = false)
        {
            if (IsEnded || _endRequested) return ShopOperationResult.BattleEnded;
            if (!IsValidSide(side)) return ShopOperationResult.InvalidArgument;
            if (_committing || (!allowInteraction && _interactionId.HasValue &&
                (_interactionId != instanceId || _interactionSide != side))) return ShopOperationResult.Busy;
            return ShopOperationResult.Success;
        }

        private static bool IsValidSide(BattleSide side)
        {
            return side == BattleSide.Player || side == BattleSide.Opponent;
        }

        private ShopOperationResult FindOwned(BattleSide side, int instanceId, out RobotEntity robot)
        {
            Entity entity;
            robot = null;
            if (!_owned.TryGetValue(instanceId, out robot) || !Registry.TryGet(instanceId, out entity) ||
                !ReferenceEquals(entity, robot) || robot.IsRemoved) return ShopOperationResult.EntityNotFound;
            if (robot.Side != side) return ShopOperationResult.WrongSide;
            var state = GetState(side);
            switch (robot.Location)
            {
                case RobotLocation.PurchasedShopSlot:
                    if (FindSlot(state, instanceId) != null) return ShopOperationResult.Success;
                    break;
                case RobotLocation.Storage:
                    if (state.StorageInstanceId == instanceId) return ShopOperationResult.Success;
                    break;
                case RobotLocation.Board:
                    if (_boards.ContainsKey(instanceId) && robot.Anchor.HasValue) return ShopOperationResult.Success;
                    break;
            }
            return ShopOperationResult.InvalidLocation;
        }

        private static ShopSlot FindSlot(BattleShopState state, int instanceId)
        {
            foreach (var slot in state.Slots) if (slot.InstanceId == instanceId) return slot;
            return null;
        }

        private static bool IsMovableSource(RobotEntity robot)
        {
            return robot.Location == RobotLocation.PurchasedShopSlot || robot.Location == RobotLocation.Storage;
        }

        private void DetachSource(RobotEntity robot)
        {
            var state = GetState(robot.Side);
            var slot = FindSlot(state, robot.InstanceId);
            if (slot != null) slot.InstanceId = null;
            if (state.StorageInstanceId == robot.InstanceId) state.StorageInstanceId = null;
            BoardModel board;
            if (_boards.TryGetValue(robot.InstanceId, out board))
            {
                board.Release(robot.InstanceId.ToString(CultureInfo.InvariantCulture));
                _boards.Remove(robot.InstanceId);
            }
        }

        private void CompleteInteraction()
        {
            if (_interactionId.HasValue)
            {
                RobotEntity robot;
                if (_owned.TryGetValue(_interactionId.Value, out robot)) robot.TrySetDragging(false);
            }
            _interactionId = null;
        }

        private void Notify(BattleSide side) { GameEvent.Send<int>(BattleShopEvents.Changed, (int)side); }

        private void FinishMutation()
        {
            _committing = false;
            if (_endRequested)
            {
                _endRequested = false;
                EndBattle();
            }
        }

        private int[] CopyCandidates(IReadOnlyList<int> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Count != Rules.SlotCount) throw new ArgumentException("必须传入三个候选：1001、1002、1003。", nameof(source));
            var copy = new int[source.Count];
            var unique = new HashSet<int>();
            for (var index = 0; index < source.Count; index++)
            {
                var id = source[index];
                if (id < 1001 || id > 1003 || !unique.Add(id)) throw new ArgumentException("候选必须是1001..1003且不重复。", nameof(source));
                copy[index] = id;
            }
            return copy;
        }

        private Dictionary<int, double>[] MapSlotWeights(int[] candidates)
        {
            var slots = new Dictionary<int, double>[Rules.SlotCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var weights = new Dictionary<int, double>();
                var hasPositive = false;
                for (var index = 0; index < candidates.Length; index++)
                {
                    var weight = Rules.GetCandidateWeight(slot, index, candidates[index]);
                    weights.Add(candidates[index], weight);
                    hasPositive |= weight > 0;
                }
                if (!hasPositive) throw new ArgumentException("每槽至少有一个正权重候选。");
                slots[slot] = weights;
            }
            return slots;
        }

        private void ValidateConfig(int id, Robot config)
        {
            if (config == null || config.Id != id || config.ShapeId <= 0 || config.SkillId < 0 ||
                config.ShapeId_Ref == null || config.ShapeId_Ref.Id != config.ShapeId ||
                config.ShapeId_Ref.CellOffsets == null || string.IsNullOrWhiteSpace(config.RobotPrefab) ||
                string.IsNullOrWhiteSpace(config.RobotIcon)) throw new ArgumentException("无效的机器人配置：" + id);
            Rules.GetPrice(config.QualityId, 1);
            var cells = new List<BoardCoordinate>();
            foreach (var cell in config.ShapeId_Ref.CellOffsets)
            {
                if (cell == null || cell.Length != 2) throw new ArgumentException("机器人占格必须是坐标二元组。");
                cells.Add(new BoardCoordinate(cell[0], cell[1]));
            }
            new PlaceableDefinition("config", cells);
        }

        private ShopOffer[] GenerateBatch(BattleShopState state, bool initial)
        {
            var candidates = _candidates[(int)state.Side];
            var weights = _slotWeights[(int)state.Side];
            var ids = DrawIds(candidates, weights);
            if (!initial)
            {
                var retries = 0;
                while (SameMultiset(ids, state.LastBatchRobotIds) && retries < Rules.AdditionalRerollLimit)
                {
                    ids = DrawIds(candidates, weights);
                    retries++;
                }
                if (SameMultiset(ids, state.LastBatchRobotIds))
                {
                    var slot = _random.NextInt(ids.Length);
                    if (slot < 0 || slot >= ids.Length) throw new InvalidOperationException("随机源返回越界槽位。");
                    var unseen = new List<int>();
                    foreach (var id in candidates)
                        if (Array.IndexOf(ids, id) < 0 && weights[slot][id] > 0) unseen.Add(id);
                    if (unseen.Count > 0)
                    {
                        ids[slot] = unseen.Count == 1 ? unseen[0] : DrawWeighted(unseen, weights[slot]);
                    }
                }
            }
            // 预检ID范围，任何生成失败都不推进OfferId、不消费金币。
            if (_nextOfferId > long.MaxValue - ids.Length) throw new InvalidOperationException("OfferId已耗尽。");
            var offers = new ShopOffer[ids.Length];
            for (var index = 0; index < ids.Length; index++)
            {
                var level = initial ? 1 : (NextUnit() < Rules.SecondStarProbability ? 2 : 1);
                offers[index] = new ShopOffer(_nextOfferId + index, ids[index], level,
                    Rules.GetPrice(_configs[ids[index]].QualityId, level));
            }
            return offers;
        }

        private void ApplyBatch(BattleShopState state, ShopOffer[] offers)
        {
            var ids = new List<int>(offers.Length);
            for (var index = 0; index < offers.Length; index++)
            {
                state.Slots[index].Offer = offers[index];
                ids.Add(offers[index].RobotId);
            }
            state.LastBatchRobotIds = ids.AsReadOnly();
            _nextOfferId += offers.Length;
        }

        private int[] DrawIds(IReadOnlyList<int> candidates, Dictionary<int, double>[] slotWeights)
        {
            var result = new int[Rules.SlotCount];
            for (var index = 0; index < result.Length; index++) result[index] = DrawWeighted(candidates, slotWeights[index]);
            return result;
        }

        private int DrawWeighted(IReadOnlyList<int> candidates, Dictionary<int, double> weights)
        {
            // 按最大权重缩放，避免合法大权重相加溢出。
            var max = 0d;
            foreach (var id in candidates) max = Math.Max(max, weights[id]);
            if (max <= 0) throw new InvalidOperationException("没有正权重候选。");
            var total = 0d;
            foreach (var id in candidates) total += weights[id] / max;
            var roll = NextUnit() * total;
            var cumulative = 0d;
            var lastPositive = 0;
            foreach (var id in candidates)
            {
                var weight = weights[id];
                if (weight <= 0) continue;
                lastPositive = id;
                cumulative += weight / max;
                if (roll < cumulative) return id;
            }
            return lastPositive;
        }

        private double NextUnit()
        {
            var value = _random.NextDouble();
            if (double.IsNaN(value) || value < 0 || value >= 1) throw new InvalidOperationException("随机浮点数必须在[0,1)内。");
            return value;
        }

        private static bool SameMultiset(int[] ids, IReadOnlyList<int> previous)
        {
            if (ids.Length != previous.Count) return false;
            var first = (int[])ids.Clone();
            var second = new int[previous.Count];
            for (var index = 0; index < previous.Count; index++) second[index] = previous[index];
            Array.Sort(first);
            Array.Sort(second);
            for (var index = 0; index < first.Length; index++) if (first[index] != second[index]) return false;
            return true;
        }

        private readonly struct RewardKey : IEquatable<RewardKey>
        {
            private readonly long _battleId;
            private readonly ShopRewardKind _kind;
            private readonly string _sourceId;
            internal RewardKey(long battleId, ShopRewardKind kind, string sourceId)
            {
                _battleId = battleId;
                _kind = kind;
                _sourceId = sourceId;
            }
            public bool Equals(RewardKey other) => _battleId == other._battleId && _kind == other._kind && _sourceId == other._sourceId;
            public override bool Equals(object obj) => obj is RewardKey other && Equals(other);
            public override int GetHashCode() => (_battleId.GetHashCode() * 397 ^ (int)_kind) * 397 ^ _sourceId.GetHashCode();
        }
    }
}
