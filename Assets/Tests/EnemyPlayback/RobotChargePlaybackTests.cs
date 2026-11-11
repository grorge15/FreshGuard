#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class RobotChargePlaybackTests
    {
        [UnityTest]
        public IEnumerator RealBattleClockLoopsSegmentsFreezesAndResetsBothRobotsOnUpgrade()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host == null || ConfigSystem.Instance.Tables == null || UIModule.UIRoot == null)
            {
                EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
                var deadline = Time.realtimeSinceStartup + 60;
                while ((UIModule.UIRoot == null || GameModule.UI.GetUI<MainMenuUI>() == null) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsNotNull(GameModule.UI.GetUI<MainMenuUI>());
                yield return GameModule.Scene.LoadSceneAsync("Main").ToCoroutine();
                host = Object.FindObjectOfType<BattleShopSceneController>();
            }
            Assert.IsNotNull(host);
            GameModule.UI.CloseUI<MainMenuUI>();
            host.EndBattle();
            try
            {
                yield return host.InitializeAsync().ToCoroutine();
                Assert.IsTrue(host.IsReady, host.InitializationError?.ToString());
                var deadline = Time.realtimeSinceStartup + 15;
                while (!host.IsCombatRunning && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.IsTrue(host.IsCombatRunning);
                host.Bootstrap.StopBalls();
                host.SetFrozen(true);
                var views = (Dictionary<int, GameObject>)typeof(BattleShopSceneController)
                    .GetField("_views", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var board = Object.FindObjectsOfType<PlacementBoardView>().Single(b => host.Placement.GetBoardSide(b) == BattleSide.Player);
                // 只准备确定型号和位置；资源池、接触发射入口与帧时钟均使用真实战斗实现。
                var robots = new List<RobotEntity>();
                foreach (var id in new[] { 704, 701 })
                {
                    var config = ConfigSystem.Instance.Tables.TbRobot.Get(id);
                    var robot = host.Context.Registry.CreateRobotFromConfig(config, BattleSide.Player);
                    Invoke(robot, "TrySetLocation", RobotLocation.Board, (BoardCoordinate?)new BoardCoordinate(robots.Count * 2, 0));
                    var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/AssetRaw/Actor/Prefabs/Robots/" + config.RobotPrefab + ".prefab"), host.transform);
                    instance.transform.position = board.GetCellWorldPosition(new BoardCoordinate(robots.Count * 2, 0));
                    var view = instance.GetComponent<RobotView>();
                    view.ApplyBoardScale(board); view.Bind(host.Context.Registry, robot.InstanceId); view.BindInput(host);
                    Assert.IsTrue(instance.transform.Find("RobotPortrait").GetComponent<SpriteRenderer>().enabled,
                        "战斗组件绑定前应保留静态立绘");
                    views.Add(robot.InstanceId, instance); robots.Add(robot);
                }
                host.Combat.SyncRobots();
                foreach (var robot in robots)
                {
                    var view = views[robot.InstanceId].GetComponent<RobotView>();
                    var animated = view.transform.Find("RobotPortrait/ChargeAnimation").GetComponent<SpriteRenderer>();
                    var initial = animated.sprite;
                    host.SetFrozen(false);
                    deadline = Time.realtimeSinceStartup + 10;
                    while (animated.sprite == initial && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.AreNotSame(initial, animated.sprite, "没有攻击时也应循环当前段");
                    var launched = false;
                    while (!launched && Time.realtimeSinceStartup < deadline)
                    {
                        launched = host.Combat.TryAttackFromContact(host.Context.BattleId, robot.Side, robot.InstanceId);
                        if (!launched) yield return null;
                    }
                    Assert.IsTrue(launched);
                    Assert.AreEqual(1, Counter(robot).AttackCount);
                    host.SetFrozen(true);
                    var frame = animated.sprite;
                    yield return new WaitForSecondsRealtime(.25f);
                    Assert.AreSame(frame, animated.sprite);
                    Invoke(robot, "TrySetDragging", true); view.Render(robot);
                    Assert.IsFalse(animated.enabled);
                    Invoke(robot, "TrySetDragging", false); view.Render(robot);
                    Assert.IsTrue(animated.enabled); Assert.AreSame(frame, animated.sprite);
                    Invoke(robot, "TryUpgrade"); view.Render(robot);
                    Assert.AreEqual(0, Counter(robot).AttackCount);
                    Assert.AreSame(initial, animated.sprite);
                }
                Capture(host.Placement.SceneCamera);
            }
            finally { host.EndBattle(); }
            yield return null;
        }

        private static void Invoke(RobotEntity robot, string name, params object[] args) => typeof(RobotEntity)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(robot, args);
        private static AttackCycleBuffBehavior Counter(RobotEntity robot) =>
            robot.Buffs.Items.Select(b => typeof(BuffObj).GetField("Behavior", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(b)).OfType<AttackCycleBuffBehavior>().Single();
        private static void Capture(Camera camera)
        {
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new RenderTexture(900, 1600, 24);
            var image = new Texture2D(900, 1600, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture; camera.Render(); RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 900, 1600), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "freshguard-robot-charge.png"), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                Object.Destroy(texture); Object.Destroy(image);
            }
        }
    }
}
#endif
