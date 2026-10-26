using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class OpponentAiSceneTests
    {
        private GameObject _root;
        private BattleShopSceneController _host;
        private PlacementBoardView _board;
        private PlacementController _placement;
        private BattleShopContext _context;
        private Texture2D _texture;
        private Sprite _sprite;
        private UniTaskCompletionSource<GameObject> _load;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("OpponentAiTest");
            _host = _root.AddComponent<BattleShopSceneController>();
            _host.enabled = false;
            var boardObject = new GameObject("Board");
            boardObject.transform.SetParent(_root.transform);
            _board = boardObject.AddComponent<PlacementBoardView>();
            var slot = new GameObject("SlotTemplate");
            slot.transform.SetParent(_root.transform);
            var renderer = slot.AddComponent<SpriteRenderer>();
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
            renderer.sprite = _sprite;
            Set(_board, "_slotPrefab", renderer);
            Assert.IsTrue(_board.Initialize());
            _context = ShopTestData.Context();
            SetContext(_context);
            Set(_host, "_ready", true);
            Set(_host, "_opponentBoard", _board);
            Set(_host, "_rules", _context.Rules);
            var playerObject = new GameObject("PlayerBoard");
            playerObject.transform.SetParent(_root.transform);
            var playerBoard = playerObject.AddComponent<PlacementBoardView>();
            Set(playerBoard, "_slotPrefab", renderer);
            _placement = _root.AddComponent<PlacementController>();
            var cameraObject = new GameObject("SceneCamera");
            cameraObject.transform.SetParent(_root.transform);
            Set(_placement, "_playerBoard", playerBoard);
            Set(_placement, "_enemyBoard", _board);
            Set(_placement, "_sceneCamera", cameraObject.AddComponent<Camera>());
            Assert.IsTrue(_placement.Initialize());
            Set(_host, "_placement", _placement);
            // 原有用例在交互已经开放的阶段验证异步事务。
            Set(_host, "_interactionOpened", true);
            _load = new UniTaskCompletionSource<GameObject>();
            Func<string, Transform, CancellationToken, UniTask<GameObject>> loader = (name, parent, token) => _load.Task;
            typeof(BattleShopSceneController).GetProperty("OpponentViewLoader", Private).SetValue(_host, loader);
        }

        [TearDown]
        public void TearDown()
        {
            SetContext(null);
            ((Dictionary<int, GameObject>)typeof(BattleShopSceneController).GetField("_views", Private).GetValue(_host)).Clear();
            _context.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_sprite);
            UnityEngine.Object.DestroyImmediate(_texture);
        }

        [Test]
        public void ReadyBeforeInteractionOpeningCannotLoadPurchaseMoveOrRefresh()
        {
            Set(_host, "_interactionOpened", false);
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            var offer = _context.GetState(BattleSide.Opponent).Slots[0].Offer;
            var loads = 0;
            Func<string, Transform, CancellationToken, UniTask<GameObject>> loader = (name, parent, token) =>
            { loads++; return UniTask.FromResult(View()); };
            typeof(BattleShopSceneController).GetProperty("OpponentViewLoader", Private).SetValue(_host, loader);
            Assert.IsTrue(_host.IsReady);
            Assert.IsFalse(_host.CanInteractWithRobots);
            Assert.AreEqual(ShopOperationResult.Busy, Execute(Plan()).GetAwaiter().GetResult());
            Assert.AreEqual(ShopOperationResult.Busy, Execute(new OpponentAiAction(OpponentAiActionKind.Move,
                sourceInstanceId: 999, battleId: _context.BattleId)).GetAwaiter().GetResult());
            Assert.AreEqual(ShopOperationResult.Busy, Execute(new OpponentAiAction(OpponentAiActionKind.Refresh,
                battleId: _context.BattleId)).GetAwaiter().GetResult());
            Assert.AreEqual(0, loads);
            Assert.AreEqual(coins, _context.GetState(BattleSide.Opponent).EnergyCoins);
            Assert.AreSame(offer, _context.GetState(BattleSide.Opponent).Slots[0].Offer);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void OpeningRequiresCurrentBattleAndVisibleRunningCombatUi()
        {
            Set(_host, "_interactionOpened", false);
            Assert.IsFalse(_host.TryOpenRobotInteraction(_context.BattleId + 1));
            Assert.IsFalse(_host.TryOpenRobotInteraction(_context.BattleId));
            Assert.IsFalse(_host.CanInteractWithRobots);
        }

        [Test]
        public void ClosingInteractionDuringLoadCannotCommitOrConsumeFunds()
        {
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            var task = Execute(Plan());
            Set(_host, "_interactionOpened", false);
            var prepared = View();
            _load.TrySetResult(prepared);
            Assert.AreEqual(ShopOperationResult.Busy, task.GetAwaiter().GetResult());
            Assert.IsTrue(prepared == null);
            Assert.AreEqual(coins, _context.GetState(BattleSide.Opponent).EnergyCoins);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void SharedInteractionGateRequiresPlacementAndFreezesOnPause()
        {
            Assert.IsTrue(_host.CanInteractWithRobots);
            Set(_host, "_frozen", true);
            Assert.IsFalse(_host.CanInteractWithRobots);
            Set(_host, "_frozen", false);
            Assert.IsTrue(_host.CanInteractWithRobots);
            _placement.SuspendPlacement();
            Assert.IsFalse(_host.CanInteractWithRobots);
            Assert.AreEqual(ShopOperationResult.Busy, Execute(Plan()).GetAwaiter().GetResult());
        }

        [Test]
        public void SchedulerDoesNotCountCoveredPreparationTimeAndRetainsDelayAfterPause()
        {
            var calls = 0;
            var ai = new OpponentAiController(new OpponentAiSettings(),
                () => _context.CreateOpponentAiSnapshot(_board.Model),
                (action, token) => { calls++; return UniTask.FromResult(ShopOperationResult.Success); });
            Set(_host, "_opponentAi", ai);
            try
            {
                Set(_host, "_interactionOpened", false);
                AdvanceAi(10f);
                Assert.AreEqual(0, calls);
                Set(_host, "_interactionOpened", true);
                AdvanceAi(0.5f);
                Assert.AreEqual(0, calls);
                Set(_host, "_frozen", true);
                AdvanceAi(100f);
                Assert.AreEqual(0, calls);
                Set(_host, "_frozen", false);
                AdvanceAi(0.29f);
                Assert.AreEqual(0, calls);
                AdvanceAi(0.02f);
                Assert.AreEqual(1, calls);
                AdvanceAi(0f);
                Assert.AreEqual(1, calls, "交互开放不能补跑加载期动作。");
            }
            finally { ai.Dispose(); Set(_host, "_opponentAi", null); }
        }

        private void AdvanceAi(float deltaTime) => typeof(BattleShopSceneController)
            .GetMethod("AdvanceOpponentAi", Private).Invoke(_host, new object[] { deltaTime });

        [Test]
        public void SlowLoadLeavesSharedInteractionAvailableAndNeverCancelsPlayersDrag()
        {
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            var task = Execute(Plan());
            Assert.IsFalse(_context.IsBusy);
            var playerOffer = _context.GetState(BattleSide.Player).Slots[0].Offer;
            RobotDragSession player;
            Assert.AreEqual(ShopOperationResult.Success, _context.TryBeginDrag(BattleSide.Player,
                RobotDragSource.Offer(0, playerOffer.OfferId), out player));
            var prepared = View();
            _load.TrySetResult(prepared);
            Assert.AreEqual(ShopOperationResult.Busy, task.GetAwaiter().GetResult());
            Assert.IsTrue(prepared == null, "失败的预加载视图必须释放");
            Assert.AreSame(player, _context.ActiveInteraction);
            Assert.AreEqual(coins, _context.GetState(BattleSide.Opponent).EnergyCoins);
            _context.CancelInteraction(player.InteractionId);
        }

        [Test]
        public void PauseDuringLoadPreventsLatePurchase()
        {
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            var task = Execute(Plan());
            Set(_host, "_frozen", true);
            _load.TrySetResult(View());
            Assert.AreEqual(ShopOperationResult.Busy, task.GetAwaiter().GetResult());
            Assert.AreEqual(coins, _context.GetState(BattleSide.Opponent).EnergyCoins);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void BattleReplacementDuringLoadCannotSubmitIntoNewBattle()
        {
            var task = Execute(Plan());
            using (var next = ShopTestData.Context())
            {
                SetContext(next);
                var coins = next.GetState(BattleSide.Opponent).EnergyCoins;
                _load.TrySetResult(View());
                Assert.AreEqual(ShopOperationResult.BattleEnded, task.GetAwaiter().GetResult());
                Assert.AreEqual(coins, next.GetState(BattleSide.Opponent).EnergyCoins);
                Assert.IsFalse(next.IsBusy);
            }
        }

        [Test]
        public void StaleOfferAfterLoadDoesNotConsumeMoneyOrBeginInteraction()
        {
            var task = Execute(Plan());
            Assert.AreEqual(ShopOperationResult.Success, _context.TryRefresh(BattleSide.Opponent));
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            _load.TrySetResult(View());
            Assert.AreEqual(ShopOperationResult.StaleOffer, task.GetAwaiter().GetResult());
            Assert.AreEqual(coins, _context.GetState(BattleSide.Opponent).EnergyCoins);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void PreparedViewDeploysSameEntityAndMoveReusesViewWithoutResourceIo()
        {
            var action = Plan();
            var price = _context.GetState(BattleSide.Opponent).Slots[action.SlotId].Offer.Price;
            var coins = _context.GetState(BattleSide.Opponent).EnergyCoins;
            var task = Execute(action);
            var instance = View();
            _load.TrySetResult(instance);
            Assert.AreEqual(ShopOperationResult.Success, task.GetAwaiter().GetResult());
            var view = instance.GetComponent<RobotView>();
            Entity entity;
            Assert.IsTrue(_context.Registry.TryGet(view.InstanceId, out entity));
            var robot = (RobotEntity)entity;
            Assert.AreEqual(BattleSide.Opponent, robot.Side);
            Assert.AreEqual(action.Anchor, robot.Anchor);
            Assert.AreEqual(coins - price, _context.GetState(BattleSide.Opponent).EnergyCoins);
            Assert.AreEqual(robot.Definition.Id, _board.Model.GetOccupant(action.Anchor));
            Assert.IsTrue(instance.GetComponentInChildren<Collider2D>().enabled);
            Assert.IsFalse(_context.IsBusy);
            var destination = new BoardCoordinate(1, 1);
            var move = new OpponentAiAction(OpponentAiActionKind.Move, sourceInstanceId: robot.InstanceId,
                anchor: destination, battleId: _context.BattleId);
            Assert.AreEqual(ShopOperationResult.Success, Execute(move).GetAwaiter().GetResult());
            Assert.AreEqual(destination, robot.Anchor);
            Assert.AreEqual(robot.InstanceId, view.InstanceId);
            Assert.AreEqual(coins - price, _context.GetState(BattleSide.Opponent).EnergyCoins);
        }

        [Test]
        public void CoinChangeDuringLoadIsRecheckedBeforeDragBegins()
        {
            var task = Execute(Plan());
            typeof(BattleShopState).GetProperty("EnergyCoins").SetValue(_context.GetState(BattleSide.Opponent), 0);
            _load.TrySetResult(View());
            Assert.AreEqual(ShopOperationResult.InsufficientCoins, task.GetAwaiter().GetResult());
            Assert.IsFalse(_context.IsBusy);
        }

        private OpponentAiAction Plan() => OpponentAiPlanner.Plan(_context.CreateOpponentAiSnapshot(_board.Model));
        private UniTask<ShopOperationResult> Execute(OpponentAiAction action)
            => (UniTask<ShopOperationResult>)typeof(BattleShopSceneController).GetMethod("ExecuteOpponentAiActionAsync", Private)
                .Invoke(_host, new object[] { action, CancellationToken.None });
        private void SetContext(BattleShopContext context)
            => typeof(BattleShopSceneController).GetProperty("Context").SetValue(_host, context);
        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, Private).SetValue(target, value);
        private GameObject View()
        {
            var instance = new GameObject("PreparedRobot");
            instance.transform.SetParent(_root.transform);
            var view = instance.AddComponent<RobotView>();
            Set(view, "_configId", 501);
            var cell = new GameObject("Cell_0_0");
            cell.transform.SetParent(instance.transform);
            cell.AddComponent<SpriteRenderer>();
            cell.AddComponent<BoxCollider2D>();
            return instance;
        }
    }
}
