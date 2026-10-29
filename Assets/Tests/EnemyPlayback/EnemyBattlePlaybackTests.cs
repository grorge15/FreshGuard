#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace GameLogic.Tests
{
    public sealed class EnemyBattlePlaybackTests
    {
        [UnityTest]
        public IEnumerator EnemyKillSPImmediatelyAccumulatesInPlayerEnergyCoinsText()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null)
            {
                host.EndBattle();
            }
            else
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                var menuDeadline = Time.realtimeSinceStartup + 60f;
                while ((UIModule.UIRoot == null || GameModule.UI.GetUI<MainMenuUI>() == null) &&
                    Time.realtimeSinceStartup < menuDeadline) yield return null;
                Assert.IsNotNull(UIModule.UIRoot);
                Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>(), "等待 Launcher 完成资源与主菜单准备");
                yield return GameModule.Scene.LoadSceneAsync("Main").ToCoroutine();
                host = Object.FindObjectOfType<BattleShopSceneController>();
            }
            Assert.IsNotNull(host);
            GameModule.UI.CloseUI<MainMenuUI>();
            yield return host.InitializeAsync().ToCoroutine();
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsCombatRunning) break;
                yield return null;
            }
            Assert.IsNotNull(host);
            Assert.IsTrue(host.IsCombatRunning);
            try
            {
                host.SetFrozen(true);
                var context = host.Context;
                var label = host.CombatWindow.Shop.transform.Find("m_rect_ShopPanel/m_text_EnergyCoins").GetComponent<Text>();
                var initial = context.GetState(BattleSide.Player).EnergyCoins;
                Assert.AreEqual(initial.ToString(), label.text);
                var expected = initial;
                foreach (var configId in new[] { 2001, 2002, 2003 })
                {
                    var config = ConfigSystem.Instance.Tables.TbEnemy.Get(configId);
                    var id = context.Registry.CreateEnemyFromConfig(config, BattleSide.Player);
                    host.SetFrozen(false);
                    ResolveRewardTarget(context, id, BattleSide.Player);
                    ResolveRewardTarget(context, id, BattleSide.Player);
                    host.SetFrozen(true);
                    expected += config.EnemyKillSP;
                    Assert.AreEqual(expected, context.GetState(BattleSide.Player).EnergyCoins);
                    Assert.AreEqual(expected.ToString(), label.text, "同帧收到商店通知后显示累加金额");
                }
                var opponentBefore = context.GetState(BattleSide.Opponent).EnergyCoins;
                var opponent = context.Registry.CreateEnemyFromConfig(ConfigSystem.Instance.Tables.TbEnemy.Get(2001), BattleSide.Opponent);
                host.SetFrozen(false);
                ResolveRewardTarget(context, opponent, BattleSide.Opponent);
                host.SetFrozen(true);
                Assert.AreEqual(opponentBefore + 1, context.GetState(BattleSide.Opponent).EnergyCoins);
                Assert.AreEqual(expected.ToString(), label.text);
                var leaked = context.Registry.CreateEnemyFromConfig(ConfigSystem.Instance.Tables.TbEnemy.Get(2001), BattleSide.Player);
                context.Registry.Remove(leaked, EntityRemovalReason.Leaked, out _);
                Assert.AreEqual(expected.ToString(), label.text);
            }
            finally { host.EndBattle(); }
            yield return null;
        }

        private static void ResolveRewardTarget(BattleShopContext context, int id, BattleSide side)
        {
            var combat = context.Battle.Combat;
            var action = new CombatActionSnapshot(context.BattleId, 99999, side, 0, new[] { id },
                CombatOutcomeTag.SingleDamage, 100000, Vector3.zero, Vector3.zero, 1, CombatSourceKind.Other);
            combat.Resolve(combat.CaptureAttack(action));
        }

        [UnityTest]
        public IEnumerator EditedCombatRegionsKeepLayoutAndShopStillDragsAndCancelsOnHide()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null)
            {
                host.EndBattle();
                host.SendMessage("Start");
            }
            else
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsReady) break;
                yield return null;
            }
            Assert.IsNotNull(host);
            Assert.IsTrue(host.IsReady);
            try
            {
                host.SetFrozen(true);
                var window = host.CombatWindow;
                var shop = window.Shop;
                var shopRect = shop.rectTransform;
                var infoRect = window.Info.rectTransform;
                shopRect.anchoredPosition = new Vector2(18, 120);
                shopRect.sizeDelta = new Vector2(980, 240);
                shopRect.localScale = Vector3.one * 0.8f;
                infoRect.anchoredPosition = new Vector2(20, 280);
                infoRect.sizeDelta = new Vector2(900, 135);
                infoRect.localScale = Vector3.one * 0.9f;
                Canvas.ForceUpdateCanvases();
                var canvas = window.Canvas.rootCanvas;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var slot = shop.transform.Find("m_rect_ShopPanel/m_tf_Offers/Slot_0");
                var pointer = RectTransformUtility.WorldToScreenPoint(camera, slot.position);
                var eventData = new PointerEventData(EventSystem.current)
                    { position = pointer, pointerId = -1, button = PointerEventData.InputButton.Left };
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(eventData, hits);
                Assert.IsTrue(hits.Exists(hit => hit.gameObject.transform.IsChildOf(shop.transform)),
                    "调整后商店仍能接收射线。 pointer=" + pointer + " screen=" + Screen.width + "x" + Screen.height +
                    " root=" + window.rectTransform.rect + " rootScale=" + window.transform.lossyScale +
                    " canvas=" + canvas.renderMode + " camera=" + camera + " raycaster=" + window.GraphicRaycaster.enabled +
                    " slot=" + ((RectTransform)slot).rect + " hits=" + string.Join(",", hits.ConvertAll(hit => hit.gameObject.name)));
                ExecuteEvents.Execute(slot.gameObject, eventData, ExecuteEvents.pointerDownHandler);
                // 使用框架真实递归 Update 推进长按，不能手动调用 Drag.Advance 替代。
                deadline = Time.realtimeSinceStartup + 2f;
                while (shop.Drag.Phase == RobotDragPhase.Pressed && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(RobotDragPhase.Dragging, shop.Drag.Phase);
                var ghost = shop.transform.Find("m_item_DragGhost");
                Assert.That(Vector2.Distance(pointer, RectTransformUtility.WorldToScreenPoint(camera, ghost.position)), Is.LessThan(1f));
                host.SendMessage("LateUpdate");
                yield return null;
                Assert.AreEqual(new Vector2(18, 120), shopRect.anchoredPosition);
                Assert.AreEqual(new Vector2(980, 240), shopRect.sizeDelta);
                Assert.AreEqual(Vector3.one * 0.8f, shopRect.localScale);
                Assert.AreEqual(new Vector2(20, 280), infoRect.anchoredPosition);
                Assert.AreEqual(new Vector2(900, 135), infoRect.sizeDelta);
                Assert.AreEqual(Vector3.one * 0.9f, infoRect.localScale);
                window.Visible = false;
                Assert.AreEqual(RobotDragPhase.Idle, shop.Drag.Phase);
                Assert.IsFalse(host.Context.IsBusy);
                window.Visible = true;
            }
            finally { host.EndBattle(); }
            yield return null;
            Assert.IsNull(GameModule.UI.GetUI<MainCombatUI>());
            Assert.AreEqual(0, Object.FindObjectsOfType<BattleInfoView>().Length);
        }

        [UnityTest]
        public IEnumerator LaunchSceneShowsMainCombatUIRunsTwentyWavesAndRestartsWithoutGhostEnemies()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null)
            {
                host.EndBattle();
                host.SendMessage("Start");
            }
            else if (SceneManager.GetActiveScene().path != "Assets/Scenes/main.unity")
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsReady) break;
                yield return null;
            }
            Assert.IsNotNull(host, "启动场景应加载战斗场景");
            Assert.IsTrue(host.IsReady, "资源准备应完成");
            Assert.IsNotNull(host.Enemies);
            Assert.IsNotNull(host.InfoWindow);
            Assert.AreEqual(host.Context.BattleId, host.InfoWindow.BattleId);
            Assert.AreEqual(0, host.Enemies.LiveCount, "准备倒计时内不得创建敌人实体");
            yield return null;
            AssertSavedCombatLayout(host);
            while (!host.IsCombatRunning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsCombatRunning);
            Assert.AreEqual(1, host.Enemies.GetSpawnedCount(BattleSide.Player));
            Assert.AreEqual(1, host.Enemies.GetSpawnedCount(BattleSide.Opponent));
            var views = Object.FindObjectsOfType<EnemyView>();
            Assert.AreEqual(2, views.Length);
            var position = views[0].transform.position;
            var animator = views[0].GetComponent<Animator>();
            host.SetFrozen(true);
            var frozenUntil = Time.realtimeSinceStartup + 0.7f;
            while (Time.realtimeSinceStartup < frozenUntil) yield return null;
            Assert.AreEqual(position, views[0].transform.position);
            Assert.AreEqual(0f, animator.speed);
            Assert.AreEqual(1, host.Enemies.GetSpawnedCount(BattleSide.Player));
            host.SetFrozen(false);
            Assert.AreEqual(1f, animator.speed);
            // 暂停自动帧驱动，在真实加载的场景中按同一控制器推进完整20波。
            host.enabled = false;
            var survivor = views[0].InstanceId;
            host.Context.Registry.TryGet(survivor, out var survivorEntity);
            typeof(EnemyEntity).GetField("<BaseSpeed>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(survivorEntity, 0f);
            host.Context.Registry.ApplyDamage(survivor, 7, out _);
            AdvanceWithoutLeaks(host, 20d, survivor);
            Assert.AreEqual(2, host.Enemies.CurrentWave);
            Assert.IsTrue(host.Context.Registry.TryGet(survivor, out survivorEntity));
            Assert.AreEqual(93, ((EnemyEntity)survivorEntity).CurrentHp, "跨波保留旧怪生命");
            host.SendMessage("LateUpdate");
            Assert.AreEqual("波次 2", InfoLabel(host, "m_text_Wave"));
            Assert.AreEqual("00:20", InfoLabel(host, "m_text_ElapsedTime"));
            AdvanceWithoutLeaks(host, 400d, 0);
            KillEnemies(host, 0);
            host.Enemies.Advance(5f);
            host.SendMessage("LateUpdate");
            Assert.AreEqual(200, host.Enemies.GetSpawnedCount(BattleSide.Player));
            Assert.AreEqual(200, host.Enemies.GetSpawnedCount(BattleSide.Opponent));
            Assert.IsTrue(host.Enemies.IsAllEnemiesCleared);
            Assert.IsTrue(host.IsCombatRunning, "最后一波清空后仍保留战斗");
            Assert.AreEqual("波次 20", InfoLabel(host, "m_text_Wave"));
            Assert.AreEqual("06:45", InfoLabel(host, "m_text_ElapsedTime"));
            AssertSavedCombatLayout(host);
            var oldBattle = host.Context.BattleId;
            host.ApplyTowerDamage(oldBattle, BattleSide.Opponent, int.MaxValue);
            host.EndBattle();
            yield return null;
            Assert.AreEqual(0, Object.FindObjectsOfType<EnemyView>().Length);
            Assert.IsNull(GameModule.UI.GetUI<MainCombatUI>());
            host.EndBattle();
            host.enabled = true;
            host.SendMessage("Start");
            deadline = Time.realtimeSinceStartup + 30f;
            while (!host.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsReady);
            Assert.AreNotEqual(oldBattle, host.Context.BattleId);
            Assert.AreEqual(5, host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(0, host.Enemies.LiveCount);
            Assert.AreEqual(0, host.Enemies.GetSpawnedCount(BattleSide.Player));
            Assert.AreEqual(0d, host.Enemies.ElapsedSeconds);
            Assert.AreEqual("波次 1", InfoLabel(host, "m_text_Wave"));
            Assert.AreEqual("00:00", InfoLabel(host, "m_text_ElapsedTime"));
            Assert.AreEqual(1, Object.FindObjectsOfType<BattleInfoView>().Length);
            Assert.AreEqual(DamageResult.Ignored, host.ApplyTowerDamage(oldBattle, BattleSide.Player, 100));
            host.EndBattle();
        }

        private static string InfoLabel(BattleShopSceneController host, string name) => host.InfoWindow.View.transform
            .Find("m_rect_BattleInfo/m_rect_Battle/" + name).GetComponent<Text>().text;

        private static void AdvanceWithoutLeaks(BattleShopSceneController host, double target, int survivor)
        {
            while (host.Enemies.ElapsedSeconds < target && host.IsCombatRunning)
            {
                KillEnemies(host, survivor);
                host.Enemies.Advance((float)System.Math.Min(0.5d, target - host.Enemies.ElapsedSeconds));
            }
            Assert.IsTrue(host.IsCombatRunning);
        }

        private static void KillEnemies(BattleShopSceneController host, int survivor)
        {
            foreach (var view in Object.FindObjectsOfType<EnemyView>())
            {
                if (view.InstanceId == survivor || view.InstanceId <= 0) continue;
                if (host.Context.Registry.ApplyDamage(view.InstanceId, int.MaxValue, out var removal) == DamageResult.Killed)
                    host.Enemies.NotifyKilled(removal);
            }
        }

        private static void AssertSavedCombatLayout(BattleShopSceneController host)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab");
            Assert.IsNotNull(host.CombatWindow);
            Assert.IsTrue(host.CombatWindow.FullScreen);
            foreach (var name in new[] { "BattleInfoUI", "BattleShopUI" })
            {
                var saved = prefab.transform.Find(name) as RectTransform;
                var live = host.CombatWindow.transform.Find(name) as RectTransform;
                Assert.AreEqual(saved.anchorMin, live.anchorMin);
                Assert.AreEqual(saved.anchorMax, live.anchorMax);
                Assert.AreEqual(saved.anchoredPosition, live.anchoredPosition);
                Assert.AreEqual(saved.sizeDelta, live.sizeDelta);
                Assert.AreEqual(saved.localScale, live.localScale);
            }
            Assert.IsFalse(host.InfoWindow.View.GetComponent<CanvasGroup>().blocksRaycasts);
        }
    }
}
#endif
