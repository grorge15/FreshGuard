using System;
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
        private GameConfig.globalcfg.TbGlobal _globalTable;
        public Func<int, float> GlobalValueProvider { get; set; }
        public Vector2 CurrentDirection => _direction;
        public float Speed => speed;
        public int BounceCount { get; private set; }

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
        private void OnDisable() { _launched = false; if (_body != null) _body.velocity = Vector2.zero; }
        private void FixedUpdate() { if (_launched) _body.velocity = _direction * speed; }

        public void Launch()
        {
            if (_body == null) return;
            try
            {
                ValidateConfig();
                if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f || radius <= 0f)
                    throw new InvalidOperationException("速度与半径必须为正数。");
                if (float.IsNaN(initialAngleMin) || float.IsNaN(initialAngleMax) ||
                    float.IsInfinity(initialAngleMin) || float.IsInfinity(initialAngleMax))
                    throw new InvalidOperationException("发射角必须是有限角度。");
                float rad = UnityEngine.Random.Range(Mathf.Min(initialAngleMin, initialAngleMax),
                    Mathf.Max(initialAngleMin, initialAngleMax)) * Mathf.Deg2Rad;
                _direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                _body.position = spawnPosition;
                _body.velocity = _direction * speed;
                BounceCount = 0;
                _launched = true;
            }
            catch (Exception e) { StopWithError(e); }
        }

        private void OnCollisionEnter2D(Collision2D collision) { Bounce(collision); }
        private void OnCollisionStay2D(Collision2D collision) { Bounce(collision); }
        private void Bounce(Collision2D collision)
        {
            if (!_launched) return;
            try
            {
                ValidateConfig();
                for (int i = 0; i < collision.contactCount; i++)
                {
                    Vector2 normal = collision.GetContact(i).normal.normalized;
                    if (Vector2.Dot(_direction, normal) >= -0.00001f) continue;
                    float range = Mathf.Abs(GlobalValueProvider(4));
                    _direction = BallBounceMath.Resolve(_direction, normal, UnityEngine.Random.Range(-range, range),
                        GlobalValueProvider(6), UnityEngine.Random.value < 0.5f);
                    BounceCount++;
                }
                _body.velocity = _direction * speed;
            }
            catch (Exception e) { StopWithError(e); }
        }

        private void ValidateConfig()
        {
            float range = GlobalValueProvider(4), threshold = GlobalValueProvider(6);
            if (float.IsNaN(range) || float.IsInfinity(range) || float.IsNaN(threshold) ||
                float.IsInfinity(threshold) || threshold < 0f || threshold > 45f)
                throw new InvalidOperationException("Global 4 必须有限；Global 6 必须在 0～45 度之间。");
        }

        private void StopWithError(Exception e)
        {
            _launched = false;
            _body.velocity = Vector2.zero;
            Debug.LogError("能源球停止，请检查配置后调用 Launch 重试：" + e.Message, this);
        }
    }
}
