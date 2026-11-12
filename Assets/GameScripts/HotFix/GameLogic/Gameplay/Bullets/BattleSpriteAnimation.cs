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
        public AnimationClip Clip => _clip;

        public void Sample(float elapsed)
            => SampleClip(_clip, elapsed, _loop);

        public void Sample(AnimationClip clip, float elapsed)
            => SampleClip(clip, elapsed, false);

        private void SampleClip(AnimationClip clip, float elapsed, bool loop)
        {
            EntityValidation.RequireFiniteNonNegative(elapsed, nameof(elapsed));
            if (clip == null || clip.length <= 0f)
                throw new InvalidOperationException("序列帧表现需要有效的 AnimationClip。");
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            var animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            var time = loop ? Mathf.Repeat(elapsed, clip.length) : Mathf.Min(elapsed, clip.length);
            clip.SampleAnimation(gameObject, time);
            _renderer.enabled = true;
        }

        public void Clear()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
