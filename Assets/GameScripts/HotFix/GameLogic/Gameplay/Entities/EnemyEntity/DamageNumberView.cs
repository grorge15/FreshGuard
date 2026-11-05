using UnityEngine;

namespace GameLogic
{
    /// <summary>世界空间伤害数字：先弯出再向棋盘外侧飘移并淡出，由战斗控制器回收。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public sealed class DamageNumberView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 0.5f;
        [SerializeField, Min(0f)] private float riseDistance = 0.35f;
        [SerializeField, Min(0f)] private float driftDistance = 0.75f;
        [SerializeField] private Color color = new Color(1f, 0.3f, 0.15f, 1f);
        private static readonly System.Random MotionRandom = new System.Random();
        private TextMesh _label;
        private Color _initialColor;
        private Vector3 _startPosition;
        private Vector3 _firstControlOffset;
        private Vector3 _secondControlOffset;
        private Vector3 _endOffset;
        private float _elapsed;
        private bool _initialized;

        public float Lifetime => lifetime;
        public float Elapsed => _elapsed;

        private void Awake()
        {
            _label = GetComponent<TextMesh>();
            if (_label.font == null) _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.fontSize = 32;
            _label.characterSize = 0.08f;
            _label.color = color;
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (_label.font != null) renderer.sharedMaterial = _label.font.material;
                renderer.sortingOrder = short.MaxValue;
            }
        }

        public void Initialize(int damage, Vector3 position, Vector2 outwardDirection)
        {
            if (damage <= 0) throw new System.ArgumentOutOfRangeException(nameof(damage));
            if (lifetime <= 0f || float.IsNaN(lifetime) || float.IsInfinity(lifetime)) lifetime = 0.5f;
            if (riseDistance < 0f || float.IsNaN(riseDistance) || float.IsInfinity(riseDistance)) riseDistance = 0.35f;
            if (driftDistance < 0f || float.IsNaN(driftDistance) || float.IsInfinity(driftDistance)) driftDistance = 0.75f;
            if (_label == null) Awake();
            _label.text = damage.ToString();
            _initialColor = _label.color;
            var outward = outwardDirection.sqrMagnitude > 0.0001f ? outwardDirection.normalized : Vector2.left;
            // 侧边轨道向上弯出；上下轨道旋转同一条弧线，终段始终朝棋盘外侧。
            var bend = new Vector2(-outward.y, outward.x);
            if (bend.y < 0f) bend = -bend;
            // 每次出字只采样一次，保持整条轨迹平滑；独立随机源不干扰敌人路线等战斗随机。
            _startPosition = position + (Vector3)(outward * RandomRange(-0.02f, 0.02f) +
                bend * RandomRange(-0.025f, 0.025f));
            var drift = driftDistance * RandomRange(0.88f, 1.12f);
            var rise = riseDistance * RandomRange(0.85f, 1.15f);
            _firstControlOffset = outward * (drift * 0.08f) + bend * (rise * RandomRange(0.78f, 0.92f));
            _secondControlOffset = outward * (drift * RandomRange(0.4f, 0.5f)) + bend * rise;
            _endOffset = outward * drift + bend * rise;
            _elapsed = 0f;
            _initialized = true;
            transform.position = _startPosition;
            Apply(0f);
        }

        private static float RandomRange(float min, float max) => Mathf.Lerp(min, max, (float)MotionRandom.NextDouble());

        /// <returns>true when the number has finished and may be destroyed.</returns>
        public bool Advance(float deltaTime)
        {
            if (!_initialized) return true;
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(deltaTime));
            _elapsed = Mathf.Min(lifetime, _elapsed + deltaTime);
            Apply(_elapsed / lifetime);
            return _elapsed >= lifetime;
        }

        private void Apply(float progress)
        {
            progress = Mathf.Clamp01(progress);
            var inverse = 1f - progress;
            transform.position = _startPosition +
                3f * inverse * inverse * progress * _firstControlOffset +
                3f * inverse * progress * progress * _secondControlOffset +
                progress * progress * progress * _endOffset;
            var current = _initialColor;
            current.a = _initialColor.a * (1f - progress);
            _label.color = current;
        }
    }
}
