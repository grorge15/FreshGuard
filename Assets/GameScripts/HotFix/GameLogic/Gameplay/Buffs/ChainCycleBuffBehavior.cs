using System;

namespace GameLogic
{
    /// <summary>只在施法上下文选择弹丸，发射事实通知才提交次数。每个被动实例独立计数。</summary>
    public sealed class ChainCycleBuffBehavior : BuffBehavior
    {
        public int AttackCount { get; private set; }
        public override void OnCast(BuffContainer container, BuffObj buff, SkillCastContext context)
        {
            if (context.Request.SkillId != 1605 || !context.Request.Owner.Equals(container.Owner) ||
                container.Owner.Kind != SkillOwnerKind.Entity) return;
            var cycle = Count(context.Parameters, "attackCycleCount");
            var bullet = Count(context.Parameters, "chainBulletId");
            context.CycleBuffInstanceId = buff.InstanceId;
            if (AttackCount + 1 >= cycle) context.BulletId = bullet;
        }
        public override void OnAttackLaunched(BuffContainer container, BuffObj buff, SkillCastSnapshot cast)
        {
            if (cast.SkillId != 1605 || !cast.Owner.Equals(container.Owner) || cast.CycleBuffInstanceId != buff.InstanceId) return;
            AttackCount = (AttackCount + 1) % Count(cast.Parameters, "attackCycleCount");
        }
        private static int Count(System.Collections.Generic.IReadOnlyDictionary<string,float> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value < 1 || value >= int.MaxValue || value != Math.Truncate(value))
                throw new InvalidOperationException("攻击循环参数无效：" + key);
            return (int)value;
        }
    }
}
