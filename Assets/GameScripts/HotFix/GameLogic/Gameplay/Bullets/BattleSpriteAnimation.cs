using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>由战斗时间采样 Sprite 动画，不依赖 Animator 的自动时钟。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
    public sealed class BattleSpriteAnimation : MonoBehaviour
    {
        [SerializeField] private AnimationClip _clip;
        [SerializeField] private bool _loop;
        private SpriteRenderer _renderer;
        public float Duration => _clip != null ? _clip.length : 0f;

        public void Sample(float elapsed)
        {
            EntityValidation.RequireFiniteNonNegative(elapsed, nameof(elapsed));
            if (_clip == null || _clip.length <= 0f)
                throw new InvalidOperationException("序列帧表现需要有效的 AnimationClip。");
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            var animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            var time = _loop ? Mathf.Repeat(elapsed, _clip.length) : Mathf.Min(elapsed, _clip.length);
            _clip.SampleAnimation(gameObject, time);
            _renderer.enabled = true;
        }

        public void Clear()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
