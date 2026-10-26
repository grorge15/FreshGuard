using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameConfig;
using TEngine;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace GameLogic
{
    /// <summary>单局装配入口：双方共用纯逻辑上下文，只有玩家打开 UIWindow。</summary>
    [DisallowMultipleComponent]
    public sealed partial class BattleShopSceneController : MonoBehaviour
    {
        [SerializeField] private PlacementController _placement;
        [SerializeField] private PlacementBoardView _playerBoard;
        [SerializeField] private PlacementBoardView _opponentBoard;
        [SerializeField] private PlacementBoardsBootstrap _bootstrap;
        [SerializeField] private EnemyBattleController _enemies;
        [SerializeField] private int _layoutConfigId = 1;
        [SerializeField] private int[] _playerCandidates = { 501, 502, 503, 504, 505, 601, 602, 603 };
        [SerializeField] private int[] _opponentCandidates = { 501, 502, 503, 504, 505, 601, 602, 603 };
        private readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        private Transform _preparedRoot;
        private Transform _comboPopupRoot;
        private bool _initializing;
        private int _initializationGeneration;
        private CancellationTokenSource _initializationCancellation;
        private ShopRules _rules;
        private BattleRewardAdapter _rewards;
        private bool _listening;
        private bool _closingWindow;
        private bool _ready;
        private bool _running;
        private bool _frozen;
        private IProgress<BattleLoadingProgress> _loadingProgress;
        private readonly Stopwatch _loadingStopwatch = new Stopwatch();
        public Exception InitializationError { get; private set; }
        public bool IsInitializing => _initializing;
        private float _preparationRemaining;
        private GlassBoardController _playerGlass;
        private GlassBoardController _opponentGlass;
        private RobotCombatController _combat;
        public MainCombatUI CombatWindow { get; private set; }
        public BattleShopUI ShopWindow { get; private set; }
        public BattleShopContext Context { get; private set; }
        public PlacementController Placement => _placement;
        public int RefreshPrice => _rules.RefreshPrice;
        public bool IsReady => _ready && Context != null && !Context.IsEnded && !Context.IsEndRequested;
        public bool IsCombatRunning => IsReady && _running && !_frozen;
        /// <summary>
        /// 真正进入可见战斗态：双方能源球已发射、首批敌人已生成，并且物理系统已经完成至少一次反弹。
        /// 加载遮罩只有在此条件满足后才允许关闭。
        /// </summary>
        public bool IsCombatOperational
        {
            get
            {
                if (!IsCombatRunning || _bootstrap == null || _enemies == null ||
                    _bootstrap.PlayerBall == null || _bootstrap.OpponentBall == null)
                    return false;
                if (!_bootstrap.PlayerBall.IsLaunched || !_bootstrap.OpponentBall.IsLaunched)
                    return false;
                if (_enemies.GetSpawnedCount(BattleSide.Player) <= 0 ||
                    _enemies.GetSpawnedCount(BattleSide.Opponent) <= 0)
                    return false;
                return _bootstrap.PlayerBall.BounceCount > 0 && _bootstrap.OpponentBall.BounceCount > 0;
            }
        }
        public bool IsFrozen => _frozen;
        public float PreparationRemaining => _preparationRemaining;
        public PlacementBoardsBootstrap Bootstrap => _bootstrap;
        public GlassBoardController PlayerGlass => _playerGlass;
        public GlassBoardController OpponentGlass => _opponentGlass;
        public EnemyBattleController Enemies => _enemies;
        public RobotCombatController Combat => _combat;

        // 资源包和 Luban 配置由 Launcher/Procedure 异步完成；由 GameApp 在点击开始战斗后显式启动，
        // 避免 Main 场景作为 Launcher 入口时在资源包就绪前抢跑初始化。
        private void Start() { }

        public UniTask InitializeAsync()
        {
            return InitializeAsync(null);
        }

        /// <summary>初始化战斗并向主界面加载条报告阶段进度。</summary>
        public async UniTask InitializeAsync(IProgress<BattleLoadingProgress> progress)
        {
            if (this != null && Context != null && Context.IsEnded) EndBattle();
            if (this == null || Context != null) return;
            if (_initializing)
            {
                _loadingProgress = progress;
                return;
            }
            _initializing = true;
            _loadingProgress = progress ?? _loadingProgress;
            _loadingStopwatch.Restart();
            InitializationError = null;
            var generation = ++_initializationGeneration;
            _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            var cancellationToken = _initializationCancellation.Token;
            try
            {
                ReportLoading(BattleLoadingStage.Config, 0.35f, "读取战斗配置");
                // 任何事务通知内都可能结束并重启；跨帧等事件延迟解绑与窗口Pop完成。
                // 先持有初始化代次和令牌，等待期间的再次终局仍可取消本次初始化。
                await UniTask.Yield(cancellationToken);
                var tables = ConfigSystem.Instance.Tables;
                if (_placement == null || _playerBoard == null || _opponentBoard == null || _bootstrap == null)
                    throw new InvalidOperationException("商店缺少双方盘面或统一启动器引用。");
                _bootstrap.StopBalls();
                var layout = await ReadBoardLayoutAsync(tables, cancellationToken);
                ReportLoading(BattleLoadingStage.Config, 0.45f, "初始化战场规则");
                if (!_playerBoard.ResetForBattle(layout) || !_opponentBoard.ResetForBattle(layout) || !_bootstrap.Initialize())
                    throw new InvalidOperationException("双方棋盘初始化失败。");
                ApplyMapLayout();
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
                if (!GameModule.Resource.CheckLocationValid("MainCombatUI"))
                    throw new InvalidOperationException("MainCombatUI 未加入 YooAsset 资源收集。");
                var preparation = new GameObject("PreparedRobotViews");
                preparation.transform.SetParent(transform, false);
                preparation.SetActive(false);
                _preparedRoot = preparation.transform;
                Context = new BattleShopContext(_rules, _playerCandidates, _opponentCandidates, tables.TbRobot.Get,
                    skillDefinitions: SkillDefinitionRegistry.FromTables(tables));
                BattleSettlementPresenter.Instance.BeginBattle(Context.BattleId);
                _rewards = new BattleRewardAdapter(Context);
                GameEvent.AddEventListener<int>(BattleShopEvents.Changed, OnShopChanged);
                GameEvent.AddEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, OnRobotMerged);
                _listening = true;
                var context = Context;
                var popupRoot = new GameObject("ComboPopups");
                popupRoot.transform.SetParent(transform, false);
                _comboPopupRoot = popupRoot.transform;
                _bootstrap.BindBattle(context.BattleId, () => IsCombatRunning && Context == context,
                    context.Registry, _comboPopupRoot);
                _bootstrap.RobotContactDetected += OnRobotContact;
                _bootstrap.BerserkChanged += OnBerserkChanged;
                _playerGlass = GetGlassController(_playerBoard);
                _opponentGlass = GetGlassController(_opponentBoard);
                ReportLoading(BattleLoadingStage.Glass, 0.55f, "加载玻璃布局");
                await _playerGlass.PrepareAsync(this, context, _playerBoard, BattleSide.Player, layout, tables, cancellationToken);
                await _opponentGlass.PrepareAsync(this, context, _opponentBoard, BattleSide.Opponent, layout, tables, cancellationToken);
                ReportLoading(BattleLoadingStage.Glass, 0.65f, "玻璃布局完成");
                ValidateMapSpawn();
                PrepareTowers(context, tables, cancellationToken);
                if (_enemies == null) throw new InvalidOperationException("缺少 EnemyBattleController 场景引用。");
                ReportLoading(BattleLoadingStage.Enemy, 0.72f, "预加载敌人");
                await _enemies.PrepareAsync(this, context, tables, cancellationToken);
                ReportLoading(BattleLoadingStage.Enemy, 0.82f, "敌人资源完成");
                _combat = new RobotCombatController(this, context, _enemies, _views);
                ReportLoading(BattleLoadingStage.CombatPresentation, 0.86f, "准备战斗表现");
                await _combat.PrepareAsync(tables, cancellationToken);
                // 完整等待加载，取消后也要收回本局迟到的窗口，避免遗留 UI。
                ReportLoading(BattleLoadingStage.CombatUi, 0.90f, "加载战斗界面");
                var window = await GameModule.UI.ShowUIAsyncAwait<MainCombatUI>(context, this, Progress);
                if (cancellationToken.IsCancellationRequested || this == null || Context != context ||
                    context.IsEnded || context.IsEndRequested)
                {
                    if (window != null && ReferenceEquals(window.Context, context) && ReferenceEquals(window.Host, this))
                        GameModule.UI.CloseUI<MainCombatUI>();
                    throw new OperationCanceledException(cancellationToken);
                }
                if (window == null || !window.IsPrepare || window.IsDestroyed ||
                    !ReferenceEquals(window.Context, context) || !ReferenceEquals(window.Host, this))
                    throw new InvalidOperationException("主战斗窗口加载失败或归属已变化。");
                CombatWindow = window;
                ShopWindow = window.Shop;
                _battleInfoWindow = window.Info;
                PublishBattleProgress(true);
                _preparationRemaining = 3f;
                _running = _frozen = false;
                _ready = true;
                ReportLoading(BattleLoadingStage.Ai, 0.97f, "初始化对手 AI");
                InitializeOpponentAi();
                _playerTowerView.gameObject.SetActive(true);
                _opponentTowerView.gameObject.SetActive(true);
                ReportLoading(BattleLoadingStage.Ready, 1f, "战斗准备完成");
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
                InitializationError = e;
                ReportLoading(BattleLoadingStage.Failed, 1f, "战斗加载失败：" + e.Message);
                CloseOwnedWindow();
                Cleanup();
                UnityEngine.Debug.LogError("商店初始化失败；修复配置或资源后调用 InitializeAsync 重试：" + e.Message, this);
            }
            finally
            {
                if (generation == _initializationGeneration)
                {
                    _initializing = false;
                    _initializationCancellation?.Dispose();
                    _initializationCancellation = null;
                    _loadingProgress = null;
                    _loadingStopwatch.Stop();
                }
            }
        }

        private void ReportLoading(BattleLoadingStage stage, float progress, string message)
        {
            var snapshot = new BattleLoadingProgress(stage, progress, message,
                _loadingStopwatch.IsRunning ? _loadingStopwatch.Elapsed.TotalSeconds : 0d);
            _loadingProgress?.Report(snapshot);
            if (stage == BattleLoadingStage.Ready || stage == BattleLoadingStage.Failed)
                UnityEngine.Debug.Log($"[BattleLoading] {stage} {snapshot.Progress:P0} {snapshot.ElapsedSeconds:F2}s - {message}", this);
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
            root.gameObject.SetActive(true);
            var controller = root.GetComponent<GlassBoardController>();
            return controller != null ? controller : root.gameObject.AddComponent<GlassBoardController>();
        }

        public void SetFrozen(bool frozen)
        {
            if (!IsReady) return;
            _frozen = frozen;
            _bootstrap.SetFrozen(frozen);
            _enemies?.SetFrozen(frozen);
        }

        public void PressBoardRobot(int instanceId, int pointerId, Vector2 position, float now)
        {
            if (!IsReady || ShopWindow == null) return;
            Entity entity;
            if (!Context.Registry.TryGet(instanceId, out entity) || !(entity is RobotEntity robot) ||
                robot.Side != BattleSide.Player || robot.Location != RobotLocation.Board || !robot.Anchor.HasValue) return;
            PlacementBoardView board;
            BoardCoordinate cell;
            if (!_placement.TryGetPointerCell(position, out board, out cell) || board != _playerBoard) return;
            if (!_views.TryGetValue(instanceId, out var instance) || instance == null) return;
            var view = instance.GetComponent<RobotView>();
            if (view == null) return;
            Vector3 worldPosition;
            if (!_placement.TryGetPointerWorldPosition(position, out worldPosition) ||
                !view.TryGetNearestOccupiedCell(worldPosition, out var grabbed)) return;
            ShopWindow.Drag.Press(RobotDragSource.Owned(instanceId), pointerId, position, now, grabbed);
        }

        public void SetPlayerDragging(bool dragging)
        {
            if (_bootstrap != null && _bootstrap.PlayerBall != null) _bootstrap.PlayerBall.SetDragging(dragging);
        }

        private void OnRobotContact(PhysicalCollisionBall ball, int robotId)
        {
            if (IsCombatRunning && ball != null && Context != null && ball.BattleId == Context.BattleId)
                _combat?.TryAttackFromContact(ball.BattleId, ball.Side, robotId);
        }

        private void OnBerserkChanged(BattleSide side, bool active)
        {
            var views = new List<RobotView>();
            foreach (var instance in _views.Values)
            {
                if (instance == null) continue;
                var view = instance.GetComponent<RobotView>();
                if (view != null && Context != null && Context.Registry.TryGet(view.InstanceId, out var entity) && entity.Side == side)
                    views.Add(view);
            }
            if (!active)
            {
                var main = side == BattleSide.Player ? _bootstrap.PlayerBall : _bootstrap.OpponentBall;
                if (main != null) main.ProtectOverlappingRobots(views);
            }
            foreach (var view in views) view.SetBerserkTrigger(active);
        }

        private void SyncRobotCollisionMode(RobotView view, BattleSide side)
        {
            if (view == null || _bootstrap == null) return;
            view.SetBerserkTrigger(_bootstrap.IsBerserk(side));
            var main = side == BattleSide.Player ? _bootstrap.PlayerBall : _bootstrap.OpponentBall;
            if (main != null) main.RefreshProtectedRobot(view);
        }

        public void UpdateMergeCandidates(RobotDragSource source)
        {
            if (Context == null) return;
            foreach (var pair in _views)
            {
                if (pair.Value == null) continue;
                pair.Value.GetComponent<RobotView>()?.SetMergeCandidate(
                    Context.CheckMerge(BattleSide.Player, source, pair.Key) == ShopOperationResult.Success);
            }
        }

        public void ClearMergeCandidates()
        {
            foreach (var pair in _views)
                if (pair.Value != null) pair.Value.GetComponent<RobotView>()?.SetMergeCandidate(false);
        }

        public async UniTask<ShopOperationResult> DeployDragAsync(RobotDragSession session, BoardPlacementTarget target,
            CancellationToken cancellationToken)
        {
            var context = Context;
            if (!IsReady || session == null || session.BattleId != context.BattleId ||
                !context.IsCurrentInteraction(session.InteractionId)) return ShopOperationResult.InvalidLocation;
            GameConfig.robot.Robot config;
            int level;
            PlaceableDefinition definition;
            var content = context.GetDragContent(session.Side, session.Source, out config, out level, out definition);
            if (content != ShopOperationResult.Success) return content;
            if (target.Board == null || !target.Board.Model.Evaluate(target.Anchor, definition).IsValid)
                return ShopOperationResult.InvalidPlacement;
            GameObject prepared = null;
            GameObject existing = null;
            if (session.Source.Kind == RobotDragSourceKind.OwnedRobot) _views.TryGetValue(session.Source.InstanceId, out existing);
            try
            {
                if (existing == null)
                {
                    prepared = await GameModule.Resource.LoadGameObjectAsync(config.RobotPrefab, _preparedRoot, cancellationToken);
                    if (prepared == null) return ShopOperationResult.ResourceFailed;
                    prepared.SetActive(false);
                    var checkedView = prepared.GetComponent<RobotView>();
                    if (checkedView == null || checkedView.ConfigId != config.Id) return ShopOperationResult.ResourceFailed;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (this == null || Context != context || context.IsEnded || !context.IsCurrentInteraction(session.InteractionId))
                    return ShopOperationResult.InvalidLocation;
                RobotEntity robot;
                var result = context.TryCommitDeploy(session.InteractionId, _placement.GetBoardSide(target.Board),
                    target.Board.Model, target.Anchor, out robot);
                if (result != ShopOperationResult.Success) return result;
                // 同步通知可以终局；已提交结果仍为成功，不激活已清理对局的视图。
                if (this == null || Context != context || context.IsEnded || robot.IsRemoved) return ShopOperationResult.Success;
                var instance = existing != null ? existing : prepared;
                BindDeployedRobotView(context, robot, instance, target);
                prepared = null;
                return ShopOperationResult.Success;
            }
            catch (OperationCanceledException) { return ShopOperationResult.ResourceFailed; }
            catch (Exception e) { UnityEngine.Debug.LogException(e, this); return ShopOperationResult.ResourceFailed; }
            finally { if (prepared != null) Destroy(prepared); }
        }

        private void BindDeployedRobotView(BattleShopContext context, RobotEntity robot, GameObject instance,
            BoardPlacementTarget target)
        {
            var view = instance.GetComponent<RobotView>();
            // Bind 会启用碰撞体；双方同步同一套碰撞、视图和战斗注册流程。
            SyncRobotCollisionMode(view, robot.Side);
            view.Bind(context.Registry, robot.InstanceId);
            view.BindInput(this);
            instance.transform.SetParent(target.EntityParent, false);
            instance.transform.position = target.AnchorWorldPosition;
            instance.transform.localRotation = Quaternion.identity;
            view.ApplyBoardScale(target.Board);
            _views[robot.InstanceId] = instance;
            instance.SetActive(true);
            view.Render(robot);
            SyncRobotCollisionMode(view, robot.Side);
            _combat?.SyncRobots();
            target.Board.RefreshAllCells();
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
                SyncRobotCollisionMode(view, robot.Side);
                view.BindInput(this);
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
                view.ApplyBoardScale(target.Board);
                _views.Add(instanceId, prepared);
                prepared.SetActive(true);
                SyncRobotCollisionMode(view, robot.Side);
                _combat?.SyncRobots();
                prepared = null;
                target.Board.RefreshOccupied(target.Board.Model.Evaluate(target.Anchor, robot.Definition).Cells);
                return ShopOperationResult.Success;
            }
            catch (OperationCanceledException) { return ShopOperationResult.ResourceFailed; }
            catch (Exception e) { UnityEngine.Debug.LogException(e, this); return ShopOperationResult.ResourceFailed; }
            finally { if (prepared != null) Destroy(prepared); }
        }

        private void Update()
        {
            if (_pendingTowerSettlement.HasValue) { EndBattle(); return; }
            if (Context != null && Context.IsEnded) { EndBattle(); return; }
            if (IsReady && !_frozen)
            {
                var startedThisFrame = false;
                _rewards?.Advance();
                if (!IsReady) return;
                AdvanceOpponentAi(Time.deltaTime);
                if (!IsReady) return;
                if (!_running)
                {
                    _preparationRemaining = Mathf.Max(0f, _preparationRemaining - Time.deltaTime);
                    if (_preparationRemaining > 0f) return;
                    _running = true;
                    if (!_bootstrap.LaunchBoth(_playerBoard, _opponentBoard))
                    {
                        Cleanup();
                        UnityEngine.Debug.LogError("双方能源球发射失败，修复后重新初始化对局。", this);
                        return;
                    }
                    try { _enemies?.StartBattle(); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e, this); return; }
                    startedThisFrame = true;
                }
                var frameContext = Context;
                try
                {
                    frameContext.Battle.AdvanceTime(Time.deltaTime);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e, this);
                    if (Context == frameContext) EndBattle();
                    return;
                }
                if (Context != frameContext || !IsCombatRunning) return;
                _bootstrap.AdvanceBerserk(Time.deltaTime);
                if (!IsCombatRunning) return;
                _playerGlass?.Advance(Time.deltaTime);
                _opponentGlass?.Advance(Time.deltaTime);
                if (!startedThisFrame && IsCombatRunning)
                {
                    try { _enemies?.Advance(Time.deltaTime); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e, this); }
                }
                if (IsCombatRunning)
                {
                    try
                    {
                        _combat?.AdvanceExistingBullets(Time.deltaTime);
                        if (IsCombatRunning) _combat?.AdvanceAttacks(Time.deltaTime);
                    }
                    catch (Exception e) { UnityEngine.Debug.LogException(e, this); EndBattle(); }
                }
                TryCompleteWaveClear(frameContext);
            }
        }

        private void OnShopChanged(int side)
        {
            _opponentAi?.MarkDirty();
            if (Context == null) return;
            if (Context.IsEnded) { EndBattle(); return; }
            foreach (var id in new List<int>(_views.Keys))
            {
                Entity entity;
                if (Context.Registry.TryGet(id, out entity) && entity is RobotEntity robot)
                {
                    var instance = _views[id];
                    if (instance != null)
                    {
                        if (robot.Location == RobotLocation.Board && robot.Anchor.HasValue)
                        {
                            var board = robot.Side == BattleSide.Player ? _playerBoard : _opponentBoard;
                            instance.transform.SetParent(board.transform, false);
                            instance.transform.position = board.GetCellWorldPosition(robot.Anchor.Value);
                            instance.GetComponent<RobotView>()?.ApplyBoardScale(board);
                        }
                        var view = instance.GetComponent<RobotView>();
                        SyncRobotCollisionMode(view, robot.Side);
                        view?.Render(robot);
                        SyncRobotCollisionMode(view, robot.Side);
                    }
                    continue;
                }
                if (_views[id] != null) { _views[id].SetActive(false); Destroy(_views[id]); }
                _views.Remove(id);
            }
            _playerBoard.RefreshAllCells();
            _opponentBoard.RefreshAllCells();
            _combat?.SyncRobots();
        }

        private void OnRobotMerged(RobotMergeSnapshot snapshot)
        {
            if (Context == null || Context.BattleId != snapshot.BattleId || Context.IsEnded) return;
            OnShopChanged((int)snapshot.Side);
            GameObject view;
            if (_views.TryGetValue(snapshot.TargetInstanceId, out view) && view != null)
                view.GetComponent<RobotView>()?.PlayMergeFeedback();
        }

        public void EndBattle()
        {
            var context = Context;
            // 商店事务也可能在通知中造成塔死亡；等待该事务退出后再清理、广播。
            if (_pendingTowerSettlement.HasValue && context != null && context.IsMutationInProgress) return;
            var settlement = _pendingTowerSettlement;
            _pendingTowerSettlement = null;
            var generation = _initializationGeneration;
            CloseOwnedWindow();
            // 关闭窗口的取消通知可以重入终局并启动新局，只清理本次捕获的所有权。
            if (Context == context && generation == _initializationGeneration) Cleanup();
            if (settlement.HasValue) NotifySettlementAfterCleanupAsync(context, settlement.Value).Forget();
        }

        private void CloseOwnedWindow()
        {
            if (_closingWindow || UIModule.UIRoot == null) return;
            var window = GameModule.UI.GetUI<MainCombatUI>();
            if (window != null && window.UserDatas != null && window.UserDatas.Length > 1 &&
                ReferenceEquals(window.UserDatas[1], this))
            {
                _closingWindow = true;
                try { GameModule.UI.CloseUI<MainCombatUI>(); }
                finally { _closingWindow = false; }
            }
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
            DisposeOpponentAi();
            CombatWindow = null;
            ShopWindow = null;
            _battleInfoWindow = null;
            _lastProgressBattleId = 0;
            _lastProgressWave = 0;
            _lastProgressSecond = -1;
            _ready = _running = _frozen = false;
            _preparationRemaining = 0f;
            if (_bootstrap != null)
            {
                _bootstrap.UnbindBattle();
                _bootstrap.RobotContactDetected -= OnRobotContact;
                _bootstrap.BerserkChanged -= OnBerserkChanged;
            }
            var popupRoot = _comboPopupRoot;
            _comboPopupRoot = null;
            if (popupRoot != null) Destroy(popupRoot.gameObject);
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
                GameEvent.RemoveEventListener<RobotMergeSnapshot>(BattleShopEvents.Merged, OnRobotMerged);
            }
            rewards?.Dispose();
            var combat = _combat;
            _combat = null;
            combat?.Dispose();
            _enemies?.Clear();
            ClearTowers();
            _playerGlass?.Clear();
            _opponentGlass?.Clear();
            _playerGlass = _opponentGlass = null;
            ClearViews();
            RestoreMapLayout();
            context?.Dispose();
            if (_playerBoard != null) _playerBoard.RefreshAllCells();
            if (_opponentBoard != null) _opponentBoard.RefreshAllCells();
            if (preparation != null) Destroy(preparation.gameObject);
        }
        private void OnDestroy()
        {
            var context = Context;
            var settlement = _pendingTowerSettlement;
            _pendingTowerSettlement = null;
            CloseOwnedWindow();
            Cleanup();
            if (settlement.HasValue) NotifySettlementAfterCleanupAsync(context, settlement.Value).Forget();
        }
    }
}
