namespace GameLogic
{
    public sealed partial class BattleShopContext
    {
        private enum ShopDropAction { Keep, Return, MergeOffer, MergeOwned, SwapStorage }

        public ShopOperationResult CheckShopDrop(BattleSide side, RobotDragSource source, int? targetSlotId)
        {
            ShopDropAction action;
            return ResolveShopDrop(side, source, targetSlotId, out action);
        }

        // 预览与提交共用分支；只有同类有效目标的不匹配/封顶才允许回店或交换。
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
                if (robot.Location == RobotLocation.Storage) { action = ShopDropAction.SwapStorage; return ShopOperationResult.Success; }
                if (sourceSlot != null) return result;
            }
            // 店内来源只允许合成，不因松到空白/未购商品而新增或交换对象。
            if (sourceSlot != null) return ShopOperationResult.Success;
            if (state.Slots.Count >= ShopRules.ShopCapacity) return ShopOperationResult.ShopFull;
            action = ShopDropAction.Return;
            return ShopOperationResult.Success;
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
                if (action == ShopDropAction.Return || action == ShopDropAction.SwapStorage)
                {
                    var robot = _owned[session.Source.InstanceId];
                    var oldTarget = action == ShopDropAction.SwapStorage ? _owned[target.InstanceId.Value] : null;
                    DetachSource(robot);
                    if (oldTarget != null)
                    {
                        state.StorageInstanceId = oldTarget.InstanceId;
                        oldTarget.TrySetLocation(RobotLocation.Storage, null);
                    }
                    else target = state.AddSlot();
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
