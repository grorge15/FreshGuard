namespace GameLogic
{
    public sealed partial class BattleShopContext
    {
        private enum ShopDropAction { Keep, Return, MergeOffer, MergeOwned }

        public ShopOperationResult CheckShopDrop(BattleSide side, RobotDragSource source, int? targetSlotId)
        {
            ShopDropAction action;
            return ResolveShopDrop(side, source, targetSlotId, out action);
        }

        // 预览与提交共用分支；场上机器人可合成或返回有空位的商店。
        private ShopOperationResult ResolveShopDrop(BattleSide side, RobotDragSource source, int? targetSlotId,
            out ShopDropAction action)
        {
            action = ShopDropAction.Keep;
            RobotEntity robot;
            ShopSlot sourceSlot;
            GameConfig.robot.Robot config;
            int level;
            PlaceableDefinition definition;
            var result = ResolveDragSource(side, source, out robot, out sourceSlot, out config, out level, out definition, true);
            if (result != ShopOperationResult.Success) return result;
            var state = GetState(side);
            var target = targetSlotId.HasValue ? state.FindSlot(targetSlotId.Value) : null;
            if (targetSlotId.HasValue && target == null) return ShopOperationResult.InvalidSlot;
            if (target == sourceSlot && sourceSlot != null) return ShopOperationResult.Success;

            if (robot == null)
            {
                if (target?.Offer == null) return ShopOperationResult.IncompatibleMerge;
                action = ShopDropAction.MergeOffer;
                return CheckOfferMerge(side, source, target.SlotId, target.Offer.OfferId);
            }
            if (target != null && target.IsPurchased)
            {
                RobotEntity targetRobot;
                result = FindOwned(side, target.InstanceId.Value, out targetRobot);
                if (result != ShopOperationResult.Success) return result;
                if (targetRobot.Location != RobotLocation.PurchasedShopSlot) return ShopOperationResult.InvalidLocation;
                result = CheckMerge(side, source, targetRobot.InstanceId);
                if (result == ShopOperationResult.Success) { action = ShopDropAction.MergeOwned; return result; }
                if (result != ShopOperationResult.IncompatibleMerge && result != ShopOperationResult.MaxLevelReached) return result;
                if (sourceSlot != null) return result;
            }
            // 店内来源只允许合成，不因松到空白/未购商品而新增或交换对象。
            if (sourceSlot != null) return ShopOperationResult.Success;
            if (FindReturnSlot(state, targetSlotId) == null) return ShopOperationResult.ShopFull;
            action = ShopDropAction.Return;
            return ShopOperationResult.Success;
        }

        private static ShopSlot FindReturnSlot(BattleShopState state, int? targetSlotId)
        {
            var target = targetSlotId.HasValue ? state.FindSlot(targetSlotId.Value) : null;
            if (target != null && target.IsEmpty) return target;
            foreach (var slot in state.Slots) if (slot.IsEmpty) return slot;
            return null;
        }

        public ShopOperationResult TryCommitShopDrop(long interactionId, int? targetSlotId)
        {
            RobotDragSession session;
            var result = CheckInteractionCommit(interactionId, out session);
            if (result != ShopOperationResult.Success) return result;
            ShopDropAction action;
            result = ResolveShopDrop(session.Side, session.Source, targetSlotId, out action);
            if (result != ShopOperationResult.Success) return result;
            var state = GetState(session.Side);
            var target = targetSlotId.HasValue ? state.FindSlot(targetSlotId.Value) : null;
            if (action == ShopDropAction.MergeOffer)
                return TryCommitOfferMerge(interactionId, target.SlotId, target.Offer.OfferId);
            if (action == ShopDropAction.MergeOwned)
                return TryCommitMerge(interactionId, target.InstanceId.Value);
            _committing = true;
            try
            {
                if (action == ShopDropAction.Return)
                {
                    var robot = _owned[session.Source.InstanceId];
                    DetachSource(robot);
                    target = FindReturnSlot(state, targetSlotId);
                    target.InstanceId = robot.InstanceId;
                    robot.TrySetLocation(RobotLocation.PurchasedShopSlot, null);
                }
                CompleteInteraction();
                Notify(session.Side);
                return ShopOperationResult.Success;
            }
            finally { FinishMutation(); }
        }
    }
}
