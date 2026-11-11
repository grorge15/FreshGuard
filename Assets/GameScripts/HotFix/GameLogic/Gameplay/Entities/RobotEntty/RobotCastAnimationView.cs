using UnityEngine;

namespace GameLogic
{
    /// <summary>由战斗时钟采样的施法表现；Timeline结束或取消时恢复原Sprite。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotCastAnimationView : MonoBehaviour
    {
        [SerializeField] private BattleSpriteAnimation _animation;
        private SpriteRenderer _renderer;
        private SkillCastSnapshot _cast;
        private Sprite _original;
        private float _elapsed;
        private float _duration;
        public bool IsPlaying => _cast != null;
        public bool IsPrepared => _animation != null && _animation.Duration > 0f && isActiveAndEnabled;

        internal void Play(SkillCastSnapshot cast, float duration)
        {
            StopCurrent();
            _renderer = _animation.GetComponent<SpriteRenderer>();
            _cast = cast; _original = _renderer.sprite; _elapsed = 0f; _duration = duration;
            Sample();
        }
        internal void Advance(float deltaTime)
        {
            if (_cast == null) return;
            _elapsed = Mathf.Min(_duration, _elapsed + deltaTime);
            Sample();
        }
        private void Sample()
        {
            var visible = _renderer.enabled;
            _animation.Sample(Mathf.Clamp01(_elapsed / _duration) * _animation.Duration);
            _renderer.enabled = visible;
        }
        internal void Stop(SkillCastSnapshot cast)
        { if (ReferenceEquals(_cast, cast)) StopCurrent(); }
        private void StopCurrent()
        {
            if (_cast == null) return;
            if (_renderer != null) _renderer.sprite = _original;
            _cast = null; _original = null;
        }
        private void OnDisable() => StopCurrent();
    }
}
