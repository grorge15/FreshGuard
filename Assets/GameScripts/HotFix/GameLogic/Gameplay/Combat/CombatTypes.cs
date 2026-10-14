using System;

namespace GameLogic
{
    [Flags]
    public enum CombatOutcomeTag
    {
        None = 0,
        SingleDamage = 1 << 0,
        AreaDamage = 1 << 1,
        DamageOverTime = 1 << 2,
        Slow = 1 << 3,
        Knockback = 1 << 4,
        Pull = 1 << 5,
        Energy = 1 << 6
    }

    // 同组加法、异组乘法。新业务可添加分组，不改变攻击和投射物流转。
    public enum CombatModifierGroup { GlobalDamage, AreaDamage, SkillDamage, TargetCondition, Equipment }
    public enum CombatSourceKind { Robot, Enemy, Other }
    public enum CombatModifierPhase { Capture, Resolution }
}
