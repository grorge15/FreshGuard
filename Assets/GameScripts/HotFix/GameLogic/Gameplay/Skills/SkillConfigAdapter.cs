using System;
using System.Collections.Generic;
using GameConfig;

namespace GameLogic
{
    public static class SkillConfigAdapter
    {
        public static SkillCatalog FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var catalog = new SkillCatalog();
            foreach (var row in tables.TbSkill.DataList)
            {
                if (!Enum.IsDefined(typeof(SkillType), row.SkillType) || !Enum.IsDefined(typeof(SkillTargetRule), row.TargetRule))
                    throw new InvalidOperationException("技能类型或目标规则无效：" + row.Id);
                catalog.Add(new SkillModel(row.Id, (SkillType)row.SkillType, row.TimelineKey,
                    row.MaxLevel, row.Cooldown, (SkillTargetRule)row.TargetRule, row.PassiveBuffIds));
            }
            foreach (var row in tables.TbSkillLevel.DataList)
            {
                if (!catalog.TryGet(row.SkillId, out var model) || row.Level < 1 || row.Level > model.MaxLevel ||
                    row.Damage < 0 || !EntityValidation.IsFiniteNonNegative(row.Slow) || row.Slow > 1f ||
                    !EntityValidation.IsFiniteNonNegative(row.Duration)) throw new InvalidOperationException("技能等级配置无效：" + row.Id);
                catalog.Add(new SkillLevelModel(row.SkillId, row.Level, new Dictionary<string, float>
                { { "damage", row.Damage }, { "slow", row.Slow }, { "duration", row.Duration } }));
            }
            foreach (var row in tables.TbBuff.DataList)
                catalog.Add(new BuffModel(row.Id, row.BehaviorKey, row.MaxStacks, row.Priority, row.TickInterval));
            foreach (var row in tables.TbSkill.DataList)
            {
                for (var level = 1; level <= row.MaxLevel; level++)
                    if (!catalog.TryGetLevel(row.Id, level, out _)) throw new InvalidOperationException("缺少技能等级：" + row.Id + "/" + level);
                foreach (var id in row.PassiveBuffIds)
                    if (!catalog.TryGetBuff(id, out _)) throw new InvalidOperationException("缺少被动Buff：" + id);
            }
            return catalog;
        }
    }
}
