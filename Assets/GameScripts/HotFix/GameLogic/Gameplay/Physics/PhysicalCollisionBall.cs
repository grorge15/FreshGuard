using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class PhysicalCollisionBall : MonoBehaviour
    {
        [SerializeField] private Vector2 spawnPosition;
        [SerializeField] private float initialAngleMin = 20f;
        [SerializeField] private float initialAngleMax = 70f;
        [SerializeField, Min(0.01f)] private float speed = 5f;
        [SerializeField, Min(0.001f)] private float radius = 0.2f;
        [SerializeField] private bool launchOnEnable;
        [Header("状态外观")]
        [Tooltip("普通状态使用的 Sprite；未配置时使用 SpriteRenderer 原有图片。")]
        [SerializeField] private Sprite _normalSprite;
        [Tooltip("暴走状态使用的 Sprite；未配置时使用普通状态图片。")]
        [SerializeField] private Sprite _berserkSprite;
        [Tooltip("Luban 生成的 Global 二进制；作为 Prefab 依赖加载，不在碰撞回调中进行 IO。")]
        [SerializeField] private TextAsset globalConfig;
        [SerializeField] private ComboPopupView _comboPopupPrefab;
        [SerializeField, Min(0f)] private float _comboPopupOffset = 0.35f;
        private Rigidbody2D _body;
        private CircleCollider2D _collider;
        private SpriteRenderer _spriteRenderer;
        private Sprite _defaultSprite;
        private Vector2 _direction;
        private bool _launched;
        private bool _frozen;
        private bool _dragging;
        private Func<bool> _runningGate;
        private readonly HashSet<int> _glassHitsThisStep = new HashSet<int>();
        private GameConfig.globalcfg.TbGlobal _globalTable;
        private bool _comboEnabled;
        private BattleEntityRegistry _comboRegistry;
        private Transform _comboPopupRoot;
        private float _comboTimeout;
        private double _comboElapsed;
        private bool _reconcileAfterFreeze;
        private BallGlobalRules _rules;
        private Vector2 _launchPosition;
        private float _lowSpeedElapsed;
        private float _rageReentryRemaining;
        private readonly Dictionary<int, RobotContact> _robotContacts = new Dictionary<int, RobotContact>();
        private readonly HashSet<int> _robotHitsThisStep = new HashSet<int>();
        private readonly List<int> _endedRobotContacts = new List<int>();
        private readonly List<ComboPopupView> _comboPopups = new List<ComboPopupView>();
        private readonly Dictionary<int, RobotContact> _protectedContacts = new Dictionary<int, RobotContact>();

        private sealed class RobotContact
        {
            public RobotView View;
            public Collider2D Collider;
            public bool Counted;
            public bool AttackHandled;
            public float StayElapsed;
        }

        private Vector3? _mapOriginalScale;
        private float _mapScale = 1f;
        public float MapScale => _mapScale;
        public float WorldRadius => radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

        public int ComboCount { get; private set; }
        public Func<int, float> GlobalValueProvider { get; set; }
        public Vector2 CurrentDirection => _direction;
        public float Speed => _rules == null ? speed : IsBerserk ? _rules.RageSpeed : _rules.NormalSpeed;
        public float EffectiveSpeed
        {
            get
            {
                return Speed * _mapScale * (!_dragging || IsBerserk ? 1f : ReadDraggingMultiplier());
            }
        }
        public int BounceCount { get; private set; }
        public BattleSide Side { get; private set; }
        public long BattleId { get; private set; }
        public bool IsBerserk { get; private set; }
        public bool IsLaunched => _launched;
        public float SecondsSinceEffectiveInteraction { get; private set; }
        public event Action<PhysicalCollisionBall> EffectiveInteraction;
        public event Action<PhysicalCollisionBall> ComboThresholdReached;
        public event Action<PhysicalCollisionBall, int> RobotContactDetected;

        public void ConfigureFromTables(GameConfig.Tables tables) => Configure(BallGlobalRules.FromTables(tables));

        public void Configure(BallGlobalRules rules)
        {
            if (_launched) throw new InvalidOperationException("能源球规则只能在发射前装配。");
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        internal PhysicalCollisionBall CreateTemporary()
        {
            var ball = Instantiate(this, transform.parent);
            ball.gameObject.SetActive(false);
            if (ball.gameObject.scene != gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ball.gameObject, gameObject.scene);
            ball._mapScale = _mapScale;
            ball._mapOriginalScale = _mapOriginalScale;
            ball.GlobalValueProvider = GlobalValueProvider;
            return ball;
        }

        public float SampleLaunchAngle() => _rules == null
            ? UnityEngine.Random.Range(Mathf.Min(initialAngleMin, initialAngleMax), Mathf.Max(initialAngleMin, initialAngleMax))
            : UnityEngine.Random.Range((float)_rules.LaunchAngleMin, _rules.LaunchAngleMax);

        public void SetMapScale(float scale)
        {
            if (_launched) throw new InvalidOperationException("能源球运行中不能修改地图倍率。");
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (!_mapOriginalScale.HasValue) _mapOriginalScale = transform.localScale;
            var original = _mapOriginalScale.Value;
            var scaled = new Vector3(original.x * scale, original.y * scale, original.z);
            if (float.IsNaN(scaled.x) || float.IsInfinity(scaled.x) || float.IsNaN(scaled.y) || float.IsInfinity(scaled.y) ||
                scaled.x == 0f || scaled.y == 0f)
                throw new ArgumentOutOfRangeException(nameof(scale), "能源球缩放超出有效数值范围。");
            transform.localScale = scaled;
            _mapScale = scale;
        }

        private float ReadDraggingMultiplier()
        {
            if (GlobalValueProvider == null) return 1f;
            float multiplier = GlobalValueProvider(17);
            return !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f && multiplier <= 1f
                ? multiplier : 1f;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<CircleCollider2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null) _defaultSprite = _spriteRenderer.sprite;
            RefreshSprite();
            _body.gravityScale = 0f;
            _body.drag = 0f;
            _body.angularDrag = 0f;
            _body.freezeRotation = true;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            GetComponent<CircleCollider2D>().radius = radius;
            if (globalConfig != null)
            {
                _globalTable = new GameConfig.globalcfg.TbGlobal(new Luban.ByteBuf(globalConfig.bytes));
                GlobalValueProvider = key => _globalTable.Get(key).Value;
            }
            else
            {
                GlobalValueProvider = key => ConfigSystem.Instance.Tables.TbGlobal.Get(key).Value;
            }
        }

        private void OnEnable() { if (launchOnEnable) Launch(); }
        private void OnDisable() { Stop(); }
        private void FixedUpdate()
        {
            try
            {
                _glassHitsThisStep.Clear();
                _robotHitsThisStep.Clear();
                if (_launched && !_frozen && _runningGate != null && !_runningGate()) { Stop(); return; }
                if (_launched && !_frozen && (_runningGate == null || _runningGate()))
                {
                    ReconcileProtectedContacts();
                    ReconcileRobotContacts();
                    AdvanceContactTime(Time.fixedDeltaTime);
                    AdvanceComboTime(Time.fixedDeltaTime);
                    SecondsSinceEffectiveInteraction += Time.fixedDeltaTime;
                    if (_rules != null)
                    {
                        _lowSpeedElapsed = _body.velocity.magnitude < _rules.LowSpeedThreshold * _mapScale
                            ? _lowSpeedElapsed + Time.fixedDeltaTime : 0f;
                        if (_lowSpeedElapsed >= _rules.LowSpeedDuration ||
                            SecondsSinceEffectiveInteraction >= _rules.NoInteractionDuration)
                            LaunchAt(_launchPosition, SampleLaunchAngle());
                    }
                    _body.velocity = _direction * EffectiveSpeed;
                }
            }
            catch (Exception e) { StopWithError(e); }
        }

        public void BindBattle(long battleId, BattleSide side, Func<bool> runningGate)
        {
            Stop();
            _comboEnabled = false;
            _comboRegistry = null;
            _comboPopupRoot = null;
            if (battleId <= 0 || runningGate == null) throw new ArgumentException("能源球缺少有效对局绑定。");
            EntityValidation.RequireSide(side);
            BattleId = battleId;
            Side = side;
            _runningGate = runningGate;
            IsBerserk = false;
            _frozen = false;
            SecondsSinceEffectiveInteraction = 0f;
            _rageReentryRemaining = 0f;
        }

        public void BindCombo(BattleEntityRegistry registry, Transform popupRoot)
        {
            if (_launched || BattleId <= 0 || _runningGate == null || registry == null || popupRoot == null)
                throw new InvalidOperationException("Combo必须在主球绑定本局后、发射前装配。");
            float timeout = GlobalValueProvider(34);
            if (float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0f)
                throw new InvalidOperationException("Global34 Combo断档时间必须为有限正数。");
            if (_comboPopupPrefab == null || _comboPopupPrefab.GetComponent<TextMesh>() == null ||
                _comboPopupPrefab.GetComponentInChildren<Collider2D>(true) != null ||
                float.IsNaN(_comboPopupOffset) || float.IsInfinity(_comboPopupOffset) || _comboPopupOffset < 0f)
                throw new InvalidOperationException("主球缺少有效Combo提示Prefab或出现偏移配置。");
            _comboTimeout = timeout;
            _comboRegistry = registry;
            _comboPopupRoot = popupRoot;
            _comboEnabled = true;
        }

        public void BindRobotContacts(BattleEntityRegistry registry)
        {
            if (_launched || BattleId <= 0 || _runningGate == null || registry == null)
                throw new InvalidOperationException("机器人接触必须在球绑定本局后、发射前装配。");
            _comboRegistry = registry;
        }

        public void SetDragging(bool dragging)
        {
            try
            {
                // 先验证配置再提交拖拽状态，冻结、未发射或暴走不能掩盖配置读取失败。
                if (dragging) ReadDraggingMultiplier();
                _dragging = dragging;
                ApplyEffectiveVelocity();
            }
            catch (Exception e) { StopWithError(e); }
        }

        public void SetBerserk(bool berserk)
        {
            if (!berserk && IsBerserk) _rageReentryRemaining = _rules?.ReentryBlockSeconds ?? 0f;
            if (berserk && !IsBerserk)
            {
                ResetComboCount();
                foreach (var contact in _robotContacts.Values) contact.Counted = true;
            }
            IsBerserk = berserk;
            RefreshSprite();
            _reconcileAfterFreeze = true;
            ApplyEffectiveVelocity();
        }

        private void RefreshSprite()
        {
            if (_spriteRenderer == null) return;
            var normal = _normalSprite != null ? _normalSprite : _defaultSprite;
            _spriteRenderer.sprite = IsBerserk && _berserkSprite != null ? _berserkSprite : normal;
        }

        /// <summary>放置完成后沿原运动方向脱离，跳过不足以容纳整颗球的机器人间隙。</summary>
        public void ResolvePlacementOverlap(RobotView placed, PlacementBoardView board)
        {
            if (!_launched || IsBerserk || _body == null || _collider == null || placed == null || board == null) return;
            var obstacle = placed.GetPhysicalCollider();
            if (obstacle == null || !obstacle.enabled || obstacle.isTrigger || !obstacle.gameObject.activeInHierarchy) return;
            Physics2D.SyncTransforms();
            if (!_collider.Distance(obstacle).isOverlapped) return;
            var direction = _direction.normalized;
            if (direction.sqrMagnitude < 0.5f) return;
            var center = (Vector2)_collider.bounds.center;
            var worldRadius = _collider.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            var clearance = Mathf.Max(0.001f * _mapScale, Physics2D.defaultContactOffset * 2f);
            var castRadius = worldRadius + clearance;
            var boardScale = board.transform.lossyScale;
            var reach = board.Size.magnitude * Mathf.Max(Mathf.Abs(boardScale.x), Mathf.Abs(boardScale.y)) +
                Vector2.Distance(center, board.transform.position) + castRadius * 2f;
            // 从棋盘外向球反向扫圆，得到每个实体在前进方向上的出口；正向从内部扫会给出零距离命中。
            var physics = gameObject.scene.GetPhysicsScene2D();
            var filter = new ContactFilter2D { useTriggers = false };
            var hits = new List<RaycastHit2D>();
            physics.CircleCast(center + direction * reach, castRadius, -direction, reach, filter, hits);
            var candidates = new List<float>();
            foreach (var hit in hits)
                if (IsPlacementObstacle(hit.collider)) candidates.Add(reach - hit.distance + clearance);
            candidates.Sort();
            var overlaps = new List<Collider2D>();
            foreach (var distance in candidates)
            {
                if (distance <= 0f) continue;
                var destination = center + direction * distance;
                if (!FitsInsideBoard(destination, castRadius, board)) continue;
                var blocked = false;
                physics.OverlapCircle(destination, castRadius, filter, overlaps);
                foreach (var collider in overlaps)
                    if (IsPlacementObstacle(collider)) { blocked = true; break; }
                if (blocked) continue;
                var position = _body.position + destination - center;
                _body.position = position;
                transform.position = new Vector3(position.x, position.y, transform.position.z);
                Physics2D.SyncTransforms();
                ReconcileProtectedContacts();
                ReconcileRobotContacts();
                ApplyEffectiveVelocity();
                return;
            }
            // 前方直到墙边都无容身位置时，沿用临时穿出保护，不能把球瞬移出棋盘或反转方向。
            ProtectOverlappingRobots(board.GetComponentsInChildren<RobotView>());
        }

        private bool IsPlacementObstacle(Collider2D collider) => collider != null && collider != _collider &&
            !collider.isTrigger && collider.gameObject.scene == gameObject.scene && collider.attachedRigidbody == null;

        private static bool FitsInsideBoard(Vector2 center, float worldRadius, PlacementBoardView board)
        {
            var local = board.transform.InverseTransformPoint(center);
            var scale = board.transform.lossyScale;
            return Mathf.Abs(local.x) + worldRadius / Mathf.Abs(scale.x) < board.Size.x * 0.5f &&
                Mathf.Abs(local.y) + worldRadius / Mathf.Abs(scale.y) < board.Size.y * 0.5f;
        }

        public void ProtectOverlappingRobots(IEnumerable<RobotView> views)
        {
            if (_collider == null || _comboRegistry == null) return;
            Physics2D.SyncTransforms();
            foreach (var view in views)
            {
                if (view == null || !view.TryGetComboRobot(_comboRegistry, Side, out var robot)) continue;
                var collider = view.GetPhysicalCollider();
                if (!TouchesRobot(collider)) continue;
                var contact = new RobotContact { View = view, Collider = collider, Counted = true, AttackHandled = true };
                _protectedContacts[robot.InstanceId] = contact;
                _robotContacts[robot.InstanceId] = contact;
                IgnoreRobot(contact, true);
            }
        }

        public void RefreshProtectedRobot(RobotView view)
        {
            if (view != null && _protectedContacts.TryGetValue(view.InstanceId, out var contact)) IgnoreRobot(contact, true);
        }

        private bool TouchesRobot(Collider2D collider)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return false;
            var distance = _collider.Distance(collider);
            return distance.isValid && distance.distance <= Physics2D.defaultContactOffset;
        }

        private void IgnoreRobot(RobotContact contact, bool ignore)
        {
            if (_collider == null) return;
            if (contact.Collider != null) Physics2D.IgnoreCollision(_collider, contact.Collider, ignore);
        }

        private void ReconcileProtectedContacts()
        {
            _endedRobotContacts.Clear();
            foreach (var pair in _protectedContacts)
            {
                var contact = pair.Value;
                if (contact.View == null || !contact.View.TryGetComboRobot(_comboRegistry, Side, out _) || !TouchesRobot(contact.Collider))
                    _endedRobotContacts.Add(pair.Key);
                else IgnoreRobot(contact, true);
            }
            foreach (var id in _endedRobotContacts)
            {
                IgnoreRobot(_protectedContacts[id], false);
                _protectedContacts.Remove(id);
                _robotContacts.Remove(id);
            }
        }

        public void SetFrozen(bool frozen)
        {
            if (_frozen && !frozen) _reconcileAfterFreeze = true;
            _frozen = frozen;
            ApplyEffectiveVelocity();
        }

        private void ApplyEffectiveVelocity()
        {
            if (_body == null) return;
            try
            {
                bool moving = _launched && !_frozen && (_runningGate == null || _runningGate());
                Vector2 velocity = moving ? _direction * EffectiveSpeed : Vector2.zero;
                _body.simulated = moving;
                _body.velocity = velocity;
            }
            catch (Exception e) { StopWithError(e); }
        }

        public void Stop()
        {
            _launched = false;
            _dragging = false;
            IsBerserk = false;
            _rageReentryRemaining = 0f;
            RefreshSprite();
            foreach (var contact in _protectedContacts.Values) IgnoreRobot(contact, false);
            _protectedContacts.Clear();
            _glassHitsThisStep.Clear();
            ClearCombo();
            if (_body != null) { _body.velocity = Vector2.zero; _body.simulated = false; }
        }

        public void Launch()
        {
            LaunchAt(spawnPosition, SampleLaunchAngle());
        }

        public void LaunchAt(Vector2 position, float angle)
        {
            if (_body == null || _frozen || (_runningGate != null && !_runningGate())) return;
            try
            {
                ValidateConfig();
                if (float.IsNaN(Speed) || float.IsInfinity(Speed) || Speed <= 0f || radius <= 0f)
                    throw new InvalidOperationException("速度与半径必须为正数。");
                if (float.IsNaN(initialAngleMin) || float.IsNaN(initialAngleMax) ||
                    float.IsInfinity(initialAngleMin) || float.IsInfinity(initialAngleMax))
                    throw new InvalidOperationException("发射角必须是有限角度。");
                if (float.IsNaN(angle) || float.IsInfinity(angle) || float.IsNaN(position.x) ||
                    float.IsNaN(position.y) || float.IsInfinity(position.x) || float.IsInfinity(position.y))
                    throw new InvalidOperationException("发射位置和角度必须有限。");
                float rad = angle * Mathf.Deg2Rad;
                _direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                _body.simulated = true;
                _body.position = position;
                _launchPosition = position;
                _lowSpeedElapsed = 0f;
                _body.velocity = _direction * EffectiveSpeed;
                BounceCount = 0;
                SecondsSinceEffectiveInteraction = 0f;
                _glassHitsThisStep.Clear();
                ClearCombo();
                _launched = true;
            }
            catch (Exception e) { StopWithError(e); }
        }

        private void OnCollisionEnter2D(Collision2D collision) { Bounce(collision); }
        private void OnCollisionStay2D(Collision2D collision) { Bounce(collision); }
        private void OnTriggerEnter2D(Collider2D collider) { ProcessRobotTrigger(collider); }
        private void OnTriggerStay2D(Collider2D collider) { ProcessRobotTrigger(collider); }
        private void ProcessRobotTrigger(Collider2D collider)
        {
            try { ProcessRobotCombo(collider.GetComponentInParent<RobotView>(), collider, false); }
            catch (Exception e) { StopWithError(e); }
        }
        private void Bounce(Collision2D collision)
        {
            if (!_launched || _frozen || (_runningGate != null && !_runningGate())) return;
            try
            {
                var glass = collision.collider.GetComponent<GlassView>();
                if (glass != null && (glass.Owner == null || !glass.Owner.IsRunning ||
                    glass.Owner.Side != Side || glass.Owner.BattleId != BattleId)) return;
                ValidateConfig();
                var bounced = false;
                for (int i = 0; i < collision.contactCount; i++)
                {
                    Vector2 normal = collision.GetContact(i).normal.normalized;
                    if (Vector2.Dot(_direction, normal) >= -0.00001f) continue;
                    float range = Mathf.Abs(GlobalValueProvider(IsBerserk ? 5 : 4));
                    _direction = BallBounceMath.Resolve(_direction, normal, UnityEngine.Random.Range(-range, range),
                        GlobalValueProvider(IsBerserk ? 7 : 6), UnityEngine.Random.value < 0.5f);
                    BounceCount++;
                    bounced = true;
                    break;
                }
                _body.velocity = _direction * EffectiveSpeed;
                var robot = collision.collider.GetComponentInParent<RobotView>();
                if (robot != null) ProcessRobotCombo(robot, collision.collider, bounced);
                if (bounced && glass != null && _glassHitsThisStep.Add(glass.InstanceId))
                {
                    var result = glass.Collide(this);
                    if (result != GlassCollisionResult.Ignored)
                    {
                        SecondsSinceEffectiveInteraction = 0f;
                        EffectiveInteraction?.Invoke(this);
                    }
                }
            }
            catch (Exception e) { StopWithError(e); }
        }

        private bool IsComboRunning => _comboEnabled && _launched && !_frozen && _runningGate != null && _runningGate();
        private bool IsContactRunning => _comboRegistry != null && _launched && !_frozen && _runningGate != null && _runningGate();

        private void ProcessRobotCombo(RobotView view, Collider2D collider, bool bounced)
        {
            if (!IsContactRunning || collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                view == null || collider != view.GetPhysicalCollider() ||
                !view.TryGetComboRobot(_comboRegistry, Side, out var robot)) return;
            if (!_robotContacts.TryGetValue(robot.InstanceId, out var contact))
            {
                contact = new RobotContact { View = view, Collider = collider };
                _robotContacts.Add(robot.InstanceId, contact);
            }
            if (_protectedContacts.ContainsKey(robot.InstanceId)) return;
            if (!contact.AttackHandled || IsBerserk && _rules != null && contact.StayElapsed >= _rules.RageStayInterval)
            {
                contact.AttackHandled = true;
                contact.StayElapsed = 0f;
                SecondsSinceEffectiveInteraction = 0f;
                RobotContactDetected?.Invoke(this, robot.InstanceId);
                // A contact notification can synchronously settle and dispose the battle.
                if (!IsContactRunning) return;
            }
            if (IsBerserk) { contact.Counted = true; return; }
            if (!IsComboRunning || !bounced || contact.Counted || !_robotHitsThisStep.Add(robot.InstanceId)) return;
            contact.Counted = true;
            if (_rageReentryRemaining > 0f) return;
            ComboCount++;
            _comboElapsed = 0d;
            if (ComboCount <= 50 && ComboCount % 10 == 0) ShowComboPopup();
            if (ComboCount == (_rules?.ComboTriggerCount ?? 50)) ComboThresholdReached?.Invoke(this);
        }

        private void AdvanceContactTime(float deltaTime)
        {
            foreach (var contact in _robotContacts.Values) contact.StayElapsed += deltaTime;
        }

        private void AdvanceComboTime(float deltaTime)
        {
            if (!IsComboRunning || IsBerserk) return;
            _rageReentryRemaining = Mathf.Max(0f, _rageReentryRemaining - deltaTime);
            if (ComboCount == 0) return;
            _comboElapsed += deltaTime;
            if (_comboElapsed > _comboTimeout) ResetComboCount();
        }

        private void ReconcileRobotContacts()
        {
            if (_comboRegistry == null || _frozen) return;
            if (_reconcileAfterFreeze) Physics2D.SyncTransforms();
            _endedRobotContacts.Clear();
            foreach (var pair in _robotContacts)
            {
                var contact = pair.Value;
                if (_protectedContacts.ContainsKey(pair.Key)) continue;
                if (contact.View == null || !contact.View.TryGetComboRobot(_comboRegistry, Side, out _))
                { _endedRobotContacts.Add(pair.Key); continue; }
                var collider = contact.Collider;
                var touching = false;
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                {
                    // Restoring simulated clears Unity's contact cache; retain the old episode
                    // while the shapes are still touching until the first resumed simulation.
                    touching = _collider.IsTouching(collider);
                    if (!touching && (_reconcileAfterFreeze || IsBerserk))
                    {
                        var distance = _collider.Distance(collider);
                        touching = distance.isValid && distance.distance <= Physics2D.defaultContactOffset;
                    }
                }
                if (!touching) _endedRobotContacts.Add(pair.Key);
            }
            foreach (var id in _endedRobotContacts) _robotContacts.Remove(id);
            _reconcileAfterFreeze = false;
        }

        private void ShowComboPopup()
        {
            ComboPopupView popup = null;
            try
            {
                if (_comboPopupRoot == null) throw new InvalidOperationException("Combo提示根节点已失效。");
                popup = Instantiate(_comboPopupPrefab, transform.position + Vector3.up * _comboPopupOffset,
                    Quaternion.identity, _comboPopupRoot);
                popup.Initialize(ComboCount, popup.transform.position);
                _comboPopups.Add(popup);
            }
            catch (Exception e)
            {
                if (popup != null) ReleaseComboPopup(popup);
                Debug.LogError("Combo提示播放失败：" + e.Message, this);
            }
        }

        private void Update()
        {
            if (!IsComboRunning) return;
            for (var i = _comboPopups.Count - 1; i >= 0; i--)
            {
                var popup = _comboPopups[i];
                try { if (popup != null && !popup.Advance(Time.deltaTime)) continue; }
                catch (Exception e) { Debug.LogError("Combo提示播放失败：" + e.Message, this); }
                _comboPopups.RemoveAt(i);
                if (popup != null) ReleaseComboPopup(popup);
            }
        }

        private void ResetComboCount() { ComboCount = 0; _comboElapsed = 0d; }

        private void ClearCombo()
        {
            ResetComboCount();
            _robotContacts.Clear();
            _robotHitsThisStep.Clear();
            _endedRobotContacts.Clear();
            _reconcileAfterFreeze = false;
            foreach (var popup in _comboPopups) if (popup != null) ReleaseComboPopup(popup);
            _comboPopups.Clear();
        }

        private static void ReleaseComboPopup(ComboPopupView popup)
        {
            if (Application.isPlaying) { popup.gameObject.SetActive(false); Destroy(popup.gameObject); }
            else DestroyImmediate(popup.gameObject);
        }

        private void ValidateConfig()
        {
            float range = GlobalValueProvider(IsBerserk ? 5 : 4), threshold = GlobalValueProvider(IsBerserk ? 7 : 6);
            if (float.IsNaN(range) || float.IsInfinity(range) || float.IsNaN(threshold) ||
                float.IsInfinity(threshold) || threshold < 0f || threshold > 45f)
                    throw new InvalidOperationException("能源球反弹角度必须有限，防轴向阈值必须在0～45度之间。");
        }

        private void StopWithError(Exception e)
        {
            Stop();
            Debug.LogError("能源球停止，请检查配置后调用 Launch 重试：" + e.Message, this);
        }
    }
}
