using System;
using UnityEngine;

namespace GameLogic
{
    public static class MapBattleValidation
    {
        public static void RequireClearSpawn(PlacementBoardView board, PhysicalCollisionBall ball)
        {
            if (board == null || board.Model == null || ball == null) throw new InvalidOperationException("中心出生检查缺少棋盘或能源球。");
            if (!board.Model.IsTerrainMap) return;
            var scale = board.transform.lossyScale;
            var minScale = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            if (minScale <= 0f) throw new InvalidOperationException("棋盘缩放不能为零。");
            foreach (var issue in MapValidator.ValidateBallClearance(board.Model.CaptureMap(), ball.WorldRadius / minScale))
                if (issue.Severity == MapValidationSeverity.Error) throw new InvalidOperationException(board.name + "：" + issue);
            // 真实碰撞补充检查：准备期可能已有机器人，且旧局Destroy要到帧末完成。
            Physics2D.SyncTransforms();
            foreach (var collider in Physics2D.OverlapCircleAll(board.transform.position, ball.WorldRadius))
            {
                if (collider.isTrigger || !collider.transform.IsChildOf(board.transform) ||
                    collider == ball.GetComponent<Collider2D>()) continue;
                throw new InvalidOperationException(board.name + "：中心出生位置与碰撞体重叠：" + collider.name);
            }
        }
    }
}
