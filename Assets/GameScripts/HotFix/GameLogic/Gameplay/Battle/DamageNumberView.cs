using UnityEngine;

namespace GameLogic
{
    /// <summary>世界空间伤害数字：0.5 秒内上浮并淡出，结束后由战斗控制器回收。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public sealed class DamageNumberView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 0.5f;
        [SerializeField, Min(0f)] private float riseDistance = 0.35f;
        [SerializeField] private Color color = new Color(1f, 0.3f, 0.15f, 1f);
        private TextMesh _label;
        private Color _initialColor;
        private Vector3 _startPosition;
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

        public void Initialize(int damage, Vector3 position)
        {
            if (damage <= 0) throw new System.ArgumentOutOfRangeException(nameof(damage));
            if (lifetime <= 0f || float.IsNaN(lifetime) || float.IsInfinity(lifetime)) lifetime = 0.5f;
            if (riseDistance < 0f || float.IsNaN(riseDistance) || float.IsInfinity(riseDistance)) riseDistance = 0.35f;
            if (_label == null) Awake();
            _label.text = damage.ToString();
            _initialColor = _label.color;
            _startPosition = position;
            _elapsed = 0f;
            _initialized = true;
            transform.position = position;
            Apply(0f);
        }

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
            transform.position = _startPosition + Vector3.up * (riseDistance * progress);
            var current = _initialColor;
            current.a = _initialColor.a * (1f - progress);
            _label.color = current;
        }
    }
}
