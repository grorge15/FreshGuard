using UnityEngine;

namespace GameLogic
{
    /// <summary>怪物预制体自带的通用受击序列帧，由 EnemyView 统一推进和回收。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class EnemyHitEffect : MonoBehaviour
    {
        [SerializeField] private Sprite[] _frames;
        [SerializeField, Min(1f)] private float _framesPerSecond = 32f;
        [SerializeField, Min(0.1f)] private float _visualSize = 1.65f;
        private SpriteRenderer _renderer;
        private float _elapsed;

        public bool IsPlaying { get; private set; }
        public float Duration => _frames != null && _framesPerSecond > 0f ? _frames.Length / _framesPerSecond : 0f;

        private void Awake() { Clear(); }
        private void OnDisable() { Clear(); }

        public void Play(Bounds bodyBounds, int sortingLayer, int sortingOrder)
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_frames == null || _frames.Length == 0 || _frames[0] == null || _framesPerSecond <= 0f) return;
            // 特效挂在角色根节点下，与身体抖动分离，不参与角色包围盒计算。
            transform.position = bodyBounds.center + new Vector3(bodyBounds.size.x * 0.04f, bodyBounds.size.y * 0.015f, 0f);
            transform.rotation = Quaternion.identity;
            var spriteSize = Mathf.Max(0.001f, _frames[0].bounds.size.y);
            var worldSize = Mathf.Max(bodyBounds.size.x, bodyBounds.size.y) * _visualSize;
            var parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(worldSize / spriteSize / Mathf.Max(0.001f, Mathf.Abs(parentScale.x)),
                worldSize / spriteSize / Mathf.Max(0.001f, Mathf.Abs(parentScale.y)), 1f);
            SetSorting(sortingLayer, sortingOrder);
            _elapsed = 0f;
            _renderer.sprite = _frames[0];
            _renderer.color = Color.white;
            _renderer.enabled = true;
            IsPlaying = true;
        }

        public void Advance(float deltaTime)
        {
            if (!IsPlaying || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _elapsed += deltaTime;
            var frame = Mathf.FloorToInt(_elapsed * _framesPerSecond);
            if (frame >= _frames.Length) { Clear(); return; }
            _renderer.sprite = _frames[frame];
        }

        public void SetSorting(int layer, int order)
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            _renderer.sortingLayerID = layer;
            _renderer.sortingOrder = Mathf.Clamp(order + 2, short.MinValue, short.MaxValue);
        }

        public void Clear()
        {
            IsPlaying = false;
            _elapsed = 0f;
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null) { _renderer.enabled = false; _renderer.sprite = null; }
        }
    }
}
