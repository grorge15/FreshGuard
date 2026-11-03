using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using GameConfig;
using Luban;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace GameLogic.Tests
{
    public sealed class TowerIntegrationTests
    {
        [Test]
        public void SceneTowersKeepAuthoredTransformsAcrossCleanupAndNewBattle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Tower/Prefabs/Tower.prefab");
            var root = new GameObject("TowerLayoutTest");
            var controller = root.AddComponent<BattleShopSceneController>();
            var player = UnityEngine.Object.Instantiate(prefab, root.transform).GetComponent<TowerView>();
            var opponent = UnityEngine.Object.Instantiate(prefab, root.transform).GetComponent<TowerView>();
            player.transform.localPosition = new Vector3(2f, -4f, 0f);
            opponent.transform.localPosition = new Vector3(-3f, 5f, 0f);
            player.transform.localScale = new Vector3(1.5f, 2f, 1f);
            opponent.transform.localScale = new Vector3(0.8f, 1.2f, 1f);
            player.transform.localRotation = Quaternion.Euler(0f, 0f, 15f);
            var rotation = player.transform.localRotation;
            try
            {
                SetField(controller, "_playerTowerView", player);
                SetField(controller, "_opponentTowerView", opponent);
                var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                    Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
                for (var battle = 0; battle < 2; battle++)
                {
                    using (var context = new BattleShopContext(ShopRules.FromTables(tables),
                        new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, tables.TbRobot.Get))
                    {
                        SetField(controller, "<Context>k__BackingField", context);
                        controller.GetType().GetMethod("PrepareTowers", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(controller, new object[] { context, tables, CancellationToken.None });
                        Assert.AreEqual(controller.Towers.PlayerTower.InstanceId, player.InstanceId);
                        Assert.AreEqual(controller.Towers.OpponentTower.InstanceId, opponent.InstanceId);
                        Assert.AreEqual(1f, player.HealthFraction);
                        controller.Towers.ApplyDamage(context.BattleId, BattleSide.Player, 25);
                        Assert.AreEqual(0f, player.HealthFraction);
                        controller.EndBattle();
                        Assert.IsTrue(player != null && opponent != null, "场景塔应在结算后保留");
                        Assert.AreEqual(0, player.InstanceId);
                        Assert.AreEqual(0, opponent.InstanceId);
                        Assert.AreEqual(new Vector3(2f, -4f, 0f), player.transform.localPosition);
                        Assert.AreEqual(new Vector3(-3f, 5f, 0f), opponent.transform.localPosition);
                        Assert.AreEqual(new Vector3(1.5f, 2f, 1f), player.transform.localScale);
                        Assert.AreEqual(new Vector3(0.8f, 1.2f, 1f), opponent.transform.localScale);
                        Assert.AreEqual(rotation, player.transform.localRotation);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(BattleSide.Player, false)]
        [TestCase(BattleSide.Opponent, false)]
        [TestCase(BattleSide.Player, true)]
        [TestCase(BattleSide.Opponent, true)]
        public void SceneDefersCleanupUntilDeathNotificationReturnsAndPublishesSettlementOnce(BattleSide side, bool destroyBeforeUpdate)
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            using (var context = new BattleShopContext(ShopRules.FromTables(tables),
                new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, tables.TbRobot.Get))
            using (var session = new TowerBattleSession(context.BattleId, context.Registry, 100))
            {
                var root = new GameObject("TowerIntegrationTest");
                var controller = root.AddComponent<BattleShopSceneController>();
                var type = controller.GetType();
                SetField(controller, "<Context>k__BackingField", context);
                SetField(controller, "_ready", true);
                SetField(controller, "_running", true);
                var method = type.GetMethod("OnTowerSettlementRequested", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "场景入口需要连接双方塔死亡与结算通知");
                SetField(controller, "_towers", session);
                var onDeath = (Action<BattleSettlementSnapshot>)Delegate.CreateDelegate(
                    typeof(Action<BattleSettlementSnapshot>), controller, method);
                session.SettlementRequested += onDeath;
                var notifications = 0;
                Action<BattleSettlementSnapshot> listener = snapshot =>
                {
                    notifications++;
                    Assert.AreEqual(context.BattleId, snapshot.BattleId);
                    Assert.AreEqual(side, snapshot.DefeatedSide);
                    Assert.AreEqual(0, snapshot.TowerHp);
                    Assert.IsTrue(context.IsEnded, "结算通知前清理必须已经提交");
                    Assert.IsNull(controller.Context);
                    controller.EndBattle();
                };
                GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener);
                try
                {
                    Assert.AreEqual(DamageResult.Killed, session.ApplyDamage(context.BattleId, side, 100));
                    Assert.IsFalse(context.IsEnded, "不能在塔通知调用栈中清理 Registry");
                    Assert.IsFalse(controller.IsCombatRunning);
                    Assert.AreEqual(ShopOperationResult.BattleEnded, context.TryRefresh(BattleSide.Player));
                    Assert.AreEqual(0, notifications);
                    if (destroyBeforeUpdate)
                    {
                        // 普通 MonoBehaviour 的销毁消息由 PlayMode 驱动；EditMode 显式验证该处理函数。
                        type.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                        UnityEngine.Object.DestroyImmediate(root);
                    }
                    else type.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                    Assert.AreEqual(1, notifications);
                    controller.EndBattle();
                    Assert.AreEqual(1, notifications);
                }
                finally
                {
                    GameEvent.RemoveEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        [Test]
        public void TowerPrefabIsLoadableAndViewTracksHealthWithoutChangingEntity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Tower/Prefabs/Tower.prefab");
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(prefab.GetComponent("TowerView"));
            var settings = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset");
            Assert.AreEqual("Tower", settings.BeginCollect("DefaultPackage", true, false).CollectAssets
                .Single(asset => asset.AssetInfo.AssetPath == AssetDatabase.GetAssetPath(prefab)).Address);
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                using (var registry = new BattleEntityRegistry())
                {
                    var tower = registry.CreateTower(3001, BattleSide.Player, 100);
                    var view = root.GetComponent("TowerView");
                    var type = view.GetType();
                    type.GetMethod("Bind").Invoke(view, new object[] { tower });
                    var body = root.transform.Find("Body");
                    var health = root.transform.Find("HealthFill");
                    var bodyPosition = body.localPosition;
                    var rootPosition = root.transform.localPosition;
                    var colliderBounds = root.GetComponent<BoxCollider2D>().bounds;
                    registry.ApplyTowerDamage(tower.InstanceId, 25);
                    var healthPosition = health.localPosition;
                    Assert.AreEqual(0.75f, type.GetProperty("HealthFraction").GetValue(view));
                    type.GetMethod("AdvanceHitFeedback", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(view, new object[] { 0.04f });
                    Assert.AreNotEqual(bodyPosition, body.localPosition, "基地受击后身体应左右晃动。");
                    Assert.AreEqual(healthPosition, health.localPosition, "基地血条不能跟随身体晃动。");
                    Assert.AreEqual(rootPosition, root.transform.localPosition, "基地逻辑根节点不能移动。");
                    Assert.AreEqual(colliderBounds, root.GetComponent<BoxCollider2D>().bounds,
                        "基地受击反馈不能改变碰撞范围。");
                    type.GetMethod("AdvanceHitFeedback", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(view, new object[] { 0.3f });
                    Assert.AreEqual(bodyPosition, body.localPosition, "基地晃动结束后必须准确复位。");
                    registry.ApplyTowerDamage(tower.InstanceId, 100);
                    Assert.AreEqual(0f, type.GetProperty("HealthFraction").GetValue(view));
                    Assert.AreEqual(true, type.GetProperty("IsDead").GetValue(view));
                    type.GetMethod("Unbind").Invoke(view, null);
                    Assert.AreEqual(0, type.GetProperty("InstanceId").GetValue(view));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(BattleSide.Player, 1f)]
        [TestCase(BattleSide.Opponent, 1f)]
        [TestCase(BattleSide.Player, 0.65f)]
        [TestCase(BattleSide.Opponent, 0.65f)]
        public void HealthBarKeepsLeftEdgeFixedAsRightEdgeDecreasesAndResetsOnNewBattle(BattleSide side, float mapScale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Tower/Prefabs/Tower.prefab");
            var root = UnityEngine.Object.Instantiate(prefab);
            root.transform.localPosition = new Vector3(2f, -3f, 0f);
            root.transform.localScale *= mapScale;
            try
            {
                using (var registry = new BattleEntityRegistry())
                {
                    var view = root.GetComponent<TowerView>();
                    var fill = root.transform.Find("HealthFill").GetComponent<SpriteRenderer>();
                    var fullPosition = fill.transform.localPosition;
                    var fullScale = fill.transform.localScale;
                    var tower = registry.CreateTower(3001, side, 100);
                    view.Bind(tower);
                    var left = fill.bounds.min.x;
                    var fullWidth = fill.bounds.size.x;
                    for (var hit = 1; hit <= 4; hit++)
                    {
                        registry.ApplyTowerDamage(tower.InstanceId, 25);
                        var fraction = 1f - hit * 0.25f;
                        Assert.That(fill.bounds.min.x, Is.EqualTo(left).Within(0.0001f), "扣血时左端必须保持原位。");
                        Assert.That(fill.bounds.max.x, Is.EqualTo(left + fullWidth * fraction).Within(0.0001f),
                            "右端应按剩余血量逐步向左收缩。");
                        Assert.AreEqual(fullScale.y, fill.transform.localScale.y, "扣血不能改变血条高度。");
                    }
                    view.Unbind();
                    using (var nextBattle = new BattleEntityRegistry())
                        view.Bind(nextBattle.CreateTower(3001, side, 100));
                    Assert.AreEqual(fullPosition, fill.transform.localPosition, "新一局满血时恢复原位置。");
                    Assert.AreEqual(fullScale, fill.transform.localScale, "新一局满血时恢复完整宽度。");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
