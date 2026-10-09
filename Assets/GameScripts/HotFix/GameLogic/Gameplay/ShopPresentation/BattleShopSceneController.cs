using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>单局装配入口：双方共用纯逻辑上下文，只有玩家打开 UIWindow。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleShopSceneController : MonoBehaviour
    {
        [SerializeField] private PlacementController _placement;
        [SerializeField] private PlacementBoardView _playerBoard;
        [SerializeField] private PlacementBoardView _opponentBoard;
        [SerializeField] private PlacementBoardsBootstrap _bootstrap;
        [SerializeField] private int _layoutConfigId = 1;
        [SerializeField] private int[] _playerCandidates = { 1001, 1002, 1003 };
        [SerializeField] private int[] _opponentCandidates = { 1001, 1002, 1003 };
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private Transform _preparedRoot;
        private bool _initializing;
        private int _initializationGeneration;
        private CancellationTokenSource _initializationCancellation;
        private ShopRules _rules;
        private BattleRewardAdapter _rewards;
        private bool _listening;
        private bool _ready;
        private bool _running;
        private bool _frozen;
        private float _preparationRemaining;
        private GlassBoardController _playerGlass;
        private GlassBoardController _opponentGlass;
        public BattleShopContext Context { get; private set; }
        public PlacementController Placement => _placement;
        public int RefreshPrice => _rules.RefreshPrice;
        public bool IsReady => _ready && Context != null && !Context.IsEnded;
        public bool IsCombatRunning => IsReady && _running && !_frozen;
        public bool IsFrozen => _frozen;
        public float PreparationRemaining => _preparationRemaining;
        public PlacementBoardsBootstrap Bootstrap => _bootstrap;
        public GlassBoardController PlayerGlass => _playerGlass;
        public GlassBoardController OpponentGlass => _opponentGlass;

        private void Start() { InitializeAsync().Forget(); }

        public async UniTask InitializeAsync()
        {
            if (this != null && Context != null && Context.IsEnded) EndBattle();
            if (this == null || Context != null || _initializing) return;
            _initializing = true;
            var generation = ++_initializationGeneration;
            _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            var cancellationToken = _initializationCancellation.Token;
            try
            {
                var tables = ConfigSystem.Instance.Tables;
                if (_placement == null || _playerBoard == null || _opponentBoard == null || _bootstrap == null)
                    throw new InvalidOperationException("商店缺少双方盘面或统一启动器引用。");
                _bootstrap.StopBalls();
                var layout = new BoardLayoutRules(tables.TbBoardLayout.Get(_layoutConfigId));
                if (!_playerBoard.ResetForBattle(layout) || !_opponentBoard.ResetForBattle(layout) || !_bootstrap.Initialize())
                    throw new InvalidOperationException("双方棋盘初始化失败。");
                _rules = ShopRules.FromTables(tables);
                foreach (var id in new[] { layout.NormalGlassId, layout.ColoredGlassId })
                {
                    var glass = tables.TbGlass.Get(id);
                    if (glass.Kind != (id == layout.NormalGlassId ? 0 : 1) ||
                        !GameModule.Resource.CheckLocationValid(glass.PrefabLocation))
                        throw new InvalidOperationException("玻璃种类或Prefab资源无效：" + id);
                    foreach (var sprite in glass.StageSprites)
                        if (!GameModule.Resource.CheckLocationValid(sprite))
                            throw new InvalidOperationException("玻璃图片资源未收集：" + sprite);
                }
                foreach (var candidates in new[] { _playerCandidates, _opponentCandidates })
                    foreach (var id in candidates)
                    {
                        var config = tables.TbRobot.Get(id);
                        if (!GameModule.Resource.CheckLocationValid(config.RobotPrefab) ||
                            !GameModule.Resource.CheckLocationValid(config.RobotIcon))
                            throw new InvalidOperationException("商店机器人资源未收集：" + id);
                    }
                if (!GameModule.Resource.CheckLocationValid("BattleShopUI"))
                    throw new InvalidOperationException("BattleShopUI 未加入 YooAsset 资源收集。");
                var preparation = new GameObject("PreparedRobotViews");
                preparation.transform.SetParent(transform, false);
                preparation.SetActive(false);
                _preparedRoot = preparation.transform;
                Context = new BattleShopContext(_rules, _playerCandidates, _opponentCandidates, tables.TbRobot.Get);
                _rewards = new BattleRewardAdapter(Context);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, OnShopChanged);
                _listening = true;
                var context = Context;
                _bootstrap.BindBattle(context.BattleId, () => IsCombatRunning && Context == context);
                _playerGlass = GetGlassController(_playerBoard);
                _opponentGlass = GetGlassController(_opponentBoard);
                await _playerGlass.PrepareAsync(this, context, _playerBoard, BattleSide.Player, layout, tables, cancellationToken);
                await _opponentGlass.PrepareAsync(this, context, _opponentBoard, BattleSide.Opponent, layout, tables, cancellationToken);
                var window = await GameModule.UI.ShowUIAsyncAwait<BattleShopUI>(context, this)
                    .AttachExternalCancellation(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || Context != context || context.IsEnded) return;
                if (window == null || !window.IsPrepare || window.IsDestroyed)
                    throw new InvalidOperationException("商店窗口加载失败。");
                _preparationRemaining = 3f;
                _running = _frozen = false;
                _ready = true;
            }
            catch (OperationCanceledException)
            {
                if (generation == _initializationGeneration)
                {
                    CloseOwnedWindow();
                    Cleanup();
                }
            }
            catch (Exception e)
            {
                if (generation != _initializationGeneration) return;
                CloseOwnedWindow();
                Cleanup();
                Debug.LogError("商店初始化失败；修复配置或资源后调用 InitializeAsync 重试：" + e.Message, this);
            }
            finally
            {
                if (generation == _initializationGeneration)
                {
                    _initializing = false;
                    _initializationCancellation?.Dispose();
                    _initializationCancellation = null;
                }
            }
        }

        private static GlassBoardController GetGlassController(PlacementBoardView board)
        {
            var root = board.transform.Find("Glasses");
            if (root == null)
            {
                root = new GameObject("Glasses").transform;
                root.SetParent(board.transform, false);
            }
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            var controller = root.GetComponent<GlassBoardController>();
            return controller != null ? controller : root.gameObject.AddComponent<GlassBoardController>();
        }

        public void SetFrozen(bool frozen)
        {
            if (!IsReady) return;
            _frozen = frozen;
            _bootstrap.SetFrozen(frozen);
        }

        public async UniTask<ShopOperationResult> DeployRobotAsync(int instanceId, BoardPlacementTarget target,
            CancellationToken cancellationToken)
        {
            Entity entity;
            if (!IsReady || !Context.Registry.TryGet(instanceId, out entity) || !(entity is RobotEntity robot))
                return ShopOperationResult.InvalidLocation;
            var context = Context;
            var battleId = context.BattleId;
            GameObject prepared = null;
            try
            {
                prepared = await GameModule.Resource.LoadGameObjectAsync(robot.Configuration.RobotPrefab,
                    _preparedRoot, cancellationToken);
                if (prepared == null) return ShopOperationResult.ResourceFailed;
                prepared.SetActive(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || Context != context || context.BattleId != battleId || context.IsEnded || !robot.IsDragging)
                    return ShopOperationResult.InvalidLocation;
                var view = prepared.GetComponent<RobotView>();
                if (view == null) return ShopOperationResult.ResourceFailed;
                view.Bind(context.Registry, instanceId);
                var result = context.TryDeploy(robot.Side, instanceId, _placement.GetBoardSide(target.Board),
                    target.Board.Model, target.Anchor);
                if (result != ShopOperationResult.Success) return result;
                // Notifications may synchronously end the battle; do not activate a late view.
                if (this == null || Context != context || context.IsEnded || robot.IsRemoved)
                {
                    if (target.Board != null) target.Board.RefreshAllCells();
                    return ShopOperationResult.BattleEnded;
                }
                prepared.transform.SetParent(target.EntityParent, false);
                prepared.transform.position = target.AnchorWorldPosition;
                prepared.transform.localRotation = Quaternion.identity;
                prepared.transform.localScale = Vector3.one;
                _views.Add(instanceId, prepared);
                prepared.SetActive(true);
                prepared = null;
                target.Board.RefreshOccupied(target.Board.Model.Evaluate(target.Anchor, robot.Definition).Cells);
                return ShopOperationResult.Success;
            }
            catch (OperationCanceledException) { return ShopOperationResult.ResourceFailed; }
            catch (Exception e) { Debug.LogException(e, this); return ShopOperationResult.ResourceFailed; }
            finally { if (prepared != null) Destroy(prepared); }
        }

        private void Update()
        {
            if (Context != null && Context.IsEnded) { EndBattle(); return; }
            if (IsReady && !_frozen)
            {
                _rewards?.Advance();
                if (!IsReady) return;
                if (!_running)
                {
                    _preparationRemaining = Mathf.Max(0f, _preparationRemaining - Time.deltaTime);
                    if (_preparationRemaining > 0f) return;
                    _running = true;
                    if (!_bootstrap.LaunchBoth(_playerBoard, _opponentBoard))
                    {
                        Cleanup();
                        Debug.LogError("双方能源球发射失败，修复后重新初始化对局。", this);
                        return;
                    }
                }
                Context.Registry.AdvanceTime(Time.deltaTime);
                _playerGlass?.Advance(Time.deltaTime);
                _opponentGlass?.Advance(Time.deltaTime);
            }
        }

        private void OnShopChanged(int side)
        {
            if (Context == null) return;
            if (Context.IsEnded) { EndBattle(); return; }
            foreach (var id in new List<int>(_views.Keys))
            {
                Entity entity;
                if (Context.Registry.TryGet(id, out entity)) continue;
                if (_views[id] != null) Destroy(_views[id]);
                _views.Remove(id);
            }
        }

        public void EndBattle()
        {
            CloseOwnedWindow();
            Cleanup();
        }

        private void CloseOwnedWindow()
        {
            if (UIModule.UIRoot == null) return;
            var window = GameModule.UI.GetUI<BattleShopUI>();
            if (window != null && window.UserDatas != null && window.UserDatas.Length > 1 &&
                ReferenceEquals(window.UserDatas[1], this)) GameModule.UI.CloseUI<BattleShopUI>();
        }

        private void ClearViews()
        {
            foreach (var pair in _views)
            {
                var id = pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (_playerBoard != null && _playerBoard.IsInitialized) _playerBoard.Model.Release(id);
                if (_opponentBoard != null && _opponentBoard.IsInitialized) _opponentBoard.Model.Release(id);
                if (pair.Value != null) Destroy(pair.Value);
            }
            _views.Clear();
        }

        private void Cleanup()
        {
            _ready = _running = _frozen = false;
            _preparationRemaining = 0f;
            if (_bootstrap != null) _bootstrap.StopBalls();
            // Detach old ownership before Dispose can notify listeners that start another battle.
            var context = Context;
            Context = null;
            var preparation = _preparedRoot;
            _preparedRoot = null;
            var rewards = _rewards;
            _rewards = null;
            _initializationGeneration++;
            _initializing = false;
            var cancellation = _initializationCancellation;
            _initializationCancellation = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            if (_listening)
            {
                _listening = false;
                GameEvent.RemoveEventListener<int>(BattleShopEvents.Changed, OnShopChanged);
            }
            rewards?.Dispose();
            _playerGlass?.Clear();
            _opponentGlass?.Clear();
            _playerGlass = _opponentGlass = null;
            ClearViews();
            context?.Dispose();
            if (_playerBoard != null) _playerBoard.RefreshAllCells();
            if (_opponentBoard != null) _opponentBoard.RefreshAllCells();
            if (preparation != null) Destroy(preparation.gameObject);
        }
        private void OnDestroy()
        {
            CloseOwnedWindow();
            Cleanup();
        }
    }
}
