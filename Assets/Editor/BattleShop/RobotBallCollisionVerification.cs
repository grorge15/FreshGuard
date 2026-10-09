using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    /// <summary>通过真实商店部署路径和运行中的 Physics2D 验证机器人挡球。</summary>
    public static class RobotBallCollisionVerification
    {
        [Serializable]
        public sealed class Result
        {
            public int configId;
            public bool bounced;
            public bool robotStayed;
            public bool emptyCellsClear;
            public float speedAfterBounce;
        }
        [Serializable]
        public sealed class Report
        {
            public bool passed;
            public string details;
            public List<Result> robots = new List<Result>();
        }

        [MenuItem("FreshGuard/Robots/Verify Ball Collisions")]
        public static void Verify() { VerifyAsync().Forget(); }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static async UniTaskVoid VerifyAsync()
        {
            var report = new Report();
            PhysicalCollisionBall ball = null;
            BattleShopSceneController host = null;
            SerializedObject ballSettings = null;
            Vector2 spawn = default;
            float angleMin = 0, angleMax = 0;
            try
            {
                Require(Application.isPlaying, "需要从启动场景进入 PlayMode。");
                host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                ball = host != null && host.Bootstrap != null ? host.Bootstrap.PlayerBall : null;
                Require(host != null && host.IsReady && ball != null, "商店或小球未就绪。");
                ballSettings = new SerializedObject(ball);
                spawn = ballSettings.FindProperty("spawnPosition").vector2Value;
                angleMin = ballSettings.FindProperty("initialAngleMin").floatValue;
                angleMax = ballSettings.FindProperty("initialAngleMax").floatValue;
                var board = GameObject.Find("PlayerBoardWorldRoot").GetComponentInChildren<PlacementBoardView>();
                var body = ball.GetComponent<Rigidbody2D>();
                foreach (var configId in new[] { 1001, 1002, 1003 })
                {
                    host.EndBattle();
                    await host.InitializeAsync();
                    Require(host.IsReady, "验收新局初始化失败。");
                    await UniTask.WaitUntil(() => host.IsCombatRunning).Timeout(TimeSpan.FromSeconds(8));
                    host.Bootstrap.StopBalls();
                    var context = host.Context;
                    var state = context.GetState(BattleSide.Player);
                    var slot = -1;
                    for (var retry = 0; retry <= 20 && slot < 0; retry++)
                    {
                        slot = Enumerable.Range(0, 3).Where(index => state.Slots[index].Offer?.RobotId == configId)
                            .DefaultIfEmpty(-1).First();
                        if (slot >= 0) break;
                        context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.Boss, 900000 + retry);
                        Require(context.TryRefresh(BattleSide.Player) == ShopOperationResult.Success, "验收商品刷新失败。");
                    }
                    Require(slot >= 0, "验收未抽到型号：" + configId);
                    RobotEntity robot;
                    Require(context.TryPurchase(BattleSide.Player, slot, state.Slots[slot].Offer.OfferId, out robot) == ShopOperationResult.Success,
                        "验收购买失败。");
                    Require(context.TryBeginInteraction(BattleSide.Player, robot.InstanceId), "验收交互失败。");
                    var target = new BoardPlacementTarget(board, new BoardCoordinate(4, 3));
                    Require(await host.DeployRobotAsync(robot.InstanceId, target, host.GetCancellationTokenOnDestroy()) == ShopOperationResult.Success,
                        "真实商店部署失败。");
                    var view = UnityEngine.Object.FindObjectsOfType<RobotView>().Single(item => item.InstanceId == robot.InstanceId);
                    var position = view.transform.position;
                    var colliders = view.GetComponentsInChildren<BoxCollider2D>();
                    Require(colliders.Length == 4 && colliders.All(item => item.enabled && !item.isTrigger), "部署碰撞体未启用。");
                    Physics2D.SyncTransforms();
                    var emptyClear = true;
                    var cells = robot.Configuration.ShapeId_Ref.CellOffsets;
                    for (var x = 0; x <= cells.Max(cell => cell[0]); x++)
                        for (var y = 0; y <= cells.Max(cell => cell[1]); y++)
                        {
                            if (cells.Any(cell => cell[0] == x && cell[1] == y)) continue;
                            var point = view.transform.TransformPoint(new Vector3(x * 0.35f, y * 0.35f, 0));
                            emptyClear &= colliders.All(item => !item.OverlapPoint(point));
                        }
                    ballSettings.Update();
                    ballSettings.FindProperty("spawnPosition").vector2Value = board.GetCellWorldPosition(new BoardCoordinate(3, 3));
                    ballSettings.FindProperty("initialAngleMin").floatValue = 0;
                    ballSettings.FindProperty("initialAngleMax").floatValue = 0;
                    ballSettings.ApplyModifiedPropertiesWithoutUndo();
                    ball.Launch();
                    await UniTask.WaitUntil(() => ball.BounceCount > 0).Timeout(TimeSpan.FromSeconds(2));
                    var result = new Result
                    {
                        configId = configId,
                        bounced = ball.CurrentDirection.x < 0 && body.velocity.x < 0,
                        robotStayed = Vector3.Distance(position, view.transform.position) < 0.0001f,
                        emptyCellsClear = emptyClear,
                        speedAfterBounce = body.velocity.magnitude
                    };
                    report.robots.Add(result);
                    Require(result.bounced && result.robotStayed && result.emptyCellsClear && Mathf.Abs(result.speedAfterBounce - ball.Speed) < 0.01f,
                        "碰撞、静态位置、空格或反弹速度验证失败：" + configId);
                }
                report.passed = true;
                report.details = "I/L/T 通过真实商店部署；Physics2D 实际碰撞反弹、恒速、机器人不移动及空白格无碰撞体均通过。";
            }
            catch (Exception e) { report.details = e.ToString(); Debug.LogError("[Robot Collision Verification] " + report.details); }
            finally
            {
                if (ball != null && ballSettings != null)
                {
                    ballSettings.Update();
                    ballSettings.FindProperty("spawnPosition").vector2Value = spawn;
                    ballSettings.FindProperty("initialAngleMin").floatValue = angleMin;
                    ballSettings.FindProperty("initialAngleMax").floatValue = angleMax;
                    ballSettings.ApplyModifiedPropertiesWithoutUndo();
                    ball.Launch();
                }
                if (host != null)
                {
                    host.EndBattle();
                    await host.InitializeAsync();
                }
                File.WriteAllText(Path.Combine(Application.dataPath, "../Library/RobotBallCollisionVerification.json"), JsonUtility.ToJson(report, true));
                if (report.passed) Debug.Log("[Robot Collision Verification] PASS " + report.details);
            }
        }
    }
}
