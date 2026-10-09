using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GameConfig.robot;
using TEngine;

namespace GameLogic
{
    public sealed partial class BattleShopContext
    {
        private readonly Dictionary<long, PlaceableDefinition> _offerDefinitions = new Dictionary<long, PlaceableDefinition>();

        public bool IsCurrentInteraction(long interactionId) => !IsEnded && !_endRequested &&
            ActiveInteraction != null && ActiveInteraction.BattleId == BattleId &&
            ActiveInteraction.InteractionId == interactionId;

        public ShopOperationResult GetDragContent(BattleSide side, RobotDragSource source,
            out Robot config, out int level, out PlaceableDefinition definition)
        {
            RobotEntity robot;
            ShopSlot slot;
            return ResolveDragSource(side, source, out robot, out slot, out config, out level, out definition);
        }

        public ShopOperationResult TryBeginDrag(BattleSide side, RobotDragSource source, out RobotDragSession session)
        {
            return BeginDrag(side, source, out session, true);
        }

        private ShopOperationResult BeginDrag(BattleSide side, RobotDragSource source,
            out RobotDragSession session, bool notify)
        {
            session = null;
            var gate = CheckMutation(side);
            if (gate != ShopOperationResult.Success) return gate;
            RobotEntity robot;
            ShopSlot slot;
            Robot config;
            int level;
            PlaceableDefinition definition;
            var result = ResolveDragSource(side, source, out robot, out slot, out config, out level, out definition, true);
            if (result != ShopOperationResult.Success) return result;
            result = CheckOfferFunds(side, slot);
            if (result != ShopOperationResult.Success) return result;
            BoardModel board = null;
            if (robot != null && robot.Location == RobotLocation.Board)
            {
                _boards.TryGetValue(robot.InstanceId, out board);
                if (!HasCompleteFootprint(robot)) return ShopOperationResult.InvalidPlacement;
            }
            _committing = true;
            try
            {
                session = new RobotDragSession(Interlocked.Increment(ref _lastInteractionId), BattleId, side, source,
                    robot != null ? robot.Location : RobotLocation.Unplaced, board, robot?.Anchor);
                ActiveInteraction = session;
                if (robot != null)
                {
                    robot.TrySetDragging(true);
                    if (board != null) board.Release(definition.Id);
                }
                if (notify) Notify(side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public void CancelInteraction(long interactionId)
        {
            if (ActiveInteraction == null || ActiveInteraction.InteractionId != interactionId) return;
            // 通知内取消在当前事务退出时执行，保留精确会话ID，不能取消后续手势。
            if (_committing) { _cancelRequestedInteractionId = interactionId; return; }
            _committing = true;
            try
            {
                var side = ActiveInteraction.Side;
                if (!RestoreInteraction()) return;
                CompleteInteraction();
                Notify(side);
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult CheckMerge(BattleSide side, RobotDragSource source, int targetInstanceId)
        {
            RobotEntity sourceRobot;
            ShopSlot slot;
            Robot config;
            int level;
            PlaceableDefinition definition;
            var result = ResolveDragSource(side, source, out sourceRobot, out slot, out config, out level, out definition);
            if (result != ShopOperationResult.Success) return result;
            RobotEntity target;
            result = FindOwned(side, targetInstanceId, out target);
            if (result != ShopOperationResult.Success) return result;
            if (sourceRobot == target) return ShopOperationResult.IncompatibleMerge;
            if (target.Location != RobotLocation.Board || target.IsDragging) return ShopOperationResult.InvalidLocation;
            if (!HasCompleteFootprint(target)) return ShopOperationResult.InvalidPlacement;
            if (config.Id != target.ConfigId || level != target.Level) return ShopOperationResult.IncompatibleMerge;
            if (level >= 5 || level >= Rules.MaxRobotLevel || level >= target.MaxLevel)
                return ShopOperationResult.MaxLevelReached;
            return ShopOperationResult.Success;
        }

        public ShopOperationResult TryCommitMerge(long interactionId, int targetInstanceId)
        {
            RobotDragSession session;
            var gate = CheckInteractionCommit(interactionId, out session);
            if (gate != ShopOperationResult.Success) return gate;
            var result = CheckMerge(session.Side, session.Source, targetInstanceId);
            if (result != ShopOperationResult.Success) return result;
            RobotEntity target = _owned[targetInstanceId];
            RobotEntity sourceRobot;
            ShopSlot slot;
            Robot config;
            int level;
            PlaceableDefinition definition;
            result = ResolveDragSource(session.Side, session.Source, out sourceRobot, out slot,
                out config, out level, out definition, true);
            if (result != ShopOperationResult.Success) return result;
            result = CheckOfferFunds(session.Side, slot);
            if (result != ShopOperationResult.Success) return result;
            _committing = true;
            try
            {
                var previousLevel = target.Level;
                if (!target.TryUpgrade()) return ShopOperationResult.IncompatibleMerge;
                if (sourceRobot != null)
                {
                    DetachSource(sourceRobot);
                    EntityRemovalSnapshot removal;
                    Registry.Remove(sourceRobot.InstanceId, EntityRemovalReason.Consumed, out removal);
                    _owned.Remove(sourceRobot.InstanceId);
                }
                else ConsumeOffer(session.Side, slot);
                CompleteInteraction();
                var snapshot = new RobotMergeSnapshot(BattleId, session.InteractionId, session.Side, session.Source,
                    targetInstanceId, previousLevel, target.Level);
                // 两种通知均在最终状态后发出；通知期间拒绝重入，终局推迟到finally。
                GameEvent.Send<RobotMergeSnapshot>(BattleShopEvents.Merged, snapshot);
                Notify(session.Side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryCommitDeploy(long interactionId, BattleSide boardSide, BoardModel board,
            BoardCoordinate anchor, out RobotEntity robot)
        {
            robot = null;
            RobotDragSession session;
            var gate = CheckInteractionCommit(interactionId, out session);
            if (gate != ShopOperationResult.Success) return gate;
            if (!IsValidSide(boardSide) || board == null) return ShopOperationResult.InvalidArgument;
            if (boardSide != session.Side) return ShopOperationResult.WrongSide;
            BattleSide registeredSide;
            if (_boardSides.TryGetValue(board, out registeredSide) && registeredSide != session.Side)
                return ShopOperationResult.WrongSide;
            RobotEntity owned;
            ShopSlot slot;
            Robot config;
            int level;
            PlaceableDefinition definition;
            var result = ResolveDragSource(session.Side, session.Source, out owned, out slot,
                out config, out level, out definition, true);
            if (result != ShopOperationResult.Success) return result;
            result = CheckOfferFunds(session.Side, slot);
            if (result != ShopOperationResult.Success) return result;
            if (!board.Evaluate(anchor, definition).IsValid) return ShopOperationResult.InvalidPlacement;
            _committing = true;
            try
            {
                robot = owned;
                if (robot == null)
                {
                    result = CreateOfferRobot(session.Side, slot, out robot);
                    if (result != ShopOperationResult.Success) return result;
                }
                // 无await/通知，Evaluate后占格不会被其他Context操作改变。
                board.TryPlace(anchor, robot.Definition);
                if (owned != null) DetachSource(owned);
                else ConsumeOffer(session.Side, slot);
                _boards[robot.InstanceId] = board;
                _boardSides[board] = session.Side;
                robot.TrySetLocation(RobotLocation.Board, anchor);
                CompleteInteraction();
                Notify(session.Side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public ShopOperationResult TryCommitStorage(long interactionId, out RobotEntity robot)
        {
            robot = null;
            RobotDragSession session;
            var gate = CheckInteractionCommit(interactionId, out session);
            if (gate != ShopOperationResult.Success) return gate;
            RobotEntity owned;
            ShopSlot sourceSlot;
            Robot config;
            int level;
            PlaceableDefinition definition;
            var result = ResolveDragSource(session.Side, session.Source, out owned, out sourceSlot,
                out config, out level, out definition, true);
            if (result != ShopOperationResult.Success) return result;
            var state = GetState(session.Side);
            RobotEntity stored = null;
            var alreadyStored = owned != null && owned.Location == RobotLocation.Storage;
            if (state.StorageInstanceId.HasValue && !alreadyStored)
            {
                if (sourceSlot == null)
                {
                    // 场上来源不能交换到商品槽；满存储自动回位并结束本手势。
                    CancelInteraction(interactionId);
                    return ShopOperationResult.StorageFull;
                }
                result = FindOwned(session.Side, state.StorageInstanceId.Value, out stored);
                if (result != ShopOperationResult.Success) return result;
            }
            result = CheckOfferFunds(session.Side, sourceSlot);
            if (result != ShopOperationResult.Success) return result;
            _committing = true;
            try
            {
                robot = owned;
                if (!alreadyStored)
                {
                    if (robot == null)
                    {
                        result = CreateOfferRobot(session.Side, sourceSlot, out robot);
                        if (result != ShopOperationResult.Success) return result;
                        ConsumeOffer(session.Side, sourceSlot);
                    }
                    else DetachSource(robot);
                    if (stored != null)
                    {
                        sourceSlot.InstanceId = stored.InstanceId;
                        stored.TrySetLocation(RobotLocation.PurchasedShopSlot, null);
                    }
                    state.StorageInstanceId = robot.InstanceId;
                    robot.TrySetLocation(RobotLocation.Storage, null);
                }
                CompleteInteraction();
                Notify(session.Side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }

        public bool TryCreateActionToken(int instanceId, out RobotActionToken token)
        {
            token = default(RobotActionToken);
            RobotEntity robot;
            Entity entity;
            if (IsEnded || _endRequested || _committing || !_owned.TryGetValue(instanceId, out robot) ||
                !Registry.TryGet(instanceId, out entity) || !ReferenceEquals(entity, robot) || !robot.CanParticipate)
                return false;
            token = new RobotActionToken(BattleId, instanceId, robot.ActionVersion, robot.Level);
            return true;
        }

        public bool IsActionTokenValid(RobotActionToken token)
        {
            RobotEntity robot;
            Entity entity;
            return !IsEnded && !_endRequested && token.BattleId == BattleId &&
                _owned.TryGetValue(token.InstanceId, out robot) && Registry.TryGet(token.InstanceId, out entity) &&
                ReferenceEquals(entity, robot) && !robot.IsRemoved &&
                token.ActionVersion == robot.ActionVersion && token.Level == robot.Level;
        }

        private ShopOperationResult CheckInteractionCommit(long interactionId, out RobotDragSession session)
        {
            session = ActiveInteraction;
            if (IsEnded || _endRequested) return ShopOperationResult.BattleEnded;
            if (_committing) return ShopOperationResult.Busy;
            if (session == null || session.InteractionId != interactionId) return ShopOperationResult.InvalidSource;
            if (session.BattleId != BattleId) return ShopOperationResult.WrongBattle;
            return ShopOperationResult.Success;
        }

        private ShopOperationResult BeginLegacyCommit(BattleSide side, int instanceId,
            out RobotDragSession session, out bool created)
        {
            session = ActiveInteraction;
            created = false;
            var gate = CheckMutation(side, instanceId);
            if (gate != ShopOperationResult.Success) return gate;
            if (session != null) return ShopOperationResult.Success;
            var result = BeginDrag(side, RobotDragSource.Owned(instanceId), out session, false);
            created = result == ShopOperationResult.Success;
            return result;
        }

        private void FinishLegacyCommit(RobotDragSession session, bool created, ShopOperationResult result)
        {
            if (created && result != ShopOperationResult.Success && IsCurrentInteraction(session.InteractionId))
            {
                if (RestoreInteraction()) CompleteInteraction();
            }
        }

        private bool IsLiftedBoardSource(RobotEntity robot) => ActiveInteraction != null &&
            ActiveInteraction.Source.Kind == RobotDragSourceKind.OwnedRobot &&
            ActiveInteraction.Source.InstanceId == robot.InstanceId &&
            ActiveInteraction.OriginalLocation == RobotLocation.Board;

        private bool RestoreInteraction()
        {
            var session = ActiveInteraction;
            if (session == null || session.OriginalBoard == null) return true;
            RobotEntity robot;
            if (!_owned.TryGetValue(session.Source.InstanceId, out robot) || robot.IsRemoved) return true;
            // 全局交互锁保证Context内没有其他操作占用原位。外部写入冲突时保持会话，可重试取消。
            if (!session.OriginalBoard.TryPlace(session.OriginalAnchor.Value, robot.Definition)) return false;
            robot.TrySetLocation(session.OriginalLocation, session.OriginalAnchor);
            return true;
        }

        private ShopOperationResult ResolveDragSource(BattleSide side, RobotDragSource source,
            out RobotEntity robot, out ShopSlot slot, out Robot config, out int level,
            out PlaceableDefinition definition, bool validateOfferConfig = false)
        {
            robot = null;
            slot = null;
            config = null;
            level = 0;
            definition = null;
            if (IsEnded || _endRequested) return ShopOperationResult.BattleEnded;
            if (!IsValidSide(side)) return ShopOperationResult.InvalidArgument;
            if (source.Kind == RobotDragSourceKind.OwnedRobot)
            {
                var result = FindOwned(side, source.InstanceId, out robot);
                if (result != ShopOperationResult.Success) return result;
                if (robot.Definition == null || robot.Configuration == null) return ShopOperationResult.InvalidConfiguration;
                if (robot.IsDragging && !IsActiveSource(side, source)) return ShopOperationResult.InvalidLocation;
                if (IsActiveSource(side, source))
                {
                    if (!robot.IsDragging || robot.Location != ActiveInteraction.OriginalLocation ||
                        !Nullable.Equals(robot.Anchor, ActiveInteraction.OriginalAnchor)) return ShopOperationResult.InvalidLocation;
                    BoardModel original;
                    if (ActiveInteraction.OriginalBoard != null &&
                        (!_boards.TryGetValue(robot.InstanceId, out original) || !ReferenceEquals(original, ActiveInteraction.OriginalBoard)))
                        return ShopOperationResult.InvalidLocation;
                }
                else if (robot.Location == RobotLocation.Board && !HasCompleteFootprint(robot))
                    return ShopOperationResult.InvalidPlacement;
                slot = FindSlot(GetState(side), robot.InstanceId);
                config = robot.Configuration;
                level = robot.Level;
                definition = robot.Definition;
                return ShopOperationResult.Success;
            }
            if (source.Kind != RobotDragSourceKind.ShopOffer) return ShopOperationResult.InvalidArgument;
            var state = GetState(side);
            if (source.SlotId < 0 || source.SlotId >= state.Slots.Count) return ShopOperationResult.InvalidSlot;
            slot = state.Slots[source.SlotId];
            if (slot.IsPurchased) return ShopOperationResult.AlreadyPurchased;
            var offer = slot.Offer;
            if (offer == null || offer.OfferId != source.OfferId) return ShopOperationResult.StaleOffer;
            if (!_configs.TryGetValue(offer.RobotId, out config)) return ShopOperationResult.InvalidConfiguration;
            if (validateOfferConfig || !_offerDefinitions.TryGetValue(offer.OfferId, out definition))
            {
                try
                {
                    // 预览读取复用定义；拿起及提交重新校验，并按当前形状重建，避免缓存掩盖配置失效。
                    definition = ValidateConfig(offer.RobotId, config, "offer:" + BattleId.ToString(CultureInfo.InvariantCulture) + ":" +
                        offer.OfferId.ToString(CultureInfo.InvariantCulture));
                }
                catch (ArgumentException) { return ShopOperationResult.InvalidConfiguration; }
                _offerDefinitions[offer.OfferId] = definition;
            }
            level = offer.Level;
            return ShopOperationResult.Success;
        }

        private bool IsActiveSource(BattleSide side, RobotDragSource source) => ActiveInteraction != null &&
            ActiveInteraction.Side == side && ActiveInteraction.Source.Kind == source.Kind &&
            ActiveInteraction.Source.InstanceId == source.InstanceId && ActiveInteraction.Source.SlotId == source.SlotId &&
            ActiveInteraction.Source.OfferId == source.OfferId;

        private bool HasCompleteFootprint(RobotEntity robot)
        {
            BoardModel board;
            if (robot.Definition == null || !robot.Anchor.HasValue || !_boards.TryGetValue(robot.InstanceId, out board)) return false;
            var anchor = robot.Anchor.Value;
            foreach (var offset in robot.Definition.Cells)
            {
                var column = (long)anchor.Column + offset.Column;
                var row = (long)anchor.Row + offset.Row;
                if (column < 0 || column >= board.Columns || row < 0 || row >= board.Rows) return false;
                var cell = new BoardCoordinate((int)column, (int)row);
                if (!board.IsOpen(cell) || board.GetOccupant(cell) != robot.Definition.Id) return false;
            }
            var count = 0;
            for (var column = 0; column < board.Columns; column++)
                for (var row = 0; row < board.Rows; row++)
                    if (board.GetOccupant(new BoardCoordinate(column, row)) == robot.Definition.Id) count++;
            return count == robot.Definition.Cells.Count;
        }

        private ShopOperationResult CheckOfferFunds(BattleSide side, ShopSlot slot) =>
            slot != null && slot.Offer != null && GetState(side).EnergyCoins < slot.Offer.Price
                ? ShopOperationResult.InsufficientCoins : ShopOperationResult.Success;

        private ShopOperationResult CreateOfferRobot(BattleSide side, ShopSlot slot, out RobotEntity robot)
        {
            robot = null;
            try { robot = Registry.CreateRobotFromConfig(_configs[slot.Offer.RobotId], side, slot.Offer.Level, Rules.MaxRobotLevel); }
            catch (ArgumentException) { return ShopOperationResult.InvalidConfiguration; }
            _owned.Add(robot.InstanceId, robot);
            return ShopOperationResult.Success;
        }

        private void ConsumeOffer(BattleSide side, ShopSlot slot)
        {
            GetState(side).EnergyCoins -= slot.Offer.Price;
            _offerDefinitions.Remove(slot.Offer.OfferId);
            slot.Offer = null;
        }
    }
}
