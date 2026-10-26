using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>现有Prefab的表现适配；轨迹和命中由独立BulletState推进。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotBulletView : MonoBehaviour
    {
        public BulletState State { get; private set; }
        public long BattleId => Action?.BattleId ?? 0;
        public BattleSide Side => Action?.SourceSide ?? default(BattleSide);
        public int TargetInstanceId => Action?.TargetInstanceIds[0] ?? 0;
        public int Damage => Action?.LockedDamage ?? 0;
        public long Sequence => Action?.Sequence ?? 0;
        public float Elapsed => State?.Elapsed ?? 0f;
        public bool IsResolved => State == null || State.IsResolved;
        public CombatActionSnapshot Action => State?.Action;
        public void Bind(BulletState state)
        { State = state ?? throw new ArgumentNullException(nameof(state)); Present(state); }
        public void Present(BulletState state)
        { if (this != null) transform.position = state.Position; }
        public void Initialize(BattleShopSceneController host, BattleShopContext context, EnemyBattleController enemies,
            CombatActionSnapshot action, float flightTime, float arcHeight)
        {
            if (host == null || context == null || enemies == null) throw new ArgumentNullException(nameof(context));
            if (action == null || action.BattleId != context.BattleId) throw new ArgumentException(nameof(action));
            Bind(new BulletState(new BulletLauncher(new BulletModel(flightTime, arcHeight), action),
                new EnemyBulletWorld(host, context, enemies), state => context.Combat.Resolve(state.Action)));
        }
        public bool Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            if (State == null) return true;
            var ended = State.Advance(deltaTime); Present(State); return ended;
        }
    }
}
