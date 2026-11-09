using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    [DisallowMultipleComponent]
    public sealed class JuiceLandingEffectView : MonoBehaviour, IBattleEffectView
    {
        [SerializeField] private BattleSpriteAnimation _animation;
        private float _elapsed;
        private bool _playing;
        public void Play(IReadOnlyList<Vector3> positions)
        {
            Clear();
            if (_animation == null || _animation.Duration <= 0f || positions == null || positions.Count != 1)
                throw new InvalidOperationException("落地效果需要序列帧及一个固定位置。");
            transform.SetPositionAndRotation(positions[0], Quaternion.identity);
            _animation.Sample(0f);
            _playing = true;
        }
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!_playing) return true;
            _elapsed += deltaTime;
            if (_elapsed >= _animation.Duration) { Clear(); return true; }
            _animation.Sample(_elapsed);
            return false;
        }
        public void Clear() { _elapsed = 0; _playing = false; if (_animation != null) _animation.Clear(); }
        private void OnDisable() => Clear();
    }
}
