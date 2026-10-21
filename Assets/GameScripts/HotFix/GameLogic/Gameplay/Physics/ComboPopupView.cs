using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>从固定世界位置上浮的Combo标识，生命周期由所属球推进和回收。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public sealed class ComboPopupView : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float lifetime = 0.5f;
        [SerializeField, Min(0f)] private float riseDistance = 0.35f;
        [SerializeField] private Color[] colors = {
            Color.white, new Color(0.37f, 0.78f, 0.42f), new Color(0.28f, 0.57f, 0.95f),
            new Color(0.70f, 0.39f, 0.88f), new Color(1f, 0.76f, 0.20f) };
        private TextMesh _label;
        private Vector3 _origin;
        private Color _color;
        private float _elapsed;
        private bool _initialized;

        public void Initialize(int combo, Vector3 position)
        {
            if (combo < 10 || combo > 50 || combo % 10 != 0) throw new ArgumentOutOfRangeException(nameof(combo));
            if (colors == null || colors.Length != 5 || lifetime <= 0f || float.IsNaN(lifetime) ||
                float.IsInfinity(lifetime) || riseDistance < 0f || float.IsNaN(riseDistance) || float.IsInfinity(riseDistance))
                throw new InvalidOperationException("Combo提示参数无效。");
            _label = GetComponent<TextMesh>();
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.fontSize = 32;
            _label.characterSize = 0.08f;
            GetComponent<MeshRenderer>().sortingOrder = 1000;
            _label.text = combo + " Combo";
            _color = colors[combo / 10 - 1];
            _origin = position;
            _elapsed = 0f;
            _initialized = true;
            Apply(0f);
        }

        public bool Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (!_initialized) return true;
            _elapsed = Mathf.Min(lifetime, _elapsed + deltaTime);
            Apply(_elapsed / lifetime);
            return _elapsed >= lifetime;
        }

        private void Apply(float progress)
        {
            transform.position = _origin + Vector3.up * (riseDistance * progress);
            var color = _color;
            color.a *= 1f - progress;
            _label.color = color;
        }
    }
}
