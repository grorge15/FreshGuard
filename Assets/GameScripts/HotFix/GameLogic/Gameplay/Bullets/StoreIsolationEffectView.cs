using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>由装盒状态和战斗时长采样，关盒末帧保持到最后一个来源消失。</summary>
    [DisallowMultipleComponent]
    public sealed class StoreIsolationEffectView : MonoBehaviour
    {
        [SerializeField] private BattleSpriteAnimation _animation;
        [SerializeField] private AnimationClip _close;
        [SerializeField] private AnimationClip _open;
        private SpriteRenderer[] _renderers;
        private Vector3 _baseScale;
        private Bounds _closedSpriteBounds;
        private bool _hasReferenceSize;
        private const float ENEMY_SIZE_PADDING = 1.1f;
        public bool IsValid => _animation != null && _close != null && _close.length > 0f &&
            _open != null && _open.length > 0f && !_close.isLooping && !_open.isLooping;

        public bool SampleState(bool isolated, float elapsed)
        {
            EntityValidation.RequireFiniteNonNegative(elapsed, nameof(elapsed));
            if (!IsValid) throw new InvalidOperationException("装盒表现需要关盒、开盒非循环动画及采样器。");
            if (!isolated && elapsed >= _open.length) { Clear(); return true; }
            _animation.Sample(isolated ? _close : _open, elapsed);
            return false;
        }
        internal void Follow(EnemyView target)
        {
            if (!_hasReferenceSize)
            {
                _baseScale = transform.localScale;
                _animation.Sample(_close, _close.length);
                _closedSpriteBounds = _animation.GetComponent<SpriteRenderer>().sprite.bounds;
                _hasReferenceSize = true;
            }
            var targetBounds = target.GetVisualBounds();
            // 每次从原比例计算，兼容池复用和父节点缩放，避免逐帧乘大或开合帧改变大小。
            transform.localScale = _baseScale;
            var right = _animation.transform.TransformVector(Vector3.right * _closedSpriteBounds.size.x);
            var up = _animation.transform.TransformVector(Vector3.up * _closedSpriteBounds.size.y);
            var width = Mathf.Abs(right.x) + Mathf.Abs(up.x);
            var height = Mathf.Abs(right.y) + Mathf.Abs(up.y);
            if (width > 0f && height > 0f)
                transform.localScale = _baseScale * Mathf.Max(targetBounds.size.x * ENEMY_SIZE_PADDING / width,
                    targetBounds.size.y * ENEMY_SIZE_PADDING / height);
            transform.position += targetBounds.center - _animation.transform.TransformPoint(_closedSpriteBounds.center);
            target.GetVisualSorting(out var layer, out var order);
            if (_renderers == null) _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var renderer in _renderers)
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = order + 1;
            }
        }
        public void Clear() { if (_animation != null) _animation.Clear(); }
        private void OnDisable() => Clear();
    }
}
