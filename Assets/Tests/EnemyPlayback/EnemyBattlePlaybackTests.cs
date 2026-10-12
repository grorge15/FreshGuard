#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class EnemyBattlePlaybackTests
    {
        [UnityTest]
        public IEnumerator LaunchSceneRunsFirstWaveFreezesAndRestartsWithoutGhostEnemies()
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            BattleShopSceneController host = null;
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
            Assert.AreEqual(0, host.Enemies.LiveCount, "准备倒计时内不得创建敌人实体");
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
            while (!host.Towers.IsEnded && !host.Enemies.IsWaveCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(host.Towers.IsEnded || host.Enemies.IsWaveCompleted, "第一波应完成或因塔被击破结束");
            if (host.Towers.IsEnded)
            {
                Assert.IsTrue(host.Towers.PlayerTower.CurrentHp == 0 || host.Towers.OpponentTower.CurrentHp == 0,
                    "结束对局时至少有一座塔应被敌人接触扣至 0 点");
            }
            else
            {
                Assert.AreEqual(5, host.Towers.PlayerTower.CurrentHp);
                Assert.AreEqual(5, host.Towers.OpponentTower.CurrentHp);
            }
            var oldBattle = host.Context.BattleId;
            host.EndBattle();
            yield return null;
            Assert.AreEqual(0, Object.FindObjectsOfType<EnemyView>().Length);
            host.EndBattle();
            host.SendMessage("Start");
            deadline = Time.realtimeSinceStartup + 30f;
            while (!host.IsReady && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsReady);
            Assert.AreNotEqual(oldBattle, host.Context.BattleId);
            Assert.AreEqual(5, host.Towers.PlayerTower.CurrentHp);
            Assert.AreEqual(0, host.Enemies.LiveCount);
            Assert.AreEqual(0, host.Enemies.GetSpawnedCount(BattleSide.Player));
            Assert.AreEqual(DamageResult.Ignored, host.ApplyTowerDamage(oldBattle, BattleSide.Player, 100));
            host.EndBattle();
        }
    }
}
#endif
