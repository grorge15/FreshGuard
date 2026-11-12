#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class ShopHapticPlaybackTests
    {
        [UnityTest]
        public IEnumerator RefreshHapticsFollowReelsAndStopOnSettingsHideAndBackground()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null) host.EndBattle();
            else
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                var menuDeadline = Time.realtimeSinceStartup + 60f;
                while ((UIModule.UIRoot == null || GameModule.UI.GetUI<MainMenuUI>() == null) &&
                    Time.realtimeSinceStartup < menuDeadline) yield return null;
                Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>());
                yield return GameModule.Scene.LoadSceneAsync("Main").ToCoroutine();
                host = Object.FindObjectOfType<BattleShopSceneController>();
            }
            GameModule.UI.CloseUI<MainMenuUI>();
            yield return host.InitializeAsync().ToCoroutine();
            var deadline = Time.realtimeSinceStartup + 60f;
            while (!host.TryOpenRobotInteraction(host.Context.BattleId) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(host.CanInteractWithRobots);

            var pulseProperty = typeof(HapticFeedbackService).GetProperty("LightPulseOverride", BindingFlags.Static | BindingFlags.NonPublic);
            var stopProperty = typeof(HapticFeedbackService).GetProperty("RefreshStopOverride", BindingFlags.Static | BindingFlags.NonPublic);
            var previousPulse = pulseProperty.GetValue(null);
            var previousStop = stopProperty.GetValue(null);
            var enabled = HapticFeedbackService.Enabled;
            var pulses = new List<Vector2Int>();
            var stops = 0;
            pulseProperty.SetValue(null, (Action<int, int>)((duration, amplitude) => pulses.Add(new Vector2Int(duration, amplitude))));
            stopProperty.SetValue(null, (Action)(() => stops++));
            HapticFeedbackService.Enabled = true;
            try
            {
                var window = host.CombatWindow;
                var shop = window.Shop;
                var refresh = (Button)typeof(BattleShopUI).GetField("_refreshButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shop);
                host.Context.Rewards.TryGrantGlassBreak(host.Context.BattleId, "refresh-haptic-test-funds", BattleSide.Player, 50);

                refresh.onClick.Invoke();
                Assert.IsTrue(shop.IsRefreshing);
                Assert.AreEqual(0, pulses.Count, "转轮开始滚动前不震动。");
                deadline = Time.realtimeSinceStartup + 1f;
                while (pulses.Count < 3 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.GreaterOrEqual(pulses.Count, 3, "刷新期间必须持续推进轻震，不能只在点击时震一次。");
                foreach (var pulse in pulses) Assert.AreEqual(new Vector2Int(100, 18), pulse);

                HapticFeedbackService.PlayBerserkStartPulse();
                Assert.AreEqual(new Vector2Int(25, 60), pulses[pulses.Count - 1]);
                var afterBerserk = pulses.Count;
                deadline = Time.realtimeSinceStartup + 0.5f;
                while (pulses.Count == afterBerserk && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(pulses.Count, afterBerserk, "暴走反馈结束后继续转轮轻震。");
                Assert.AreEqual(new Vector2Int(100, 18), pulses[pulses.Count - 1]);
                deadline = Time.realtimeSinceStartup + 3f;
                while (shop.IsRefreshing && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsFalse(shop.IsRefreshing);
                Assert.AreEqual(1, stops, "最后一个转轮停稳时停止持续震动。");
                var completed = pulses.Count;
                yield return FramesFor(0.2f);
                Assert.AreEqual(completed, pulses.Count, "刷新结束后不能残留震动循环。");

                refresh.onClick.Invoke();
                deadline = Time.realtimeSinceStartup + 1f;
                while (pulses.Count == completed && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(pulses.Count, completed);
                HapticFeedbackService.Enabled = false;
                Assert.AreEqual(2, stops, "关闭开关立即终止正在播放的刷新震动。");
                var disabled = pulses.Count;
                yield return FramesFor(0.2f);
                Assert.AreEqual(disabled, pulses.Count);
                HapticFeedbackService.Enabled = true;
                deadline = Time.realtimeSinceStartup + 0.5f;
                while (pulses.Count == disabled && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.Greater(pulses.Count, disabled);
                window.Visible = false;
                Assert.IsFalse(shop.IsRefreshing);
                Assert.AreEqual(3, stops, "隐藏战斗界面时停止刷新震动。");
                var hidden = pulses.Count;
                yield return FramesFor(0.2f);
                Assert.AreEqual(hidden, pulses.Count);
                window.Visible = true;

                foreach (var message in new[] { "OnApplicationPause", "OnApplicationFocus" })
                {
                    var before = pulses.Count;
                    refresh.onClick.Invoke();
                    deadline = Time.realtimeSinceStartup + 1f;
                    while (pulses.Count == before && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.Greater(pulses.Count, before);
                    var beforeStop = stops;
                    shop.Drag.SendMessage(message, message == "OnApplicationPause");
                    Assert.IsFalse(shop.IsRefreshing);
                    Assert.AreEqual(beforeStop + 1, stops, "切到后台或失去焦点必须停止刷新震动。");
                    var background = pulses.Count;
                    yield return FramesFor(0.15f);
                    Assert.AreEqual(background, pulses.Count);
                }

                // 用真实刷新事务耗尽测试余额，验证失败刷新没有触觉反馈。
                var state = host.Context.GetState(BattleSide.Player);
                Assert.Greater(host.RefreshPrice, 0);
                while (state.EnergyCoins >= host.RefreshPrice)
                    Assert.AreEqual(ShopOperationResult.Success, host.Context.TryRefresh(BattleSide.Player));
                var failedCount = pulses.Count;
                var failedStops = stops;
                refresh.onClick.Invoke();
                Assert.IsFalse(shop.IsRefreshing);
                yield return FramesFor(0.2f);
                Assert.AreEqual(failedCount, pulses.Count, "刷新失败不能播放震动。");
                Assert.AreEqual(failedStops, stops);
            }
            finally
            {
                host.EndBattle();
                HapticFeedbackService.Enabled = enabled;
                pulseProperty.SetValue(null, previousPulse);
                stopProperty.SetValue(null, previousStop);
            }
        }

        private static IEnumerator FramesFor(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
#endif
