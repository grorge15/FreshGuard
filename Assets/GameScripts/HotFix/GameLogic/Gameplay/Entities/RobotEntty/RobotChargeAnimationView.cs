using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只读取周期被动的次数；由战斗时钟循环采样当前段，不驱动发射。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotChargeAnimationView : MonoBehaviour
    {
        [SerializeField] private BattleSpriteAnimation _animation;
        [SerializeField] private SpriteRenderer _portraitRenderer;
        private SpriteRenderer _renderer;
        private BattleRuntime _battle;
        private RobotEntity _robot;
        private RobotCastAnimationView _castAnimation;
        private bool _visible;
        private int _stage = -1;
        private int _level;
        private int _required;
        private double _elapsed;

        internal void Bind(BattleRuntime battle, RobotEntity robot, RobotCastAnimationView castAnimation)
        {
            if (ReferenceEquals(_battle, battle) && ReferenceEquals(_robot, robot)) return;
            Clear();
            _battle = battle; _robot = robot; _castAnimation = castAnimation;
            _renderer = _animation != null ? _animation.GetComponent<SpriteRenderer>() : null;
            _visible = robot != null && robot.CanParticipate;
            Refresh();
        }

        internal void SetVisible(bool visible) { _visible = visible; Refresh(); }
        internal void Refresh() => Advance(0f);

        internal void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            var clip = _animation != null ? _animation.Clip : null;
            if (!_visible || !isActiveAndEnabled || _robot == null || !_robot.CanParticipate ||
                _battle == null || _battle.IsDisposed || !_battle.TryResolveOwner(_robot.OwnerRef, out var owner) ||
                !ReferenceEquals(owner, _robot) || clip == null || clip.length <= 0f || _renderer == null ||
                !_battle.Catalog.TryGetLevel(_robot.SkillId, _robot.Level, out var level) ||
                !level.Parameters.TryGetValue("attackCycleCount", out var count) || count < 1 || count >= int.MaxValue ||
                count != Math.Truncate(count))
            { Hide(); return; }
            AttackCycleBuffBehavior cycle = null;
            foreach (var buff in _robot.Buffs.Items)
                if (buff.Model.CountSkillId == _robot.SkillId && buff.Behavior is AttackCycleBuffBehavior candidate)
                { cycle = candidate; break; }
            if (cycle == null) { Hide(); return; }
            var required = (int)count;
            var stage = cycle.AttackCount % required;
            if (stage != _stage || _level != _robot.Level || _required != required)
            { _stage = stage; _level = _robot.Level; _required = required; _elapsed = 0d; }
            if (_castAnimation != null && _castAnimation.IsPlaying) { Hide(); return; }

            // 按完整帧划分，避免同一边界帧在相邻阶段反复出现。
            var frames = Math.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            var start = (int)((long)frames * stage / required);
            var end = (int)((long)frames * (stage + 1) / required);
            var segmentFrames = Math.Max(1, end - start);
            var duration = Math.Min(segmentFrames / (double)clip.frameRate,
                _robot.Configuration.MinTriggerInterval * 0.9d);
            _elapsed = (_elapsed + deltaTime) % duration;
            var frame = Math.Min(frames - 1, start + Math.Min(segmentFrames - 1, (int)(_elapsed / duration * segmentFrames)));
            _animation.Sample(frame / clip.frameRate);
            _renderer.enabled = true;
            if (_portraitRenderer != null) _portraitRenderer.enabled = false;
        }

        internal void Suspend() => Hide();
        private void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
            // 战斗绑定之前、组件停用之后，仍允许 RobotView 显示静态立绘。
            if (_portraitRenderer != null) _portraitRenderer.enabled = _visible &&
                (_robot == null || _robot.CanParticipate);
        }
        internal void Clear()
        {
            Hide();
            _battle = null; _robot = null; _castAnimation = null;
            _stage = -1; _level = _required = 0; _elapsed = 0d;
        }
        private void OnDisable() => Hide();
    }
}
