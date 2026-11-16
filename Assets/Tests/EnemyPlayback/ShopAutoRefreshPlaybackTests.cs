#if UNITY_EDITOR
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class ShopAutoRefreshPlaybackTests
    {
        [UnityTest]
        public IEnumerator EmptyShopAutomaticallyChargesAndPlaysRefreshAfterFiveSeconds()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && ConfigSystem.Instance.Tables != null) host.EndBattle();
            else
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                var deadline = Time.realtimeSinceStartup + 60f;
                while ((UIModule.UIRoot == null || GameModule.UI.GetUI<MainMenuUI>() == null) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>());
                yield return GameModule.Scene.LoadSceneAsync("Main").ToCoroutine();
                host = Object.FindObjectOfType<BattleShopSceneController>();
            }
            GameModule.UI.CloseUI<MainMenuUI>();
            yield return host.InitializeAsync().ToCoroutine();
            var openDeadline = Time.realtimeSinceStartup + 60f;
            while (!host.TryOpenRobotInteraction(host.Context.BattleId) && Time.realtimeSinceStartup < openDeadline)
                yield return null;
            Assert.IsTrue(host.CanInteractWithRobots);
            var previousTimeScale = Time.timeScale;
            try
            {
                var shop = host.CombatWindow.Shop;
                var state = host.Context.GetState(BattleSide.Player);
                var coins = state.EnergyCoins;
                // 只构造空店条件；让实际 UI 帧更新、刷新事务与动画自行推进。
                foreach (var slot in state.Slots)
                    typeof(ShopSlot).GetProperty("Offer").SetValue(slot, null);
                shop.RefreshView();
                yield return new WaitForSeconds(3f);
                Assert.AreEqual(coins, state.EnergyCoins, "满5秒前不能刷新扣费。");
                Time.timeScale = 0f;
                yield return new WaitForSecondsRealtime(2.5f);
                Assert.AreEqual(coins, state.EnergyCoins, "暂停时间不计入自动刷新。");
                Time.timeScale = previousTimeScale;
                var refreshDeadline = Time.realtimeSinceStartup + 5f;
                while (state.EnergyCoins == coins && Time.realtimeSinceStartup < refreshDeadline) yield return null;
                Assert.AreEqual(coins - host.Context.RefreshPrice, state.EnergyCoins);
                Assert.IsTrue(shop.IsRefreshing, "自动刷新复用老虎机动画。");
                foreach (var slot in state.Slots) Assert.IsNotNull(slot.Offer);
                yield return new WaitForSeconds(6f);
                Assert.AreEqual(coins - host.Context.RefreshPrice, state.EnergyCoins, "补货后只扣费一次。");
                Assert.IsFalse(shop.IsRefreshing);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                host.EndBattle();
            }
        }
    }
}
#endif
