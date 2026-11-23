using System;
using UnityEngine;

namespace GameLogic
{
    [DisallowMultipleComponent]
    public sealed class StaffView : MonoBehaviour
    {
        [SerializeField] private Transform _visual;
        [SerializeField] private BattleSpriteAnimation _animation;
        private Vector3 _baseScale;
        private Quaternion _authoredRotation;
        private bool _initialized;
        public StaffState State { get; private set; }
        public void Initialize(StaffState state, BattleRouteGeometry route)
        {
            if (_visual == null || _animation == null) throw new InvalidOperationException("StaffView缺少Visual或StaffRoll动画。");
            if (!_initialized) { _baseScale = _visual.localScale; _authoredRotation = _visual.localRotation; _initialized = true; }
            _visual.localRotation = _authoredRotation;
            _visual.localScale = _baseScale;
            State = state;
            // 美术横放后的局部X尺寸作为宽度，保持原始长宽比例。
            var renderer = _visual.GetComponent<SpriteRenderer>();
            _animation.Sample(0);
            var bounds = renderer.sprite.bounds;
            var size = _authoredRotation * Vector3.Scale(bounds.size, _baseScale);
            var authoredWidth = Mathf.Abs(size.x);
            if (authoredWidth <= 0.000001f) throw new InvalidOperationException("Staff外观宽度必须大于0。");
            _displayScale = _baseScale * (state.Config.Width / authoredWidth);
            Render(route);
        }
        private Vector3 _displayScale;
        public void Render(BattleRouteGeometry route)
        {
            if (State == null) return;
            transform.position = route.Position(State.Progress);
            var tangent = route.Tangent(State.Progress);
            // 局部X为横向，局部Y为道路前进方向。
            transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - 90);
            _animation.Sample(State.Phase == StaffPhase.Appearing ? 0 : State.RollingElapsed);
            _visual.localScale = _displayScale * State.AppearScale;
        }
        public void Clear() { State = null; if (_animation != null) _animation.Clear(); }
    }
}
