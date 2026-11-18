using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    public sealed class BattlePreparationTests
    {
        private EnemyBattleControllerTests.Fixture _f;
        private Scene _scene;
        private SimulationMode2D _simulationMode;
        private Random.State _randomState;
        private PlacementBoardView _playerBoard;
        private PlacementBoardView _opponentBoard;
        private PhysicalCollisionBall _playerBall;
        private PhysicalCollisionBall _opponentBall;

        [SetUp]
        public void SetUp()
        {
            _simulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            _randomState = Random.state;
            _scene = Application.isPlaying
                ? SceneManager.CreateScene("BattlePreparation_" + System.Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics2D))
                : EditorSceneManager.NewPreviewScene();
            _f = new EnemyBattleControllerTests.Fixture();
            SceneManager.MoveGameObjectToScene(_f.Root, _scene);
            // 移入独立物理场景会触发运行态 OnDisable，恢复夹具中的塔实体绑定。
            Get<TowerView>(_f.Host, "_playerTowerView").Bind(_f.Host.Towers.PlayerTower);
            Get<TowerView>(_f.Host, "_opponentTowerView").Bind(_f.Host.Towers.OpponentTower);
            _f.Host.enabled = false;
            _f.Prepare();
            Set(_f.Host, "_running", false);
            Set(_f.Host, "_preparationRemaining", 3f);
            _playerBoard = Board("PlayerBoard", new Vector3(100f, 100f));
            _opponentBoard = Board("OpponentBoard", new Vector3(110f, 100f));
            _playerBall = Ball();
            _opponentBall = Ball();
            var bootstrap = _f.Host.Bootstrap;
            Set(bootstrap, "_ball", _playerBall);
            Set(bootstrap, "_opponentBall", _opponentBall);
            Set(_f.Host, "_playerBoard", _playerBoard);
            Set(_f.Host, "_opponentBoard", _opponentBoard);
            bootstrap.ConfigureFromTables(_f.Tables);
            bootstrap.BindBattle(_f.Context.BattleId, () => _f.Host.IsBallSimulationRunning,
                _f.Context.Registry, Child("ComboRoot"));
        }

        [TearDown]
        public void TearDown()
        {
            _f?.Dispose();
            _f = null;
            if (_scene.IsValid())
            {
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(_scene);
                else EditorSceneManager.ClosePreviewScene(_scene);
            }
            Physics2D.simulationMode = _simulationMode;
            Random.state = _randomState;
        }

        public void CountdownBouncesWithoutGlassDamageThenEnablesDamageWithoutRelaunch(BattleSide side, int glassId)
        {
            _f.Host.AdvanceBattle(0f);
            Assert.IsTrue(_playerBall.IsLaunched);
            Assert.IsTrue(_opponentBall.IsLaunched);
            Assert.AreEqual(3f, _f.Host.PreparationRemaining);
            Assert.IsFalse(_f.Host.IsCombatRunning);
            Assert.AreEqual(0, _f.Spawned(BattleSide.Player));
            Assert.AreEqual(0, _f.Spawned(BattleSide.Opponent));
            var board = side == BattleSide.Player ? _playerBoard : _opponentBoard;
            var ball = side == BattleSide.Player ? _playerBall : _opponentBall;
            var owner = Child("GlassOwner").gameObject.AddComponent<GlassBoardController>();
            Set(owner, "_host", _f.Host);
            Set(owner, "_context", _f.Context);
            Set(owner, "_board", board);
            Set(owner, "_side", side);
            Set(owner, "_prepared", true);
            var config = _f.Tables.TbGlass.Get(glassId);
            var entity = _f.Context.Registry.CreateGlassFromConfig(config, side, new BoardCoordinate(2, 2), 0.15f);
            var glass = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/glass/" + config.PrefabLocation + ".prefab"), _f.Root.transform).GetComponent<GlassView>();
            glass.Bind(owner, entity, config, 0.2f);
            glass.transform.position = board.transform.position + Vector3.right * 0.4f;
            ball.LaunchAt(board.transform.position, 0f);
            var stage = glass.StageIndex;
            Assert.IsTrue(owner.CanCollide);
            Assert.AreEqual(GlassCollisionResult.Ignored, glass.Collide(ball));
            SimulateUntilBounce(ball);
            Assert.Less(ball.CurrentDirection.x, 0f, "倒计时内必须真实反弹，不能仅依赖物理引擎推出。");
            Assert.AreEqual(entity.MaxDurability, entity.CurrentDurability);
            Assert.AreEqual(stage, glass.StageIndex);
            Assert.IsFalse(glass.IsBroken);
            Assert.Greater(ball.GetComponent<BallImpactFeedback>().ActiveBurstCount, 0,
                "保护耐久期间保留玻璃碰撞特效。");
            var position = ball.transform.position;
            var direction = ball.CurrentDirection;
            var bounces = ball.BounceCount;
            _f.Host.AdvanceBattle(3f);
            Assert.IsTrue(_f.Host.IsCombatRunning);
            Assert.AreEqual(0f, _f.Host.PreparationRemaining);
            Assert.AreEqual(position, ball.transform.position, "倒计时结束不能重置弹珠位置。");
            Assert.AreEqual(direction, ball.CurrentDirection);
            Assert.AreEqual(bounces, ball.BounceCount);
            Assert.Greater(_f.Spawned(BattleSide.Player), 0);
            Assert.Greater(_f.Spawned(BattleSide.Opponent), 0);
            ball.LaunchAt(board.transform.position, 0f);
            SimulateUntilBounce(ball);
            Assert.AreEqual(entity.MaxDurability - 1, entity.CurrentDurability,
                "结束后第一次真实碰撞正常扣耐久，准备期不消耗受击间隔。");
        }

        [Test]
        public void MatchReleasePauseAndBattleEndControlBothPreparationBalls()
        {
            Set(_f.Host, "_matchReleased", false);
            _f.Host.AdvanceBattle(1f);
            Assert.IsFalse(_playerBall.IsLaunched);
            Assert.IsFalse(_opponentBall.IsLaunched);
            Assert.IsFalse(_f.Host.IsBallSimulationRunning);
            Assert.AreEqual(3f, _f.Host.PreparationRemaining);
            Set(_f.Host, "_matchReleased", true);
            _f.Host.AdvanceBattle(0f);
            Assert.IsTrue(_f.Host.IsBallSimulationRunning);
            var start = _playerBall.transform.position;
            Tick();
            Assert.Greater(Vector3.Distance(start, _playerBall.transform.position), 0f);
            _f.Host.SetFrozen(true);
            var paused = _playerBall.transform.position;
            _f.Host.AdvanceBattle(5f);
            Tick();
            Assert.AreEqual(paused, _playerBall.transform.position);
            Assert.AreEqual(3f, _f.Host.PreparationRemaining);
            Assert.IsFalse(_f.Host.IsBallSimulationRunning);
            _f.Host.SetFrozen(false);
            _f.Host.AdvanceBattle(1f);
            Assert.AreEqual(2f, _f.Host.PreparationRemaining);
            Assert.IsTrue(_playerBall.IsLaunched);
            Assert.IsTrue(_opponentBall.IsLaunched);
            Assert.IsFalse(_f.Host.IsCombatRunning);
            _f.Context.EndBattle();
            _f.Host.AdvanceBattle(0f);
            Assert.IsFalse(_playerBall.IsLaunched);
            Assert.IsFalse(_opponentBall.IsLaunched);
            Assert.IsFalse(_f.Host.IsBallSimulationRunning);
        }

        private PlacementBoardView Board(string name, Vector3 position)
        {
            var board = Child(name).gameObject.AddComponent<PlacementBoardView>();
            board.transform.position = position;
            return board;
        }

        private PhysicalCollisionBall Ball()
        {
            var ball = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/Actor/Prefabs/PhysicsPrefabs/PhysicalCollisionBall.prefab"), _f.Root.transform)
                .GetComponent<PhysicalCollisionBall>();
            if (ball.GetComponent<BallImpactFeedback>() == null) Invoke(ball, "Awake");
            return ball;
        }

        private Transform Child(string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(_f.Root.transform, false);
            return child;
        }

        private void SimulateUntilBounce(PhysicalCollisionBall ball)
        {
            var before = ball.BounceCount;
            for (var i = 0; i < 100 && ball.BounceCount == before; i++) Tick();
            Assert.Greater(ball.BounceCount, before, "需要实际玻璃物理碰撞。");
        }

        private void Tick()
        {
            Invoke(_playerBall, "FixedUpdate");
            Invoke(_opponentBall, "FixedUpdate");
            Physics2D.SyncTransforms();
            _scene.GetPhysicsScene2D().Simulate(Time.fixedDeltaTime);
            Physics2D.SyncTransforms();
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static T Get<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Invoke(object target, string name) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    public sealed class BattlePreparationPlayerLoopTests
    {
        [UnitySetUp]
        public IEnumerator Enter() { yield return new EnterPlayMode(); }

        [UnityTearDown]
        public IEnumerator Exit() { yield return new ExitPlayMode(); }

        [UnityTest]
        public IEnumerator BothBoardsBounceOffBothGlassTypesBeforeCountdownEndsAndOnlyDamageAfterwards()
        {
            foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
            foreach (var glassId in new[] { 1, 2 })
            {
                var fixture = new BattlePreparationTests();
                try
                {
                    fixture.SetUp();
                    fixture.CountdownBouncesWithoutGlassDamageThenEnablesDamageWithoutRelaunch(side, glassId);
                }
                finally { fixture.TearDown(); }
                yield return null;
            }
        }
    }
}
