using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>仅显示命中瞬间的位置快照；寿命由战斗控制器推进，不负责选敌或伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class LightningChainEffectView : MonoBehaviour
    {
        [SerializeField] private LineRenderer _chainLine;
        [SerializeField] private LineRenderer _hitFlash;
        [Min(0.01f)] public float Lifetime = 0.2f;
        private float _elapsed;
        private float _duration;
        private bool _playing;
        private readonly Color _chainColor = new Color(0.12f, 0.65f, 1f, 1f);
        private readonly Color _flashColor = new Color(0.8f, 0.97f, 1f, 1f);

        public void Play(IReadOnlyList<Vector3> positions)
        {
            Clear();
            if (positions == null || positions.Count == 0) return;
            if (_chainLine == null || _hitFlash == null)
                throw new InvalidOperationException("闪电链预制体必须绑定 ChainLine 和 HitFlash。");
            if (float.IsNaN(Lifetime) || float.IsInfinity(Lifetime) || Lifetime <= 0f)
                throw new InvalidOperationException("闪电链表现 Lifetime 必须为有限正数。");
            _chainLine.useWorldSpace = _hitFlash.useWorldSpace = true;
            // 每跳固定折点，端点严格落在已锁定目标位置；不保留调用方列表。
            if (positions.Count > 1)
            {
                _chainLine.positionCount = 1 + (positions.Count - 1) * 4;
                _chainLine.SetPosition(0, positions[0]);
                var index = 1;
                for (var target = 1; target < positions.Count; target++)
                {
                    var from = positions[target - 1];
                    var to = positions[target];
                    var direction = to - from;
                    var normal = new Vector3(-direction.y, direction.x, 0f).normalized;
                    var offset = Mathf.Min(0.035f, direction.magnitude * 0.1f);
                    for (var point = 1; point <= 4; point++)
                    {
                        var position = point == 4 ? to : Vector3.Lerp(from, to, point / 4f) +
                            normal * (point % 2 == 0 ? -offset : offset);
                        _chainLine.SetPosition(index++, position);
                    }
                }
                _chainLine.enabled = true;
            }
            // 首名即使没有可连锁目标，也以 XY 平面的八角星芒反馈命中。
            _hitFlash.positionCount = 17;
            for (var i = 0; i <= 16; i++)
            {
                var angle = i * Mathf.PI / 8f;
                var radius = i % 2 == 0 ? 0.13f : 0.035f;
                _hitFlash.SetPosition(i, positions[0] + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            _hitFlash.enabled = true;
            _duration = Lifetime;
            _playing = true;
            PresentAlpha(1f);
        }

        /// <returns>表现已到期或未播放时返回 true，调用方可立即回收。</returns>
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (!_playing) return true;
            _elapsed += deltaTime;
            if (_elapsed >= _duration) { Clear(); return true; }
            PresentAlpha(1f - _elapsed / _duration);
            return false;
        }

        public void Clear()
        {
            _playing = false;
            _elapsed = _duration = 0f;
            if (_chainLine != null) { _chainLine.enabled = false; _chainLine.positionCount = 0; }
            if (_hitFlash != null) { _hitFlash.enabled = false; _hitFlash.positionCount = 0; }
            PresentAlpha(1f);
        }

        private void PresentAlpha(float alpha)
        {
            var chain = _chainColor;
            chain.a = alpha;
            var flash = _flashColor;
            flash.a = alpha;
            if (_chainLine != null) _chainLine.startColor = _chainLine.endColor = chain;
            if (_hitFlash != null) _hitFlash.startColor = _hitFlash.endColor = flash;
        }

        private void OnDisable() { Clear(); }
    }
}
