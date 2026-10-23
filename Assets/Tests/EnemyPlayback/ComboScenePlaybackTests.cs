#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class ComboScenePlaybackTests
    {
        [UnityTest]
        public IEnumerator StartupSceneBindsBothSidesFreezesPopupsAndCleansUpOnRestart()
        {
            var host = Object.FindObjectOfType<BattleShopSceneController>();
            if (host != null && host.IsReady)
            {
                host.EndBattle();
                yield return host.InitializeAsync().ToCoroutine();
            }
            else EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                host = Object.FindObjectOfType<BattleShopSceneController>();
                if (host != null && host.IsReady) break;
                yield return null;
            }
            Assert.IsNotNull(host); Assert.IsTrue(host.IsReady, "真实资源和Combo装配应初始化成功");
            host.SetFrozen(true);
            var robots = new RobotView[2];
            var sides = new[] { BattleSide.Player, BattleSide.Opponent };
            for (var index = 0; index < sides.Length; index++)
            {
                var side = sides[index];
                var state = host.Context.GetState(side); RobotEntity robot;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryPurchase(side, 0, state.Slots[0].Offer.OfferId, out robot));
                var board = Object.FindObjectsOfType<PlacementBoardView>().Single(b => host.Placement.GetBoardSide(b) == side);
                BoardCoordinate? anchor = null;
                for (var row = 1; row < board.Model.Rows && !anchor.HasValue; row++)
                    for (var col = 1; col < board.Model.Columns && !anchor.HasValue; col++)
                    {
                        var cell = new BoardCoordinate(col, row);
                        if (board.Model.Evaluate(cell, robot.Definition).IsValid) anchor = cell;
                    }
                Assert.IsTrue(anchor.HasValue);
                RobotDragSession session;
                Assert.AreEqual(ShopOperationResult.Success, host.Context.TryBeginDrag(side, RobotDragSource.Owned(robot.InstanceId), out session));
                yield return host.DeployDragAsync(session, new BoardPlacementTarget(board, anchor.Value), CancellationToken.None)
                    .ToCoroutine(result => Assert.AreEqual(ShopOperationResult.Success, result));
                robots[index] = Object.FindObjectsOfType<RobotView>().Single(v => v.InstanceId == robot.InstanceId);
            }
            host.SetFrozen(false);
            deadline = Time.realtimeSinceStartup + 8f;
            while (!host.IsCombatRunning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(host.IsCombatRunning);
            host.Bootstrap.StopBalls();
            var balls = new[] { host.Bootstrap.PlayerBall, host.Bootstrap.OpponentBall };
            for (var index = 0; index < 2; index++)
            {
                var ball = balls[index];
                // Keep actual table34, use deterministic bounce angles for a repeatable trajectory.
                var provider = ball.GlobalValueProvider;
                ball.GlobalValueProvider = key => key == 4 || key == 6 ? 0f : provider(key);
                var cell = robots[index].GetPhysicalColliders().OrderByDescending(c => c.bounds.max.y).First();
                var start = (Vector2)cell.bounds.center + Vector2.up * 0.6f;
                ball.LaunchAt(start, 270f);
                for (var expected = 1; expected <= (index == 0 ? 10 : 1); expected++)
                {
                    if (expected > 1)
                    {
                        ball.GetComponent<Rigidbody2D>().position = start;
                        Physics2D.SyncTransforms();
                        // Let the physics engine observe the separation before the next approach.
                        typeof(PhysicalCollisionBall).GetField("_direction", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ball, Vector2.up);
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                        typeof(PhysicalCollisionBall).GetField("_direction", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ball, Vector2.down);
                    }
                    deadline = Time.realtimeSinceStartup + 2f;
                    while (ball.ComboCount < expected && Time.realtimeSinceStartup < deadline) yield return new WaitForFixedUpdate();
                    Assert.AreEqual(expected, ball.ComboCount, "主球应通过真实物理碰撞计数");
                }
                if (index == 0)
                {
                    host.SetFrozen(true);
                    var popup = Object.FindObjectsOfType<ComboPopupView>().Single();
                    Assert.AreEqual("10 Combo", popup.GetComponent<TextMesh>().text);
                    var position = popup.transform.position; var alpha = popup.GetComponent<TextMesh>().color.a;
                    var freezeUntil = Time.realtimeSinceStartup + 0.2f;
                    while (Time.realtimeSinceStartup < freezeUntil) yield return null;
                    Assert.AreEqual(10, ball.ComboCount); Assert.AreEqual(position, popup.transform.position);
                    Assert.AreEqual(alpha, popup.GetComponent<TextMesh>().color.a);
                    Capture(host.Placement.SceneCamera);
                    host.SetFrozen(false);
                }
            }
            Assert.AreEqual(1, balls[1].ComboCount);
            Assert.GreaterOrEqual(balls[0].ComboCount, 10);
            var oldBattle = host.Context.BattleId;
            host.EndBattle(); yield return null;
            Assert.AreEqual(0, balls[0].ComboCount); Assert.AreEqual(0, balls[1].ComboCount);
            Assert.AreEqual(0, Object.FindObjectsOfType<ComboPopupView>().Length);
            Assert.IsNull(GameObject.Find("ComboPopups"));
            yield return host.InitializeAsync().ToCoroutine();
            Assert.IsTrue(host.IsReady); Assert.AreNotEqual(oldBattle, host.Context.BattleId);
            Assert.AreEqual(0, balls[0].ComboCount); Assert.AreEqual(0, balls[1].ComboCount);
        }

        private static void Capture(Camera camera)
        {
            var target = new RenderTexture(1200, 800, 24); var texture = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); texture.Apply();
                System.IO.File.WriteAllBytes(Application.dataPath + "/../Library/ComboScenePreview.png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = active;
                Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
#endif
