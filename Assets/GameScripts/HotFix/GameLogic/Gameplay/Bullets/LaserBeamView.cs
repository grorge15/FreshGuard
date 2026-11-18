using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>按发射快照拉伸聚能激光序列帧；伤害由 BulletState 单次提交。</summary>
    [DisallowMultipleComponent]
    public sealed class LaserBeamView : MonoBehaviour
    {
        [SerializeField] private BattleSpriteAnimation _animation;
        [SerializeField, Min(0.01f)] private float _referenceLength = 3.76f;

        public void Present(BulletState state)
        {
            if (_animation == null || !EntityValidation.IsFiniteNonNegative(_referenceLength) || _referenceLength <= 0f)
                throw new InvalidOperationException("激光表现需要序列帧和有效的原始光束长度。");
            transform.position = state.Action.Origin;
            transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(state.Direction.y, state.Direction.x) * Mathf.Rad2Deg);
            var length = Vector3.Distance(state.Action.Origin, state.BeamEnd);
            _animation.transform.localScale = new Vector3(length / _referenceLength, 1f, 1f);
            _animation.Sample(state.Elapsed);
        }
    }
}
