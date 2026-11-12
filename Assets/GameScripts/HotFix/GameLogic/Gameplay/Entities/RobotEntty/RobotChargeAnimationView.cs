using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>只读取周期被动的次数；每次推进一段并停在段末，零次保持初始帧，不驱动发射。</summary>
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
            // 0 是未播放；第 1 次才播放第 1 段，满次数仍保留最后一段。
            var stage = cycle.AttackCount > 0 ? (cycle.AttackCount - 1) % required + 1 : 0;
            var configurationChanged = _level != _robot.Level || _required != required;
            if (_castAnimation != null && _castAnimation.IsPlaying)
            {
                // 施法动画接管表现，结束后按最新次数恢复，不能补播上一轮的蓄力。
                _stage = stage; _level = _robot.Level; _required = required; _elapsed = 0d;
                Hide(); return;
            }
            // 子技能会立即消费次数；先播完已经开始的最后一段，再回到初始帧。
            // 升级、配置变化则立即重置，不沿用旧进度。
            var completingCycle = !configurationChanged && stage == 0 && _stage == required;
            if (configurationChanged || (stage != _stage && !completingCycle))
            { _stage = stage; _level = _robot.Level; _required = required; _elapsed = 0d; }

            var frames = Math.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
            var frame = 0;
            if (_stage > 0)
            {
                // 相邻段共享边界姿态：次数不变时夹在段末，不再取余循环。
                var start = (int)((long)(frames - 1) * (_stage - 1) / required);
                var end = (int)((long)(frames - 1) * _stage / required);
                var duration = Math.Min(Math.Max(1, end - start) / (double)clip.frameRate,
                    _robot.Configuration.MinTriggerInterval * 0.9d);
                _elapsed = Math.Min(duration, _elapsed + deltaTime);
                frame = start + (int)(_elapsed / duration * (end - start));
                if (completingCycle && _elapsed >= duration)
                { _stage = 0; _elapsed = 0d; frame = 0; }
            }
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
