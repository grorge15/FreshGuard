#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameConfig;
using Luban;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class TowerBattlePlaybackTests
    {
        [UnityTest]
        public IEnumerator PlayerTowerDeathEndsOnlyItsBattle() => RunDeath(BattleSide.Player);

        [UnityTest]
        public IEnumerator OpponentTowerDeathEndsOnlyItsBattle() => RunDeath(BattleSide.Opponent);

        [UnityTest]
        public IEnumerator DestroyImmediatelyAfterDeathStillRequestsSettlement() => RunDeath(BattleSide.Opponent, true);

        [UnityTest]
        public IEnumerator DestroyDuringShopNotificationStillRequestsSettlement() => RunDeath(BattleSide.Player, true, true);

        private IEnumerator RunDeath(BattleSide side, bool destroyAfterDeath = false, bool damageDuringShopNotification = false)
        {
            Assert.IsTrue(Application.isPlaying);
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            var context = new BattleShopContext(ShopRules.FromTables(tables),
                new[] { 501, 502, 505 }, new[] { 501, 502, 505 }, tables.TbRobot.Get);
            var session = new TowerBattleSession(context.BattleId, context.Registry, 100);
            var root = new GameObject("TowerBattlePlayback");
            var controller = root.AddComponent<BattleShopSceneController>();
            SetField(controller, "<Context>k__BackingField", context);
            SetField(controller, "_towers", session);
            SetField(controller, "_ready", true);
            SetField(controller, "_running", true);
            session.SettlementRequested += (Action<BattleSettlementSnapshot>)Delegate.CreateDelegate(
                typeof(Action<BattleSettlementSnapshot>), controller,
                controller.GetType().GetMethod("OnTowerSettlementRequested", BindingFlags.Instance | BindingFlags.NonPublic));
            var sceneViews = new List<GameObject>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Tower/Prefabs/Tower.prefab");
            foreach (var tower in new[] { session.PlayerTower, session.OpponentTower })
            {
                var instance = UnityEngine.Object.Instantiate(prefab, root.transform);
                instance.SetActive(false);
                var view = instance.GetComponent<TowerView>();
                view.Bind(tower);
                instance.SetActive(true);
                Assert.AreEqual(tower.InstanceId, view.InstanceId, "准备时绑定、启用时应保留绑定");
                SetField(controller, tower.Side == BattleSide.Player ? "_playerTowerView" : "_opponentTowerView", view);
                sceneViews.Add(instance);
            }
            var notifications = 0;
            Action<BattleSettlementSnapshot> listener = snapshot =>
            {
                notifications++;
                Assert.AreEqual(context.BattleId, snapshot.BattleId);
                Assert.AreEqual(side, snapshot.DefeatedSide);
                Assert.IsTrue(context.IsEnded);
                Assert.IsNull(controller.Context);
            };
            GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener);
            try
            {
                SetField(controller, "_frozen", true);
                Assert.AreEqual(DamageResult.Ignored, controller.ApplyTowerDamage(context.BattleId, side, 100));
                SetField(controller, "_frozen", false);
                Assert.AreEqual(DamageResult.Ignored, controller.ApplyTowerDamage(context.BattleId + 1, side, 100));
                Assert.AreEqual(DamageResult.Damaged, controller.ApplyTowerDamage(context.BattleId, side, 1));
                var target = side == BattleSide.Player ? session.PlayerTower : session.OpponentTower;
                var targetView = sceneViews.Find(v => v.GetComponent<TowerView>().InstanceId == target.InstanceId).GetComponent<TowerView>();
                Assert.AreEqual(0.8f, targetView.HealthFraction);
                if (damageDuringShopNotification)
                {
                    var fired = false;
                    Action<int> onShopChanged = _ =>
                    {
                        if (fired) return;
                        fired = true;
                        Assert.AreEqual(DamageResult.Killed, controller.ApplyTowerDamage(context.BattleId, side, 4));
                        UnityEngine.Object.DestroyImmediate(root);
                        Assert.AreEqual(0, notifications, "商店事务提交前不能通知结算");
                    };
                    GameEvent.AddEventListener<int>(BattleShopEvents.Changed, onShopChanged);
                    try { Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player)); }
                    finally { GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, onShopChanged); }
                    Assert.IsTrue(fired);
                    Assert.IsTrue(context.IsEnded);
                }
                else
                {
                    Assert.AreEqual(DamageResult.Killed, controller.ApplyTowerDamage(context.BattleId, side, 4));
                    Assert.AreEqual(0f, targetView.HealthFraction);
                    Assert.IsTrue(targetView.IsDead);
                    Assert.AreEqual(0, notifications, "终局通知应离开受伤通知栈后发出");
                    Assert.IsTrue(context.IsEndRequested);
                    Assert.IsFalse(controller.IsCombatRunning);
                    if (destroyAfterDeath) UnityEngine.Object.DestroyImmediate(root);
                }
                yield return null;
                yield return null;
                Assert.AreEqual(1, notifications);
                Assert.IsTrue(context.IsEnded);
                if (root != null)
                {
                    foreach (var view in sceneViews)
                    {
                        Assert.IsTrue(view != null, "结算不能销毁场景预摆的塔");
                        Assert.AreEqual(0, view.GetComponent<TowerView>().InstanceId);
                    }
                    Assert.AreEqual(DamageResult.Ignored, controller.ApplyTowerDamage(context.BattleId, side, 100));
                    controller.EndBattle();
                }
                Assert.AreEqual(1, notifications);
            }
            finally
            {
                GameEvent.RemoveEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested, listener);
                UnityEngine.Object.DestroyImmediate(root);
                session.Dispose();
                context.Dispose();
            }
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
#endif
