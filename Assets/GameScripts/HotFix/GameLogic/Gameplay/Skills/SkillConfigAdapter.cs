using System;
using System.Collections.Generic;
using GameConfig;
using GameConfig.bullet;

namespace GameLogic
{
    public static class SkillConfigAdapter
    {
        public static SkillCatalog FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var catalog = new SkillCatalog();
            foreach (var row in tables.TbBullet.DataList)
            {
                ValidateBullet(row);
                catalog.Add(new BulletModel(row.Id, row.Prefab, row.FlightType, row.TrackTarget,
                    row.HitMode, row.Speed, row.FlightTime, row.ArcHeight, row.HitRadius));
            }
            var slowBuffId = 0;
            foreach (var row in tables.TbBuff.DataList)
            {
                catalog.Add(new BuffModel(row.Id, row.BehaviorKey, row.MaxStacks, row.Priority, row.TickInterval));
                if (row.BehaviorKey != "slow") continue;
                if (slowBuffId != 0) throw new InvalidOperationException("当前Slow字段必须对应唯一减速Buff。");
                slowBuffId = row.Id;
            }
            foreach (var row in tables.TbSkill.DataList)
            {
                if (!Enum.IsDefined(typeof(SkillType), row.SkillType) || !Enum.IsDefined(typeof(SkillTargetRule), row.TargetRule))
                    throw new InvalidOperationException("技能类型或目标规则无效：" + row.Id);
                if (row.BulletId < 0 || (row.BulletId != 0 && !catalog.TryGetBullet(row.BulletId, out _)))
                    throw new InvalidOperationException("技能缺少弹丸配置：" + row.Id + "/" + row.BulletId);
                if (row.TimelineKey.StartsWith("robot.", StringComparison.Ordinal) && row.BulletId == 0)
                    throw new InvalidOperationException("机器人投射物技能必须配置弹丸：" + row.Id);
                catalog.Add(new SkillModel(row.Id, (SkillType)row.SkillType, row.TimelineKey,
                    row.MaxLevel, row.Cooldown, (SkillTargetRule)row.TargetRule, row.PassiveBuffIds, row.BulletId));
            }
            foreach (var row in tables.TbSkillLevel.DataList)
            {
                if (!catalog.TryGet(row.SkillId, out var model) || row.Level < 1 || row.Level > model.MaxLevel ||
                    row.Damage < 0 || !EntityValidation.IsFiniteNonNegative(row.Slow) || row.Slow > 1f ||
                    !EntityValidation.IsFiniteNonNegative(row.Duration)) throw new InvalidOperationException("技能等级配置无效：" + row.Id);
                ValidateRobotLevel(row, model, catalog);
                var hitBuffs = new List<CombatBuffApplication>();
                if (row.Slow > 0f)
                {
                    if (slowBuffId == 0 || row.Duration <= 0f) throw new InvalidOperationException("减速技能缺少Buff或持续时间：" + row.Id);
                    hitBuffs.Add(new CombatBuffApplication(slowBuffId, row.Duration,
                        new Dictionary<string, float> { { "slow", row.Slow } }));
                }
                catalog.Add(new SkillLevelModel(row.SkillId, row.Level, new Dictionary<string, float>
                {
                    { "damage", row.Damage }, { "slow", row.Slow }, { "duration", row.Duration },
                    { "scatterCount", row.ScatterCount }, { "scatterAngle", row.ScatterAngle },
                    { "pierceTargetCount", row.PierceTargetCount }, { "burstCount", row.BurstCount },
                    { "burstInterval", row.BurstInterval }, { "knockbackDistance", row.KnockbackDistance }
                }, hitBuffs,
                    parameters => CaptureSlowBuff(slowBuffId, parameters)));
            }
            foreach (var row in tables.TbSkill.DataList)
            {
                for (var level = 1; level <= row.MaxLevel; level++)
                    if (!catalog.TryGetLevel(row.Id, level, out _)) throw new InvalidOperationException("缺少技能等级：" + row.Id + "/" + level);
                foreach (var id in row.PassiveBuffIds)
                    if (!catalog.TryGetBuff(id, out _)) throw new InvalidOperationException("缺少被动Buff：" + id);
            }
            return catalog;
        }

        private static void ValidateBullet(Bullet row)
        {
            var validParabolic = row.FlightType == BulletFlightType.Parabolic && row.TrackTarget &&
                row.HitMode == BulletHitMode.LockedTarget && row.FlightTime > 0f;
            var validStraight = row.FlightType == BulletFlightType.Straight && !row.TrackTarget &&
                (row.HitMode == BulletHitMode.FirstEnemy || row.HitMode == BulletHitMode.PierceEnemies) && row.Speed > 0f;
            if (row.Id <= 0 || string.IsNullOrWhiteSpace(row.Prefab) ||
                !EntityValidation.IsFiniteNonNegative(row.Speed) || !EntityValidation.IsFiniteNonNegative(row.FlightTime) ||
                !EntityValidation.IsFiniteNonNegative(row.ArcHeight) || !EntityValidation.IsFiniteNonNegative(row.HitRadius) ||
                (!validParabolic && !validStraight))
                throw new InvalidOperationException("弹丸配置或轨迹命中组合无效：" + row.Id);
        }

        private static void ValidateRobotLevel(GameConfig.skill.SkillLevel row, SkillModel skill, SkillCatalog catalog)
        {
            if (row.ScatterCount < 0 || row.PierceTargetCount < 0 || row.BurstCount < 0 ||
                !EntityValidation.IsFiniteNonNegative(row.ScatterAngle) || row.ScatterAngle > 360f ||
                !EntityValidation.IsFiniteNonNegative(row.BurstInterval) ||
                !EntityValidation.IsFiniteNonNegative(row.KnockbackDistance))
                throw new InvalidOperationException("技能等级弹丸参数无效：" + row.Id);
            if (skill.TimelineKey == "robot.scatter" && row.ScatterCount < 1)
                throw new InvalidOperationException("散射数量必须至少为1：" + row.Id);
            if (skill.TimelineKey == "robot.burst" && (row.BurstCount < 1 || row.BurstInterval <= 0f))
                throw new InvalidOperationException("连射次数与间隔必须大于0：" + row.Id);
            if (skill.BulletId != 0 && catalog.TryGetBullet(skill.BulletId, out var bullet) &&
                bullet.HitMode == BulletHitMode.PierceEnemies && row.PierceTargetCount < 1)
                throw new InvalidOperationException("穿透人数必须至少为1：" + row.Id);
        }

        private static IReadOnlyList<CombatBuffApplication> CaptureSlowBuff(int buffId, IReadOnlyDictionary<string, float> parameters)
        {
            if (!parameters.TryGetValue("slow", out var slow) || !parameters.TryGetValue("duration", out var duration) ||
                !EntityValidation.IsFiniteNonNegative(slow) || !EntityValidation.IsFiniteNonNegative(duration) ||
                slow <= 0f || duration <= 0f) return Array.Empty<CombatBuffApplication>();
            if (buffId == 0) throw new InvalidOperationException("减速技能缺少Buff配置。");
            return new[] { new CombatBuffApplication(buffId, duration, new Dictionary<string, float> { { "slow", slow } }) };
        }
    }
}
