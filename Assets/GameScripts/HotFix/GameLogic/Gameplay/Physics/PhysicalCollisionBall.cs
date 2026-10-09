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
        [SerializeField] private bool launchOnEnable = true;
        [Tooltip("Luban 生成的 Global 二进制；作为 Prefab 依赖加载，不在碰撞回调中进行 IO。")]
        [SerializeField] private TextAsset globalConfig;
        private Rigidbody2D _body;
        private Vector2 _direction;
        private bool _launched;
        private bool _frozen;
        private bool _dragging;
        private Func<bool> _runningGate;
        private readonly HashSet<int> _glassHitsThisStep = new HashSet<int>();
        private GameConfig.globalcfg.TbGlobal _globalTable;
        public Func<int, float> GlobalValueProvider { get; set; }
        public Vector2 CurrentDirection => _direction;
        public float Speed => speed;
        public float EffectiveSpeed
        {
            get
            {
                return !_dragging || IsBerserk ? speed : speed * ReadDraggingMultiplier();
            }
        }
        public int BounceCount { get; private set; }
        public BattleSide Side { get; private set; }
        public long BattleId { get; private set; }
        public bool IsBerserk { get; private set; }
        public bool IsLaunched => _launched;
        public float SecondsSinceEffectiveInteraction { get; private set; }
        public event Action<PhysicalCollisionBall> EffectiveInteraction;

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
                if (_launched && !_frozen && _runningGate != null && !_runningGate()) { Stop(); return; }
                if (_launched && !_frozen && (_runningGate == null || _runningGate()))
                {
                    SecondsSinceEffectiveInteraction += Time.fixedDeltaTime;
                    _body.velocity = _direction * EffectiveSpeed;
                }
            }
            catch (Exception e) { StopWithError(e); }
        }

        public void BindBattle(long battleId, BattleSide side, Func<bool> runningGate)
        {
            Stop();
            if (battleId <= 0 || runningGate == null) throw new ArgumentException("能源球缺少有效对局绑定。");
            EntityValidation.RequireSide(side);
            BattleId = battleId;
            Side = side;
            _runningGate = runningGate;
            IsBerserk = false;
            _frozen = false;
            SecondsSinceEffectiveInteraction = 0f;
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
            IsBerserk = berserk;
            ApplyEffectiveVelocity();
        }

        public void SetFrozen(bool frozen)
        {
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
            _glassHitsThisStep.Clear();
            if (_body != null) { _body.velocity = Vector2.zero; _body.simulated = false; }
        }

        public void Launch()
        {
            float angle = UnityEngine.Random.Range(Mathf.Min(initialAngleMin, initialAngleMax), Mathf.Max(initialAngleMin, initialAngleMax));
            LaunchAt(spawnPosition, angle);
        }

        public void LaunchAt(Vector2 position, float angle)
        {
            if (_body == null || _frozen || (_runningGate != null && !_runningGate())) return;
            try
            {
                ValidateConfig();
                if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f || radius <= 0f)
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
                _body.velocity = _direction * EffectiveSpeed;
                BounceCount = 0;
                SecondsSinceEffectiveInteraction = 0f;
                _glassHitsThisStep.Clear();
                _launched = true;
            }
            catch (Exception e) { StopWithError(e); }
        }

        private void OnCollisionEnter2D(Collision2D collision) { Bounce(collision); }
        private void OnCollisionStay2D(Collision2D collision) { Bounce(collision); }
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
