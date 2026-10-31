using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class BallPlacementRecoveryTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleEntityRegistry _registry;
        private PlacementBoardView _board;
        private PhysicalCollisionBall _ball;
        private Rigidbody2D _body;
        private Scene _scene;
        private SimulationMode2D _simulationMode;
        private UnityEngine.Random.State _randomState;
        private bool _running;
        private int _attacks;
        private BattleShopContext _glassContext;
        private BattleShopSceneController _glassHost;
        private readonly BoardCoordinate _origin = new BoardCoordinate(2, 2);

        [SetUp]
        public void SetUp()
        {
            _simulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            _randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(19);
            _scene = Application.isPlaying ? SceneManager.CreateScene("BallRecoveryPlayerLoop_" + Guid.NewGuid()) :
                UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            _registry = new BattleEntityRegistry();
            _running = true;
            _attacks = 0;
            _board = NewObject("Board").AddComponent<PlacementBoardView>();
            _board.transform.position = new Vector3(100f, 100f, 0f);
            Set(_board, "_columns", 5); Set(_board, "_rows", 5); Set(_board, "_cellSize", 1f);
            Set(_board, "_model", new BoardModel(5, 5)); Set(_board, "_initialized", true);
            var go = NewObject("Ball"); go.SetActive(false);
            _ball = go.AddComponent<PhysicalCollisionBall>();
            go.GetComponent<CircleCollider2D>().radius = 0.1f; Set(_ball, "speed", 1f);
            go.SetActive(true);
            if (Get(_ball, "_body") == null) Invoke(_ball, "Awake");
            _body = go.GetComponent<Rigidbody2D>();
            _ball.GlobalValueProvider = key => key == 34 ? 10f : key == 17 ? 0.7f : key == 6 ? 10f : 0f;
            _ball.BindBattle(1, BattleSide.Player, () => _running);
            var popup = NewObject("Popup").AddComponent<ComboPopupView>();
            Set(_ball, "_comboPopupPrefab", popup);
            _ball.BindCombo(_registry, NewObject("PopupRoot").transform);
            _ball.RobotContactDetected += (ball, id) => _attacks++;
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_ball != null) _ball.Stop();
            if (_glassHost != null) typeof(BattleShopSceneController).GetProperty("Context").SetValue(_glassHost, null);
            foreach (var go in _objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear();
            _registry?.Dispose();
            _glassContext?.Dispose(); _glassContext = null; _glassHost = null;
            if (_scene.IsValid())
            {
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(_scene);
                else UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(_scene);
            }
            Physics2D.simulationMode = _simulationMode;
            UnityEngine.Random.state = _randomState;
        }

        [Test]
        public void InitializationPreservesAuthoredColliderRadiusAndOffset()
        {
            var go = NewObject("AuthoredBall"); go.SetActive(false);
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.13f;
            collider.offset = new Vector2(0.02f, -0.03f);
            var ball = go.AddComponent<PhysicalCollisionBall>();
            go.SetActive(true);
            if (Get(ball, "_body") == null) Invoke(ball, "Awake");
            Assert.That(collider.radius, Is.EqualTo(0.13f).Within(0.0001f));
            Assert.AreEqual(new Vector2(0.02f, -0.03f), collider.offset);
        }

        [Test]
        public void WorldRadiusFollowsColliderChangesAndWorldScaleBeforeInitialization()
        {
            var go = NewObject("AuthoredBall"); go.SetActive(false);
            var collider = go.AddComponent<CircleCollider2D>();
            collider.radius = 0.13f;
            var ball = go.AddComponent<PhysicalCollisionBall>();
            go.transform.SetParent(_board.transform, false);
            _board.transform.localScale = Vector3.one * 1.5f;
            go.transform.localScale = new Vector3(0.6f, 0.8f, 1f);
            Assert.That(ball.WorldRadius, Is.EqualTo(0.156f).Within(0.0001f));
            collider.radius = 0.07f;
            Assert.That(ball.WorldRadius, Is.EqualTo(0.084f).Within(0.0001f));
        }

        [Test]
        public void PlacementAccelerationDecaysLinearlyToNormalSpeed()
        {
            ConfigureAcceleration();
            var source = Robot(_origin);
            _ball.ResolvePlacementOverlap(source, _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
            source.GetPhysicalCollider().enabled = false;
            for (int i = 0; i < 10; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_body.velocity.magnitude, Is.EqualTo(4.6f).Within(0.0001f));
            for (int i = 0; i < 15; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void AnotherEligiblePlacementRefreshesTimeAndBonusWithoutStacking()
        {
            ConfigureAcceleration();
            var first = Robot(_origin);
            _ball.ResolvePlacementOverlap(first, _board);
            for (int i = 0; i < 10; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4.6f).Within(0.0001f));
            _ball.ResolvePlacementOverlap(first, _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4.6f).Within(0.0001f), "重复提交同一碰撞对不能刷新。");
            var second = Robot(new BoardCoordinate(1, 1));
            second.transform.position = _body.position; Physics2D.SyncTransforms();
            _ball.ResolvePlacementOverlap(second, _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
            for (int i = 0; i < 25; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void DisabledRobotAccelerationDoesNotRefreshExistingDecay()
        {
            ConfigureAcceleration();
            var first = Robot(_origin); _ball.ResolvePlacementOverlap(first, _board);
            for (int i = 0; i < 10; i++) Invoke(_ball, "FixedUpdate");
            var provider = _ball.GlobalValueProvider;
            _ball.GlobalValueProvider = id => id == 14 ? 0f : provider(id);
            var second = Robot(new BoardCoordinate(1, 1));
            second.transform.position = _body.position; Physics2D.SyncTransforms();
            _ball.ResolvePlacementOverlap(second, _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4.6f).Within(0.0001f));
        }

        [Test]
        public void AccelerationFreezesWithBallAndRespectsMapAndDragMultipliers()
        {
            _ball.Stop(); _ball.SetMapScale(1.2f); _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
            ConfigureAcceleration();
            var source = Robot(_origin); _ball.ResolvePlacementOverlap(source, _board);
            _ball.SetDragging(true);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4.2f).Within(0.0001f));
            _ball.SetFrozen(true);
            for (int i = 0; i < 30; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_body.velocity.magnitude, Is.Zero);
            _ball.SetFrozen(false);
            Assert.That(_body.velocity.magnitude, Is.EqualTo(4.2f).Within(0.0001f));
            source.GetPhysicalCollider().enabled = false;
            for (int i = 0; i < 25; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(3.36f).Within(0.0001f));
        }

        [TestCase("Stop")]
        [TestCase("LaunchAt")]
        [TestCase("BindBattle")]
        [TestCase("Disable")]
        public void ResetClearsBounceAcceleration(string transition)
        {
            ConfigureAcceleration();
            _ball.ResolvePlacementOverlap(Robot(_origin), _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
            if (transition == "Stop") _ball.Stop();
            else if (transition == "LaunchAt") _ball.LaunchAt(_body.position, 30f);
            else if (transition == "BindBattle") _ball.BindBattle(2, BattleSide.Player, () => _running);
            else { _ball.gameObject.SetActive(false); if (!Application.isPlaying) Invoke(_ball, "OnDisable"); }
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void FiftiethPlacementComboSwitchesToRageBeforeApplyingAcceleration()
        {
            ConfigureTestSpeeds();
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
            ConfigureAcceleration();
            typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(_ball, 49);
            _ball.ComboThresholdReached += ball => ball.SetBerserk(true);
            var source = Robot(_origin); _ball.ResolvePlacementOverlap(source, _board);
            Assert.IsTrue(_ball.IsBerserk);
            Assert.That(_body.velocity.magnitude, Is.EqualTo(7f).Within(0.0001f));
            source.GetPhysicalCollider().enabled = false;
            for (int i = 0; i < 10; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(6.6f).Within(0.0001f));
            _ball.SetBerserk(false);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4.6f).Within(0.0001f));
            _ball.SetBerserk(true);
            for (int i = 0; i < 15; i++) Invoke(_ball, "FixedUpdate");
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(6f).Within(0.0001f));
        }

        private void ConfigureTestSpeeds()
        {
            var tables = WaveTestData.SingleWaveTables();
            var bytes = new Luban.ByteBuf(); bytes.WriteSize(tables.TbGlobal.DataList.Count);
            foreach (var row in tables.TbGlobal.DataList)
            {
                bytes.WriteInt(row.Id); bytes.WriteFloat(row.Id == 2 ? 4f : row.Id == 3 ? 6f : row.Value);
                bytes.WriteSize(row.IntArrVal.Length); foreach (int value in row.IntArrVal) bytes.WriteInt(value);
            }
            tables.TbGlobal = new GameConfig.globalcfg.TbGlobal(bytes);
            _ball.Stop(); _ball.ConfigureFromTables(tables);
        }

        [Test]
        public void GeometricTrappedEndpointUsesWallAccelerationSwitch()
        {
            ConfigureAcceleration(wall: true, robot: false);
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(4f).Within(0.0001f));
            foreach (var view in _board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().enabled = false;
            for (int i = 0; i < 15 && _ball.BounceCount == 1; i++) Tick();
            Assert.Greater(_ball.BounceCount, 1);
            Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
        }

        private void ConfigureAcceleration(bool wall = true, bool robot = true)
        {
            Set(_ball, "speed", 4f);
            var provider = _ball.GlobalValueProvider;
            _ball.GlobalValueProvider = id => id == 11 ? 1f : id == 12 ? 0.5f : id == 13 ? (wall ? 1f : 0f) :
                id == 14 ? (robot ? 1f : 0f) : provider(id);
        }

        private void BeginAccelerationCollision(bool hitRobot, bool enabled)
        {
            _ball.Stop();
            ConfigureAcceleration(wall: !hitRobot && enabled, robot: hitRobot && enabled);
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 0f);
            if (hitRobot) Robot(new BoardCoordinate(3, 2));
            else
            {
                var wall = NewObject("AccelerationWall").AddComponent<BoxCollider2D>();
                wall.transform.position = _body.position + Vector2.right;
                wall.size = new Vector2(0.2f, 2f);
            }
            Physics2D.SyncTransforms();
        }

        [Test]
        public void FreeNeighborChangesDirectionWithoutTeleportAndCountsPlacementOnce()
        {
            var robot = Robot(_origin);
            var start = _body.position;
            _ball.ResolvePlacementOverlap(robot, _board);
            Assert.That(Vector2.Distance(start, _body.position), Is.LessThan(0.0001f), "有空位只能改变方向，不能立即瞬移。");
            Assert.AreEqual("Escaping", State);
            Assert.AreEqual(1, _ball.ComboCount); Assert.AreEqual(1, _ball.BounceCount); Assert.AreEqual(1, _attacks);
            Assert.IsTrue(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), robot.GetPhysicalCollider()));
            _ball.ResolvePlacementOverlap(robot, _board);
            Assert.AreEqual(1, _ball.ComboCount); Assert.AreEqual(1, _attacks);
            for (var i = 0; i < 80 && State != "None"; i++) Tick();
            Assert.AreEqual("None", State);
            Assert.IsFalse(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), robot.GetPhysicalCollider()));
            Assert.GreaterOrEqual(BallBounceMath.AxisDistance(_ball.CurrentDirection), 9.999f);
        }

        [Test]
        public void ThreeCellRobotEscapeFinishesInNarrowFreeCellWithObstacleAhead()
        {
            var source = BeginThreeCellEscape();
            Assert.AreEqual("Escaping", State);
            Assert.Greater(_ball.CurrentDirection.x, 0.99f);
            for (int i = 0; i < 20 && State != "None"; i++) Tick();
            Assert.AreEqual("None", State, "已完全脱离时，即使前方下一步会撞墙也必须交还普通反弹流程。");
            // EditMode不发送常规碰撞回调，求解器可能临时把接触中的刚体速度置零；检查下一次业务步的输出。
            Invoke(_ball, "FixedUpdate");
            Assert.That(_body.velocity.magnitude, Is.EqualTo(6f).Within(0.0001f));
            Assert.GreaterOrEqual(BallBounceMath.AxisDistance(_ball.CurrentDirection), 9.999f);
            Assert.IsFalse(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), source.GetPhysicalCollider()));
            Assert.AreEqual(1, _attacks);
        }

        private RobotView BeginThreeCellEscape()
        {
            // 主场景格距0.35、地图倍率1.2，球半径0.14；右侧空格外还有障碍。
            Set(_board, "_cellSize", 0.35f);
            _board.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            _ball.Stop(); Set(_ball, "speed", 5f);
            _ball.GetComponent<CircleCollider2D>().radius = 0.14f;
            _ball.SetMapScale(1.2f);
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
            var source = Robot(_origin);
            string id = _board.Model.GetOccupant(_origin);
            _board.Model.Release(id);
            Assert.IsTrue(_board.Model.TryPlace(new BoardCoordinate(2, 1), new PlaceableDefinition(id, 1, 3)));
            source.GetPhysicalCollider().SetPath(0, new[] { new Vector2(-0.175f,-0.525f), new Vector2(0.175f,-0.525f),
                new Vector2(0.175f,0.525f), new Vector2(-0.175f,0.525f) });
            // 只给右侧留空，确保覆盖球的长条机器人提交后的占格参与选路。
            for (int row = 1; row <= 3; row++) for (int column = 1; column <= 3; column++)
            {
                var cell = new BoardCoordinate(column, row);
                if (!_board.Model.IsOccupied(cell) && !(column == 3 && row == 2))
                    Assert.IsTrue(_board.Model.TryPlace(cell, new PlaceableDefinition(column + ":" + row, 1, 1)));
            }
            var wall = NewObject("ObstacleBeyondFreeCell").AddComponent<BoxCollider2D>();
            wall.transform.position = _board.GetCellWorldPosition(new BoardCoordinate(4, 2));
            wall.size = new Vector2(0.42f, 1.26f);
            Physics2D.SyncTransforms();
            _ball.ResolvePlacementOverlap(source, _board);
            return source;
        }

        [TestCase(0.9f)]
        [TestCase(1.2f)]
        public void ScaledBoardAcceptsFreeCellThatFitsActualBallNextToGlass(float boardScale)
        {
            ConfigureScaledRecoveryBoard(boardScale);
            var source = Robot(_origin);
            source.GetPhysicalCollider().SetPath(0, new[] { new Vector2(-0.154f,-0.154f), new Vector2(0.154f,-0.154f),
                new Vector2(0.154f,0.154f), new Vector2(-0.154f,0.154f) });
            // 八邻格仅右侧可用；下方玻璃和球的实际形状不相交。
            for (int row = 1; row <= 3; row++) for (int column = 1; column <= 3; column++)
            {
                var cell = new BoardCoordinate(column, row);
                if (!_board.Model.IsOccupied(cell) && !(column == 3 && row == 2))
                    Assert.IsTrue(_board.Model.TryPlace(cell, new PlaceableDefinition(column + ":" + row, 1, 1)));
            }
            AddGridObstacle(new BoardCoordinate(3, 1));
            var start = _body.position;
            _ball.ResolvePlacementOverlap(source, _board);
            Assert.AreEqual("Escaping", State, "不能把物理系统的接触预留量额外扩到球半径上，误判可容纳球的空格。");
            Assert.That(Vector2.Distance(start, _body.position), Is.LessThan(0.0001f));
            for (int i = 0; i < 20 && State != "None"; i++) Tick();
            Assert.AreEqual("None", State);
            Assert.IsFalse(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), source.GetPhysicalCollider()));
            Assert.AreEqual(1, _attacks);
        }

        [Test]
        public void PausedOpponentLayoutEscapesTrappedStateWithoutWaitingForAutoRelaunch()
        {
            BeginPausedOpponentLayout();
            var start = _body.position;
            for (int i = 0; i < 15 && State != "None"; i++) Tick();
            Assert.AreEqual("None", State, "现场存在可通行空格，必须自行退出Trapped状态。");
            Assert.Greater(Vector2.Distance(start, _body.position), 0.02f);
            Assert.GreaterOrEqual(BallBounceMath.AxisDistance(_ball.CurrentDirection), 9.999f);
        }

        [Test]
        public void FreeSlotTypeStillRejectsBallTooLargeForPhysicalGap()
        {
            ConfigureScaledRecoveryBoard(0.9f);
            // 格子类型可通行并不表示球体能够穿过：0.18半径大于0.1575半格宽。
            _ball.GetComponent<CircleCollider2D>().radius = 0.18f;
            var recovery = (BallPlacementRecovery)Get(_ball, "_placementRecovery");
            recovery.Begin(_board, _body.position);
            var cell = new BoardCoordinate(1, 2);
            AddGridObstacle(new BoardCoordinate(1, 1));
            Assert.IsTrue(_board.Model.CanEnergyBallPass(cell));
            Assert.IsFalse(_board.Model.IsOccupied(cell));
            var free = typeof(PhysicalCollisionBall).GetMethod("IsPlacementPositionFree", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsFalse((bool)free.Invoke(_ball, new object[] { (Vector2)_board.GetCellWorldPosition(cell) }));
        }

        private void ConfigureScaledRecoveryBoard(float scale)
        {
            Set(_board, "_cellSize", 0.35f);
            _board.transform.localScale = new Vector3(scale, scale, 1f);
            _ball.Stop(); Set(_ball, "speed", 4f);
            _ball.GetComponent<CircleCollider2D>().radius = 0.14f;
            _ball.BindBattle(1, BattleSide.Opponent, () => _running);
            _ball.BindCombo(_registry, NewObject("OpponentPopupRoot").transform);
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
        }

        private BoxCollider2D AddGridObstacle(BoardCoordinate cell)
        {
            var box = NewObject("GridGlass_" + cell.Column + "_" + cell.Row).AddComponent<BoxCollider2D>();
            box.transform.SetParent(_board.transform, false);
            box.transform.position = _board.GetCellWorldPosition(cell);
            box.size = Vector2.one * 0.35f;
            Physics2D.SyncTransforms();
            return box;
        }

        private void BeginPausedOpponentLayout()
        {
            ConfigureScaledRecoveryBoard(0.9f);
            // 2026-10-29暂停现场：原格下方整排玻璃、右侧玻璃，上方L形机器人。
            for (int column = 1; column <= 3; column++)
            {
                AddGridObstacle(new BoardCoordinate(column, 1));
                _board.Model.TryPlace(new BoardCoordinate(column, 1), new PlaceableDefinition("glass" + column, 1, 1));
            }
            AddGridObstacle(new BoardCoordinate(3, 2));
            _board.Model.TryPlace(new BoardCoordinate(3, 2), new PlaceableDefinition("rightGlass", 1, 1));
            var robot = Robot(new BoardCoordinate(1, 3));
            _board.Model.Release(robot.InstanceId.ToString());
            Assert.IsTrue(_board.Model.TryPlace(new BoardCoordinate(1, 3), new PlaceableDefinition(robot.InstanceId.ToString(),
                new[] { new BoardCoordinate(0, 0), new BoardCoordinate(1, 0), new BoardCoordinate(0, 1) })));
            robot.GetPhysicalCollider().SetPath(0, new[] { new Vector2(-0.154f,-0.154f), new Vector2(0.504f,-0.154f),
                new Vector2(0.504f,0.154f), new Vector2(0.154f,0.154f), new Vector2(0.154f,0.504f), new Vector2(-0.154f,0.504f) });
            _board.Model.TryPlace(new BoardCoordinate(3, 3), new PlaceableDefinition("unreachable", 1, 1));
            var recovery = (BallPlacementRecovery)Get(_ball, "_placementRecovery");
            recovery.Begin(_board, _body.position); recovery.EnterTrapped(0, false);
            // 以原格中心为参照，完整保留现场相对位置及离开真实边线后的斜向方向。
            var center = (Vector2)_board.GetCellWorldPosition(_origin);
            var position = center + new Vector2(-0.002863f, -0.012696f);
            _body.position = position; _ball.transform.position = position;
            Set(_ball, "_direction", new Vector2(-0.730274f, -0.683154f));
            Physics2D.SyncTransforms();
        }

        [Test]
        public void AllEightOccupiedSnapsToRealEdgeMidpointThenCountsEndpointsWithoutAttacks()
        {
            var source = FillNeighbors();
            _ball.ResolvePlacementOverlap(source, _board);
            Assert.AreEqual("Trapped", State);
            var p = _board.transform.InverseTransformPoint(_body.position);
            Assert.That(Mathf.Abs(p.x) + Mathf.Abs(p.y), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Mathf.Min(Mathf.Abs(p.x), Mathf.Abs(p.y)), Is.LessThan(0.0001f));
            for (var i = 0; i < 80; i++) Tick();
            Assert.Greater(_ball.ComboCount, 1); Assert.AreEqual(1, _attacks);
            Assert.AreEqual("Trapped", State);
        }

        [Test]
        public void TrappedRechecksVacancyAndEscapesWithoutAnotherPlacementCount()
        {
            var source = FillNeighbors();
            _ball.ResolvePlacementOverlap(source, _board);
            var p = _board.transform.InverseTransformPoint(_body.position);
            var target = new BoardCoordinate(2 + Math.Sign(p.x), 2 + Math.Sign(p.y));
            var id = _board.Model.GetOccupant(target);
            foreach (var view in _board.GetComponentsInChildren<RobotView>())
                if (view.InstanceId.ToString() == id || view.GetPhysicalCollider().OverlapPoint(_board.GetCellWorldPosition(target)))
                    view.gameObject.SetActive(false);
            _board.Model.Release(id);
            Tick();
            Assert.AreNotEqual("Trapped", State);
            Assert.AreEqual(1, _attacks, "切换脱离状态本身不重复攻击。");
            for (var i = 0; i < 100 && State != "None"; i++) Tick();
            Assert.AreEqual("None", State);
        }

        [TestCase("Stop")]
        [TestCase("LaunchAt")]
        [TestCase("BindBattle")]
        public void ResetReleasesIgnoredPairsAndRecoveryState(string transition)
        {
            var source = FillNeighbors();
            _ball.ResolvePlacementOverlap(source, _board);
            var colliders = _board.GetComponentsInChildren<Collider2D>();
            if (transition == "Stop") _ball.Stop();
            else if (transition == "LaunchAt") _ball.LaunchAt(Vector2.zero, 30f);
            else _ball.BindBattle(2, BattleSide.Player, () => _running);
            Assert.AreEqual("None", State);
            foreach (var collider in colliders)
                Assert.IsFalse(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), collider));
        }

        [Test]
        public void FreezeDoesNotMoveOrCountAndBerserkCancelsRecovery()
        {
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            var start = _body.position; var combo = _ball.ComboCount;
            _ball.SetFrozen(true);
            for (var i = 0; i < 40; i++) Tick(false);
            Assert.AreEqual(start, _body.position); Assert.AreEqual(combo, _ball.ComboCount);
            Assert.AreEqual("Trapped", State);
            _ball.SetFrozen(false); Tick();
            _ball.SetBerserk(true);
            Assert.AreEqual("None", State);
        }

        [Test]
        public void TrappedTurnConsumesRemainingTravelInsteadOfPausingAtEndpoint()
        {
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            foreach (var view in _board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().enabled = false;
            var start = _body.position; var direction = _ball.CurrentDirection;
            for (var i = 0; i < 60; i++) Tick();
            // 1.2米：到第一个端点0.5米，再向回0.7米，最终位于中点反侧0.2米。
            Assert.That(Vector2.Distance(start - direction * 0.2f, _body.position), Is.LessThan(0.001f));
            Assert.AreEqual(2, _ball.ComboCount); Assert.AreEqual(2, _ball.BounceCount);
            Assert.AreEqual(1, _attacks);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OtherRobotCreatesEarlierEndpointWithoutBeingIgnoredOrAttacked(bool accelerate)
        {
            if (accelerate) ConfigureAcceleration(wall: false, robot: true);
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            var start = _body.position; var direction = _ball.CurrentDirection;
            RobotView blocker = null;
            foreach (var view in _board.GetComponentsInChildren<RobotView>())
            {
                var collider = view.GetPhysicalCollider();
                if (blocker == null && view != source && !Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), collider)) blocker = view;
                collider.enabled = false;
            }
            Assert.IsNotNull(blocker);
            blocker.transform.position = start + direction * 0.7f;
            blocker.GetPhysicalCollider().enabled = true; Physics2D.SyncTransforms();
            for (var i = 0; i < 20 && Vector2.Dot(_ball.CurrentDirection, direction) > 0f; i++) Tick();
            Assert.Less(Vector2.Dot(_ball.CurrentDirection, direction), 0f);
            Assert.Less(Vector2.Dot(_body.position - start, direction), 0.3f);
            Assert.AreEqual(2, _ball.ComboCount); Assert.AreEqual(1, _attacks);
            Assert.IsFalse(Physics2D.GetIgnoreCollision(_ball.GetComponent<Collider2D>(), blocker.GetPhysicalCollider()));
            if (accelerate) Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void EndpointBerserkNotificationUsesEndpointPositionAndCancelsRemainingTravel()
        {
            Set(_ball, "speed", 1.3f);
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            foreach (var view in _board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().enabled = false;
            typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(_ball, 49);
            var expected = _body.position + _ball.CurrentDirection * 0.5f;
            Vector2 reported = Vector2.zero;
            _ball.ComboThresholdReached += ball => { reported = _body.position; ball.SetBerserk(true); };
            for (var i = 0; i < 40 && !_ball.IsBerserk; i++) Tick();
            Assert.IsTrue(_ball.IsBerserk);
            Assert.That(Vector2.Distance(expected, reported), Is.LessThan(0.0005f));
            Assert.AreEqual("None", State);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GlassAtActualEndpointLosesDurabilityOnceUntilBallLeavesAndReturns(bool accelerate)
        {
            if (accelerate) ConfigureAcceleration(wall: true, robot: false);
            var tables = WaveTestData.SingleWaveTables();
            _glassContext = new BattleShopContext(new ShopRules(new Dictionary<int, double> { { 501, 1d }, { 502, 1d }, { 503, 1d } }),
                new[] { 501, 502, 503 }, new[] { 501, 502, 503 }, id => tables.TbRobot.Get(id));
            _glassHost = NewObject("GlassHost").AddComponent<BattleShopSceneController>();
            _glassHost.enabled = false;
            typeof(BattleShopSceneController).GetProperty("Context").SetValue(_glassHost, _glassContext);
            Set(_glassHost, "_ready", true); Set(_glassHost, "_running", true);
            _ball.BindBattle(_glassContext.BattleId, BattleSide.Player, () => _running);
            _ball.BindCombo(_registry, NewObject("GlassComboRoot").transform);
            _ball.LaunchAt(_board.GetCellWorldPosition(_origin), 45f);
            var source = FillNeighbors(); _ball.ResolvePlacementOverlap(source, _board);
            foreach (var view in _board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().enabled = false;
            var owner = NewObject("GlassOwner").AddComponent<GlassBoardController>();
            Set(owner, "_host", _glassHost); Set(owner, "_context", _glassContext);
            Set(owner, "_board", _board); Set(owner, "_side", BattleSide.Player); Set(owner, "_prepared", true);
            var config = tables.TbGlass.Get(1);
            var entity = _glassContext.Registry.CreateGlassFromConfig(config, BattleSide.Player, new BoardCoordinate(4,2), 0.01f);
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Glass/Prefabs/" + config.PrefabLocation + ".prefab"));
            _objects.Add(go); SceneManager.MoveGameObjectToScene(go, _scene);
            var glass = go.GetComponent<GlassView>(); glass.Bind(owner, entity, config, 0.2f);
            go.transform.position = _body.position + _ball.CurrentDirection * 0.66f;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 40 && entity.CurrentDurability == entity.MaxDurability; i++)
            { _glassContext.Registry.AdvanceTime(Time.fixedDeltaTime); Tick(); }
            Assert.AreEqual(entity.MaxDurability - 1, entity.CurrentDurability);
            if (accelerate) Assert.That(_ball.EffectiveSpeed, Is.EqualTo(5f).Within(0.0001f));
            Assert.AreEqual(2, _ball.ComboCount, "玻璃挡住路线形成实际端点，只计一次。");
            for (var i = 0; i < 3; i++) { _glassContext.Registry.AdvanceTime(Time.fixedDeltaTime); Tick(); }
            Assert.AreEqual(entity.MaxDurability - 1, entity.CurrentDurability, "尚未返回不能再次扣耐久。");
            for (var i = 0; i < 130 && entity.CurrentDurability == entity.MaxDurability - 1; i++)
            { _glassContext.Registry.AdvanceTime(Time.fixedDeltaTime); Tick(); }
            Assert.AreEqual(entity.MaxDurability - 2, entity.CurrentDurability);
            Assert.AreEqual(1, _attacks);
        }

        [Test]
        public void TargetSearchStopsAtEightNeighborsAndUsesCommittedMultiCellOccupancy()
        {
            var recovery = new BallPlacementRecovery();
            recovery.Begin(_board, _body.position);
            var wide = new PlaceableDefinition("placed-wide", 2, 2);
            Assert.IsTrue(_board.Model.TryPlace(_origin, wide));
            for (var y = 1; y <= 3; y++) for (var x = 1; x <= 3; x++)
            {
                var cell = new BoardCoordinate(x, y);
                if (!_board.Model.IsOccupied(cell)) _board.Model.TryPlace(cell, new PlaceableDefinition(x + ":" + y, 1, 1));
            }
            Assert.IsFalse(recovery.TrySelectTarget(_body.position, p => true), "远处空格不能替代周围八格。");
            _board.Model.Release("1:2");
            Assert.IsTrue(recovery.TrySelectTarget(_body.position, p => true));
            Assert.AreEqual(new BoardCoordinate(1, 2), recovery.TargetCell);
        }

        [TestCase(0, 0f, -0.5f)]
        [TestCase(1, 0.5f, 0f)]
        [TestCase(2, 0f, 0.5f)]
        [TestCase(3, -0.5f, 0f)]
        public void AllFourTrappedEdgesUseActualMidpointAndKeepOriginCell(int edge, float x, float y)
        {
            var recovery = new BallPlacementRecovery(); recovery.Begin(_board, _body.position);
            var midpoint = recovery.EnterTrapped(edge, true);
            var local = _board.transform.InverseTransformPoint(midpoint);
            Assert.That(local.x, Is.EqualTo(x).Within(0.0001f));
            Assert.That(local.y, Is.EqualTo(y).Within(0.0001f));
            Assert.AreEqual(_origin, recovery.OriginCell);
        }

        private string State
        {
            get
            {
                var property = typeof(PhysicalCollisionBall).GetProperty("RecoveryState");
                Assert.IsNotNull(property, "能源球需要可观察的 RecoveryState。");
                return property.GetValue(_ball).ToString();
            }
        }

        private RobotView FillNeighbors()
        {
            RobotView source = null;
            for (var y = 1; y <= 3; y++) for (var x = 1; x <= 3; x++)
            {
                var view = Robot(new BoardCoordinate(x, y));
                if (x == 2 && y == 2) source = view;
            }
            return source;
        }

        private RobotView Robot(BoardCoordinate cell)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/Actor/Robots/Prefabs/RobotCannon.prefab");
            var go = UnityEngine.Object.Instantiate(prefab); _objects.Add(go);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.SetParent(_board.transform, false); go.transform.position = _board.GetCellWorldPosition(cell);
            var entity = _registry.CreateRobot(501, _ball.Side, 1, 1);
            entity.TrySetLocation(RobotLocation.Board, cell);
            Assert.IsTrue(_board.Model.TryPlace(cell, new PlaceableDefinition(entity.InstanceId.ToString(), 1, 1)));
            var view = go.GetComponent<RobotView>(); view.Bind(_registry, entity.InstanceId);
            var polygon = view.GetPhysicalCollider(); polygon.pathCount = 1;
            polygon.SetPath(0, new[] { new Vector2(-0.4f,-0.4f), new Vector2(0.4f,-0.4f),
                new Vector2(0.4f,0.4f), new Vector2(-0.4f,0.4f) });
            polygon.enabled = true;
            Physics2D.SyncTransforms();
            return view;
        }

        private void Tick(bool moving = true)
        {
            Invoke(_ball, "FixedUpdate");
            if (moving) Assert.That(_body.velocity.magnitude, Is.EqualTo(_ball.EffectiveSpeed).Within(0.0001f));
            _scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            Physics2D.SyncTransforms();
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, _scene); _objects.Add(go); return go;
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    public sealed class BallPlacementRecoveryPlayerLoopTests
    {
        private BallPlacementRecoveryTests _fixture;

        [UnitySetUp]
        public IEnumerator Enter()
        {
            yield return new EnterPlayMode();
            _fixture = new BallPlacementRecoveryTests();
            _fixture.SetUp();
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        }

        [UnityTearDown]
        public IEnumerator Exit()
        {
            _fixture?.TearDown();
            _fixture = null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator RealCollisionCallbacksAccelerateAndDecayForWallAndRobot()
        {
            var path = System.IO.Path.Combine(Application.dataPath, "../Temp/BallBounceAccelerationPlayerLoop.json");
            System.IO.File.WriteAllText(path, "{\"passed\":false,\"status\":\"running\"}");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(BallPlacementRecoveryTests);
            int samples = 0;
            for (int scenario = 0; scenario < 6; scenario++)
            {
                bool robot = scenario >= 2;
                bool enabled = scenario % 2 == 1 || scenario >= 4;
                bool rageOnBounce = scenario == 4;
                bool triggerOnly = scenario == 5;
                _fixture.TearDown(); _fixture.SetUp();
                Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
                type.GetMethod("BeginAccelerationCollision", flags).Invoke(_fixture, new object[] { robot, enabled });
                var ball = (PhysicalCollisionBall)type.GetField("_ball", flags).GetValue(_fixture);
                var body = ball.GetComponent<Rigidbody2D>();
                if (rageOnBounce || triggerOnly)
                {
                    type.GetMethod("ConfigureTestSpeeds", flags).Invoke(_fixture, null);
                    var board = (PlacementBoardView)type.GetField("_board", flags).GetValue(_fixture);
                    ball.LaunchAt(board.GetCellWorldPosition(new BoardCoordinate(2, 2)), 0f);
                    if (rageOnBounce)
                    {
                        typeof(PhysicalCollisionBall).GetProperty("ComboCount").SetValue(ball, 49);
                        ball.ComboThresholdReached += current => current.SetBerserk(true);
                    }
                    else
                    {
                        ball.SetBerserk(true);
                        foreach (var view in board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().isTrigger = true;
                        var until = Time.time + 0.3f;
                        while (Time.time < until) { yield return null; Assert.That(body.velocity.magnitude, Is.EqualTo(6f).Within(0.0001f)); samples++; }
                        Assert.AreEqual(0, ball.BounceCount, "暴走穿过机器人的触发回调不能施加加速。");
                        continue;
                    }
                }
                var deadline = Time.time + 1f;
                while (ball.BounceCount == 0 && Time.time < deadline) yield return null;
                Assert.AreEqual(1, ball.BounceCount, "需要实际OnCollisionEnter2D反弹，不能手动调用效果方法替代。");
                float baseSpeed = rageOnBounce ? 6f : 4f;
                if (rageOnBounce) Assert.IsTrue(ball.IsBerserk);
                Assert.That(body.velocity.magnitude, Is.EqualTo(baseSpeed + (enabled ? 1f : 0f)).Within(0.045f));
                float collisionTime = Time.fixedTime;
                deadline = Time.time + 0.6f;
                while (Time.time < deadline)
                {
                    yield return null;
                    float expected = baseSpeed + (enabled ? Mathf.Max(0f, 1f - (Time.fixedTime - collisionTime) / 0.5f) : 0f);
                    Assert.That(body.velocity.magnitude, Is.EqualTo(expected).Within(0.045f));
                    Assert.AreEqual(1, ball.BounceCount, "离开接触后不能因Stay回调反复刷新。");
                    samples++;
                }
                Assert.That(body.velocity.magnitude, Is.EqualTo(baseSpeed).Within(0.0001f));
            }
            System.IO.File.WriteAllText(path, "{\"passed\":true,\"scenarios\":6,\"samples\":" + samples + "}");
        }

        [UnityTest]
        public IEnumerator RealFixedLoopPausedOpponentLayoutResumesMovement()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(BallPlacementRecoveryTests);
            var ball = (PhysicalCollisionBall)type.GetField("_ball", flags).GetValue(_fixture);
            type.GetMethod("BeginPausedOpponentLayout", flags).Invoke(_fixture, null);
            var body = ball.GetComponent<Rigidbody2D>();
            var start = body.position;
            var resultPath = System.IO.Path.Combine(Application.dataPath, "../Temp/PausedOpponentRecoveryPlayerLoop.json");
            System.IO.File.WriteAllText(resultPath, "{\"passed\":false,\"status\":\"running\"}");
            var samples = 0;
            var maxDistance = 0f;
            var until = Time.time + 0.8f;
            while (Time.time < until)
            {
                yield return null;
                Assert.That(body.velocity.magnitude, Is.EqualTo(4f).Within(0.001f));
                maxDistance = Mathf.Max(maxDistance, Vector2.Distance(start, body.position));
                samples++;
            }
            Assert.AreEqual(PlacementRecoveryState.None, ball.RecoveryState);
            Assert.Greater(maxDistance, 0.1f, "实际球心必须持续移动，不能只有速度字段非零。");
            Assert.Greater(ball.BounceCount, 0, "恢复后继续通过普通碰撞反弹。");
            System.IO.File.WriteAllText(resultPath, "{\"passed\":true,\"samples\":" + samples + ",\"bounces\":" + ball.BounceCount + "}");
        }

        [UnityTest]
        public IEnumerator RealFixedLoopThreeCellEscapeResumesNormalBounces()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(BallPlacementRecoveryTests);
            var ball = (PhysicalCollisionBall)type.GetField("_ball", flags).GetValue(_fixture);
            type.GetMethod("BeginThreeCellEscape", flags).Invoke(_fixture, null);
            var body = ball.GetComponent<Rigidbody2D>();
            var resultPath = System.IO.Path.Combine(Application.dataPath, "../Temp/BallThreeCellEscapePlayerLoop.json");
            System.IO.File.WriteAllText(resultPath, "{\"passed\":false,\"status\":\"running\"}");
            var samples = 0;
            var until = Time.time + 0.8f;
            while (Time.time < until)
            {
                yield return null;
                Assert.That(body.velocity.magnitude, Is.EqualTo(6f).Within(0.001f));
                Assert.That(ball.CurrentDirection.magnitude, Is.EqualTo(1f).Within(0.0001f));
                samples++;
            }
            Assert.AreEqual(PlacementRecoveryState.None, ball.RecoveryState);
            Assert.Greater(ball.BounceCount, 1, "脱离后必须通过普通物理碰撞继续反弹。");
            System.IO.File.WriteAllText(resultPath, "{\"passed\":true,\"samples\":" + samples + ",\"bounces\":" + ball.BounceCount + "}");
        }

        [UnityTest]
        public IEnumerator RealFixedLoopKeepsSpeedAndEdgeAfterTurnsAndPausesWhenFrozen()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(BallPlacementRecoveryTests);
            var ball = (PhysicalCollisionBall)type.GetField("_ball", flags).GetValue(_fixture);
            var board = (PlacementBoardView)type.GetField("_board", flags).GetValue(_fixture);
            var source = (RobotView)type.GetMethod("FillNeighbors", flags).Invoke(_fixture, null);
            ball.ResolvePlacementOverlap(source, board);
            foreach (var view in board.GetComponentsInChildren<RobotView>()) view.GetPhysicalCollider().enabled = false;
            var body = ball.GetComponent<Rigidbody2D>();
            var midpoint = body.position;
            var tangent = ball.CurrentDirection;
            var resultPath = System.IO.Path.Combine(Application.dataPath, "../Temp/BallPlacementRecoveryPlayerLoop.json");
            System.IO.File.WriteAllText(resultPath, "{\"passed\":false,\"status\":\"running\"}");
            var samples = 0;
            var until = Time.time + 1.8f;
            while (Time.time < until)
            {
                yield return null;
                Assert.That(body.velocity.magnitude, Is.EqualTo(ball.EffectiveSpeed).Within(0.0001f));
                var delta = body.position - midpoint;
                Assert.That(Mathf.Abs(delta.x * tangent.y - delta.y * tangent.x), Is.LessThan(0.001f));
                Assert.That(Mathf.Abs(Vector2.Dot(delta, tangent)), Is.LessThanOrEqualTo(0.501f));
                samples++;
            }
            Assert.GreaterOrEqual(ball.ComboCount, 3);
            ball.SetFrozen(true);
            var position = body.position; var count = ball.ComboCount;
            until = Time.time + 0.1f;
            while (Time.time < until) yield return null;
            Assert.AreEqual(position, body.position); Assert.AreEqual(count, ball.ComboCount);
            System.IO.File.WriteAllText(resultPath, "{\"passed\":true,\"samples\":" + samples + ",\"freezeChecked\":true}");
        }
    }
}
