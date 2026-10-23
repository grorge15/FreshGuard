#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class ComboPlaybackTests
    {
        private Scene _scene;
        private BattleEntityRegistry _registry;
        private PhysicalCollisionBall _ball;
        private Transform _popupRoot;

        [SetUp]
        public void SetUp()
        {
            _scene = SceneManager.CreateScene("ComboPhysics", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            _registry = new BattleEntityRegistry();
            var root = new GameObject("ComboPopups"); SceneManager.MoveGameObjectToScene(root, _scene); _popupRoot = root.transform;
            _ball = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab")).GetComponent<PhysicalCollisionBall>();
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _scene);
            _ball.Stop(); _ball.transform.localScale = Vector3.one;
            _ball.GlobalValueProvider = key => key == 34 ? 1.5f : 0f;
            _ball.BindBattle(100, BattleSide.Player, () => true);
            _ball.BindCombo(_registry, _popupRoot);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _ball.Stop(); _registry.Dispose();
            yield return SceneManager.UnloadSceneAsync(_scene);
        }

        [UnityTest]
        public IEnumerator TinyBallCannotEnterSeamsOfAllEightRobots()
        {
            typeof(PhysicalCollisionBall).GetField("radius", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_ball, 0.003f);
            _ball.GetComponent<CircleCollider2D>().radius = 0.003f; // 直径小于原来的 0.021 格间隙。
            var contacts = 0;
            _ball.RobotContactDetected += (ball, id) => contacts++;
            foreach (var name in new[] { "Cannon", "Scatter", "Piercing", "RapidFire", "Impact", "Frost", "Tractor", "Poison" })
            {
                var view = Robot(name);
                Physics2D.SyncTransforms();
                var boxes = view.GetComponentsInChildren<BoxCollider2D>();
                var body = view.GetComponent<PolygonCollider2D>();
                var first = boxes[0];
                var second = boxes.Skip(1).First(c => Vector2.Distance(c.bounds.center, first.bounds.center) < 0.36f);
                var seam = (Vector2)((first.bounds.center + second.bounds.center) * 0.5f);
                var horizontal = Mathf.Abs(first.bounds.center.x - second.bounds.center.x) > 0.1f;
                var start = horizontal ? new Vector2(seam.x, body.bounds.max.y + 0.6f) :
                    new Vector2(body.bounds.max.x + 0.6f, seam.y);
                var previousContacts = contacts;
                _ball.LaunchAt(start, horizontal ? 270f : 180f);
                for (var step = 0; step < 30 && _ball.BounceCount == 0; step++) Tick();
                Assert.Greater(_ball.BounceCount, 0, name + " 的格间接缝必须阻挡小球");
                Assert.AreEqual(1, _ball.ComboCount);
                Assert.AreEqual(previousContacts + 1, contacts, "整体轮廓和格子不能重复发起攻击。");
                Assert.IsFalse(body.OverlapPoint(_ball.GetComponent<Rigidbody2D>().position));
                Assert.Greater(horizontal ? _ball.CurrentDirection.y : _ball.CurrentDirection.x, 0f);
                _ball.Stop();
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActualRobotCellsCountOneEpisodeAndSeparationAllowsAnother()
        {
            foreach (var shape in new[] { "I", "L", "T" })
            {
                var view = Robot(shape);
                var boxes = view.GetPhysicalColliders().OfType<BoxCollider2D>().ToArray();
                Physics2D.SyncTransforms();
                var top = boxes.OrderByDescending(c => c.bounds.max.y).First();
                var start = (Vector2)top.bounds.center + Vector2.up;
                _ball.LaunchAt(start, 270f);
                for (var step = 0; step < 30 && _ball.ComboCount == 0; step++) Tick();
                Assert.AreEqual(1, _ball.ComboCount, shape + "真实碰撞应记一次");
                Assert.Greater(_ball.BounceCount, 0);
                Assert.AreEqual(_ball.Speed, _ball.GetComponent<Rigidbody2D>().velocity.magnitude, 0.001f);
                // Repeated stay callbacks still pass through the actual collision entrypoint.
                for (var step = 0; step < 8; step++) Tick();
                Assert.AreEqual(1, _ball.ComboCount);
                _ball.GetComponent<Rigidbody2D>().position = start;
                Direction(Vector2.down);
                for (var step = 0; step < 30 && _ball.ComboCount == 1; step++) Tick();
                Assert.AreEqual(2, _ball.ComboCount, shape + "完全脱离再撞应增加一次");
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeamContactFreezeAndBerserkTransitionsDoNotDuplicate()
        {
            var view = Robot("I"); Physics2D.SyncTransforms();
                var boxes = view.GetPhysicalColliders().OfType<BoxCollider2D>().OrderBy(c => c.bounds.center.x).ToArray();
            var seam = (Vector2)((boxes[0].bounds.center + boxes[1].bounds.center) * 0.5f);
            _ball.LaunchAt(seam + Vector2.up, 270f);
            for (var step = 0; step < 30 && _ball.ComboCount == 0; step++) Tick();
            Assert.AreEqual(1, _ball.ComboCount);
            var count = _ball.ComboCount;
            _ball.SetFrozen(true);
            var position = _ball.transform.position;
            for (var step = 0; step < 100; step++) Tick();
            Assert.AreEqual(position, _ball.transform.position); Assert.AreEqual(count, _ball.ComboCount);
            _ball.SetFrozen(false);
            for (var step = 0; step < 10; step++)
            {
                Direction(Vector2.down); Tick();
                Assert.AreEqual(count, _ball.ComboCount, "恢复后接触缓存重建不能迟发重复计数");
            }
            _ball.SetBerserk(true); Assert.AreEqual(0, _ball.ComboCount);
            _ball.SetBerserk(false); Direction(Vector2.down); Tick();
            Assert.AreEqual(0, _ball.ComboCount, "同一接触暴走切换不能补计");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SlidingAcrossRobotCellsKeepsOneContactEpisode()
        {
            var view = Robot("I"); Physics2D.SyncTransforms();
                var boxes = view.GetPhysicalColliders().OfType<BoxCollider2D>().OrderBy(c => c.bounds.center.x).ToArray();
            _ball.LaunchAt((Vector2)boxes[0].bounds.center + Vector2.up, 270f);
            for (var step = 0; step < 30 && _ball.ComboCount == 0; step++) Tick();
            Assert.AreEqual(1, _ball.ComboCount);
            var sphere = _ball.GetComponent<CircleCollider2D>();
            var body = view.GetComponent<PolygonCollider2D>();
            var initial = new HashSet<Collider2D>(boxes.Where(c => sphere.Distance(c).distance <= 0.03f));
            Assert.IsNotEmpty(initial, "必须从真实接触开始横向滑动");
            var changedCell = false;
            for (var step = 0; step < 50; step++)
            {
                Direction(new Vector2(0.5f, -1f).normalized); Tick();
                Assert.IsTrue(sphere.IsTouching(body), "滑动必须保持与整体轮廓真实接触。");
                var touching = boxes.Where(c => sphere.Distance(c).distance <= 0.03f).ToArray();
                Assert.IsNotEmpty(touching, "横向移动过程不能完全离开机器人");
                Assert.AreEqual(1, _ball.ComboCount, "持续跨格接触只能计一次");
                if (touching.All(c => !initial.Contains(c))) { changedCell = true; break; }
            }
            Assert.IsTrue(changedCell, "必须实际离开起始子Collider并接触另一个格子");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BothBoundMainBallsCountIndependentlyAndWallDoesNotRenewCombo()
        {
            var player = Robot("I"); Physics2D.SyncTransforms();
            _ball.LaunchAt((Vector2)player.transform.position + Vector2.up, 270f);
            for (var step = 0; step < 30 && _ball.ComboCount == 0; step++) Tick();
            Assert.AreEqual(1, _ball.ComboCount);
            var opponentBall = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab")).GetComponent<PhysicalCollisionBall>();
            SceneManager.MoveGameObjectToScene(opponentBall.gameObject, _scene);
            opponentBall.Stop(); opponentBall.transform.localScale = Vector3.one;
            opponentBall.GlobalValueProvider = key => key == 34 ? 1.5f : 0f;
            opponentBall.BindBattle(100, BattleSide.Opponent, () => true); opponentBall.BindCombo(_registry, _popupRoot);
            var opponent = Robot("I", BattleSide.Opponent); opponent.transform.position = Vector3.right * 10f;
            Physics2D.SyncTransforms(); opponentBall.LaunchAt((Vector2)opponent.transform.position + Vector2.up, 270f);
            for (var step = 0; step < 30 && opponentBall.ComboCount == 0; step++)
            {
                opponentBall.SendMessage("FixedUpdate"); Tick();
            }
            Assert.AreEqual(1, opponentBall.ComboCount); Assert.AreEqual(1, _ball.ComboCount);
            // Remove the robot from the wall trajectory so this checks wall-only time.
            player.gameObject.SetActive(false);
            var wall = new GameObject("Wall"); SceneManager.MoveGameObjectToScene(wall, _scene);
            wall.AddComponent<BoxCollider2D>(); wall.transform.position = new Vector3(0, 3, 0);
            Physics2D.SyncTransforms();
            var bounces = _ball.BounceCount;
            for (var step = 0; step < 80; step++) Tick();
            Assert.Greater(_ball.BounceCount, bounces, "必须真实碰过墙");
            Assert.AreEqual(0, _ball.ComboCount, "墙不能续接Combo");
            opponentBall.Stop(); yield return null;
        }

        private RobotView Robot(string shape, BattleSide side = BattleSide.Player)
        {
            shape = shape == "I" ? "Cannon" : shape == "L" ? "Scatter" : shape == "T" ? "Impact" : shape;
            var view = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/Robot" + shape + ".prefab")).GetComponent<RobotView>();
            SceneManager.MoveGameObjectToScene(view.gameObject, _scene);
            var robot = _registry.CreateRobot(view.ConfigId, side, 1, 1);
            typeof(RobotEntity).GetMethod("TrySetLocation", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(robot, new object[] { RobotLocation.Board, new BoardCoordinate(0, 0) });
            view.Bind(_registry, robot.InstanceId); return view;
        }
        private void Tick() { _ball.SendMessage("FixedUpdate"); _scene.GetPhysicsScene2D().Simulate(0.02f); }
        private void Direction(Vector2 direction) => typeof(PhysicalCollisionBall).GetField("_direction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_ball, direction);
    }
}
#endif
