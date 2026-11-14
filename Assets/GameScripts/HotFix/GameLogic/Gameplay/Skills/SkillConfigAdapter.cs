using System;
using System.Collections.Generic;
using GameConfig;
using GameConfig.bullet;

namespace GameLogic
{
    public static class SkillConfigAdapter
    {
        // 配置等级与实例叠层分别处理，升级不能增加同一实例的叠层上限。
        private const int RuntimeBuffMaxStacks = 1;

        public static SkillCatalog FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var catalog = new SkillCatalog();
            var enabledSkills = new HashSet<int>();
            var requiredBullets = new HashSet<int>();
            var requiredBuffs = new HashSet<int>();
            foreach (var row in tables.TbSkill.DataList)
            {
                if (!row.Enabled) continue;
                enabledSkills.Add(row.Id);
                if (row.BulletId != 0) requiredBullets.Add(row.BulletId);
                foreach (var id in row.PassiveBuffIds) requiredBuffs.Add(id);
            }
            var needsSlow = false;
            var needsPoison = false;
            var needsIsolation = false;
            foreach (var row in tables.TbSkillLevel.DataList)
            {
                if (!enabledSkills.Contains(row.SkillId)) continue;
                needsSlow |= row.Slow > 0f;
                needsPoison |= HasPoison(row);
                needsIsolation |= row.IsolateDuration > 0f;
            }
            foreach (var row in tables.TbBullet.DataList)
            {
                if (!requiredBullets.Contains(row.Id)) continue;
                ValidateBullet(row);
                catalog.Add(new BulletModel(row.Id, row.Prefab, row.FlightType, row.TrackTarget,
                    row.HitMode, row.Speed, row.FlightTime, row.ArcHeight, row.HitRadius,
                    row.BehaviorKey, row.HitEffectPrefab));
            }
            foreach (var id in requiredBullets)
                if (!catalog.TryGetBullet(id, out _)) throw new InvalidOperationException("缺少启用技能依赖的弹丸：" + id);
            var slowBuffId = 0;
            var poisonBuffId = 0;
            var isolationBuffId = 0;
            foreach (var row in tables.TbBuff.DataList)
            {
                if (!requiredBuffs.Contains(row.Id) && !(needsSlow && row.BehaviorKey == "slow") &&
                    !(needsPoison && row.BehaviorKey == "poison") &&
                    !(needsIsolation && row.BehaviorKey == "store_isolation")) continue;
                if (row.MaxLevel < 1)
                    throw new InvalidOperationException("Buff最高等级至少为1：" + row.Id);
                if (row.BehaviorKey != "slow" && row.BehaviorKey != "poison" && row.BehaviorKey != "store_isolation")
                    throw new InvalidOperationException("启用技能引用了未知Buff行为：" + row.Id + "/" + row.BehaviorKey);
                catalog.Add(new BuffModel(row.Id, row.BehaviorKey, RuntimeBuffMaxStacks, row.Priority, row.TickInterval,
                    row.TriggerSkillId, row.EffectPrefab));
                if (row.BehaviorKey == "store_isolation")
                {
                    if (isolationBuffId != 0 || row.TickInterval != 0f || string.IsNullOrWhiteSpace(row.EffectPrefab))
                        throw new InvalidOperationException("装盒需要唯一Buff、单实例、独立持续时间与表现资源。");
                    isolationBuffId = row.Id;
                }
                if (row.BehaviorKey == "slow")
                {
                    if (slowBuffId != 0) throw new InvalidOperationException("当前Slow字段必须对应唯一减速Buff。");
                    slowBuffId = row.Id;
                }
                if (row.BehaviorKey == "poison")
                {
                    if (poisonBuffId != 0 || row.TickInterval != 0f)
                        throw new InvalidOperationException("中毒必须对应唯一Buff、单实例单层且使用实例间隔。");
                    poisonBuffId = row.Id;
                }
            }
            foreach (var row in tables.TbSkill.DataList)
            {
                if (!row.Enabled) continue;
                if (!Enum.IsDefined(typeof(SkillType), row.SkillType) || !Enum.IsDefined(typeof(SkillTargetRule), row.TargetRule))
                    throw new InvalidOperationException("技能类型或目标规则无效：" + row.Id);
                if (row.BulletId < 0 || (row.BulletId != 0 && !catalog.TryGetBullet(row.BulletId, out _)))
                    throw new InvalidOperationException("技能缺少弹丸配置：" + row.Id + "/" + row.BulletId);
                if (row.TimelineKey.StartsWith("robot.", StringComparison.Ordinal) && row.TimelineKey != "robot.roll" && row.BulletId == 0)
                    throw new InvalidOperationException("机器人投射物技能必须配置弹丸：" + row.Id);
                catalog.Add(new SkillModel(row.Id, (SkillType)row.SkillType, row.TimelineKey,
                    row.MaxLevel, row.Cooldown, (SkillTargetRule)row.TargetRule, row.PassiveBuffIds, row.BulletId));
            }
            var poisonCap = 0;
            foreach (var row in tables.TbSkillLevel.DataList)
            {
                if (!enabledSkills.Contains(row.SkillId)) continue;
                if (!catalog.TryGet(row.SkillId, out var model) || row.Level < 1 || row.Level > model.MaxLevel ||
                    row.Damage < 0 || !EntityValidation.IsFiniteNonNegative(row.Slow) || row.Slow > 1f ||
                    !EntityValidation.IsFiniteNonNegative(row.Duration) ||
                    !EntityValidation.IsFiniteNonNegative(row.IsolateDuration)) throw new InvalidOperationException("技能等级配置无效：" + row.Id);
                ValidateRobotLevel(row, model, catalog);
                if (row.Slow > 0f && (slowBuffId == 0 || row.Duration <= 0f))
                    throw new InvalidOperationException("减速技能缺少Buff或有效持续时间：" + row.Id);
                if (row.PoisonDamage < 0 || row.PoisonMaxLayers < 0 ||
                    !EntityValidation.IsFiniteNonNegative(row.PoisonDuration) ||
                    !EntityValidation.IsFiniteNonNegative(row.PoisonInterval))
                    throw new InvalidOperationException("中毒等级参数无效：" + row.Id);
                if (HasPoison(row))
                {
                    if (poisonBuffId == 0 || row.PoisonMaxLayers <= 0 || row.PoisonDuration <= 0f || row.PoisonInterval <= 0f ||
                        (poisonCap != 0 && poisonCap != row.PoisonMaxLayers))
                        throw new InvalidOperationException("中毒缺少Buff、持续期或间隔，或各级上限不一致：" + row.Id);
                    poisonCap = row.PoisonMaxLayers;
                }
                var parameters = new Dictionary<string, float>
                {
                    { "isolateDuration", row.IsolateDuration }, { "damage", row.Damage }, { "slow", row.Slow }, { "duration", row.Duration },
                    { "scatterCount", row.ScatterCount }, { "scatterAngle", row.ScatterAngle },
                    { "pierceTargetCount", row.PierceTargetCount }, { "burstCount", row.BurstCount },
                    { "burstInterval", row.BurstInterval }, { "knockbackDistance", row.KnockbackDistance },
                    { "poisonDamage", row.PoisonDamage }, { "poisonDuration", row.PoisonDuration },
                    { "poisonInterval", row.PoisonInterval }, { "poisonMaxLayers", row.PoisonMaxLayers },
                    { "attackCycleCount", row.AttackCycleCount },
                    { "chainTargetCount", row.ChainTargetCount }, { "chainRange", row.ChainRange },
                    { "chainDamageRatio", row.ChainDamageRatio }, { "castWindup", row.CastWindup },
                    { "dropCount", row.DropCount },
                    { "dropSpacing", row.DropSpacing }, { "splashRadius", row.SplashRadius }
                };
                catalog.Add(new SkillLevelModel(row.SkillId, row.Level, parameters,
                    CaptureHitBuffs(slowBuffId, poisonBuffId, isolationBuffId, row.SkillId, parameters),
                    values => CaptureHitBuffs(slowBuffId, poisonBuffId, isolationBuffId, row.SkillId, values),
                    model.TimelineKey == "robot.roll" ? new StaffConfig(row.StaffPrefab, row.StaffSpawnGap,
                        row.StaffRollSpeed, row.StaffRollDistance, row.StaffWidth, row.StaffThickness,
                        row.StaffAppearGrowTime, row.StaffAppearSettleTime) : null));
            }
            foreach (var row in tables.TbSkill.DataList)
            {
                if (!row.Enabled) continue;
                for (var level = 1; level <= row.MaxLevel; level++)
                    if (!catalog.TryGetLevel(row.Id, level, out _)) throw new InvalidOperationException("缺少技能等级：" + row.Id + "/" + level);
                foreach (var id in row.PassiveBuffIds)
                {
                    if (!catalog.TryGetBuff(id, out var passive)) throw new InvalidOperationException("缺少被动Buff：" + id);
                    if (passive.TriggerSkillId > 0 && !catalog.TryGet(passive.TriggerSkillId, out _))
                        throw new InvalidOperationException("缺少启用的被动触发技能：" + passive.TriggerSkillId);

                }
            }
            ValidateRobotCycles(tables, catalog);
            return catalog;
        }

        private static void ValidateRobotCycles(Tables tables, SkillCatalog catalog)
        {
            foreach (var robot in tables.TbRobot.DataList)
            {
                if (robot.AttackCycleSkillId < 0)
                    throw new InvalidOperationException("机器人周期技能ID不能为负：" + robot.Id);
                if (robot.AttackCycleSkillId == 0 || !catalog.TryGet(robot.SkillId, out var primary)) continue;
                if (robot.AttackCycleSkillId == robot.SkillId || !catalog.TryGet(robot.AttackCycleSkillId, out _))
                    throw new InvalidOperationException("机器人周期技能缺失、未启用或引用自身普攻：" + robot.Id);
                for (var level = 1; level <= primary.MaxLevel; level++)
                    RobotAttackCycle.RequiredCountForConfig(catalog, primary.Id, level);
            }
        }

        private static void ValidateBullet(Bullet row)
        {
            if (row.BehaviorKey != "direct" && row.BehaviorKey != "chain" && row.BehaviorKey != "splash")
                throw new InvalidOperationException("启用技能引用了未知弹丸行为：" + row.Id + "/" + row.BehaviorKey);
            var validParabolic = row.FlightType == BulletFlightType.Parabolic && row.TrackTarget &&
                row.HitMode == BulletHitMode.LockedTarget && row.FlightTime > 0f;
            var validStraight = row.FlightType == BulletFlightType.Straight && !row.TrackTarget &&
                (row.HitMode == BulletHitMode.FirstEnemy || row.HitMode == BulletHitMode.PierceEnemies) && row.Speed > 0f;
            var validLanding = row.FlightType == BulletFlightType.Parabolic && !row.TrackTarget &&
                row.HitMode == BulletHitMode.AreaAtLanding && row.FlightTime > 0f && row.BehaviorKey == "splash";
            if (row.Id <= 0 || string.IsNullOrWhiteSpace(row.Prefab) ||
                !EntityValidation.IsFiniteNonNegative(row.Speed) || !EntityValidation.IsFiniteNonNegative(row.FlightTime) ||
                !EntityValidation.IsFiniteNonNegative(row.ArcHeight) || !EntityValidation.IsFiniteNonNegative(row.HitRadius) ||
                (!validParabolic && !validStraight && !validLanding))
                throw new InvalidOperationException("弹丸配置或轨迹命中组合无效：" + row.Id);
            if (row.BehaviorKey == "chain" && (!validStraight || row.HitMode != BulletHitMode.FirstEnemy ||
                string.IsNullOrWhiteSpace(row.HitEffectPrefab)))
                throw new InvalidOperationException("闪电链必须使用直线首次碰敌弹与命中特效：" + row.Id);
            if (row.BehaviorKey == "splash" && (!validLanding || string.IsNullOrWhiteSpace(row.HitEffectPrefab)))
                throw new InvalidOperationException("范围弹需要固定落点及落地表现：" + row.Id);
        }

        private static bool HasPoison(GameConfig.skill.SkillLevel row)
            => row.PoisonMaxLayers > 0 || row.SkillId == 1507 || row.PoisonDamage > 0 ||
                row.PoisonDuration > 0f || row.PoisonInterval > 0f;

        private static void ValidateRobotLevel(GameConfig.skill.SkillLevel row, SkillModel skill, SkillCatalog catalog)
        {
            if (skill.TimelineKey == "robot.roll" && (skill.BulletId != 0 || skill.Type != SkillType.Robot ||
                skill.TargetRule != SkillTargetRule.SameSideEnemy || row.Damage != 0 || row.PierceTargetCount != 0 || row.KnockbackDistance != 0))
                throw new InvalidOperationException("Staff技能只召唤道路障碍，不使用弹丸、伤害或单次击退参数：" + row.Id);
            if (skill.TimelineKey == "robot.store" && (row.IsolateDuration <= 0f ||
                skill.TargetRule != SkillTargetRule.SameSideEnemy || !catalog.TryGetBullet(skill.BulletId, out var store) ||
                store.HitMode != BulletHitMode.LockedTarget || store.BehaviorKey != "direct"))
                throw new InvalidOperationException("装盒技能需要单目标弹丸和有效持续时间：" + row.Id);
            if (skill.TimelineKey == "robot.juice" && (skill.TargetRule != SkillTargetRule.SameSidePosition ||
                row.DropCount < 1 || !EntityValidation.IsFiniteNonNegative(row.DropSpacing) || row.DropSpacing <= 0f ||
                !EntityValidation.IsFiniteNonNegative(row.SplashRadius) || row.SplashRadius <= 0f ||
                !catalog.TryGetBullet(skill.BulletId, out var juice) || juice.BehaviorKey != "splash"))
                throw new InvalidOperationException("果汁轰炸参数无效：" + row.Id);
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
            if (skill.BulletId != 0 && catalog.TryGetBullet(skill.BulletId, out var chain) && chain.BehaviorKey == "chain")
            {
                if (row.ChainTargetCount < 1 ||
                    !EntityValidation.IsFiniteNonNegative(row.ChainRange) || row.ChainRange <= 0f ||
                    !EntityValidation.IsFiniteNonNegative(row.ChainDamageRatio) || row.ChainDamageRatio <= 0f || row.ChainDamageRatio > 1f)
                    throw new InvalidOperationException("闪电链参数无效：" + row.Id);
            }

            if (skill.TimelineKey == "robot.chain" && (row.ChainTargetCount < 1 ||
                !EntityValidation.IsFiniteNonNegative(row.CastWindup) || row.CastWindup <= 0f))
                throw new InvalidOperationException("闪电链需要有效人数与施法前摇：" + row.Id);
        }

        private static IReadOnlyList<CombatBuffApplication> CaptureSlowBuff(int buffId, IReadOnlyDictionary<string, float> parameters)
        {
            if (!parameters.TryGetValue("slow", out var slow) || !parameters.TryGetValue("duration", out var duration) ||
                !EntityValidation.IsFiniteNonNegative(slow) || !EntityValidation.IsFiniteNonNegative(duration) ||
                slow <= 0f || duration <= 0f) return Array.Empty<CombatBuffApplication>();
            if (buffId == 0) throw new InvalidOperationException("减速技能缺少Buff配置。");
            return new[] { new CombatBuffApplication(buffId, duration, new Dictionary<string, float> { { "slow", slow } }) };
        }

        private static IReadOnlyList<CombatBuffApplication> CaptureHitBuffs(int slowBuffId, int poisonBuffId, int isolationBuffId,
            int skillId, IReadOnlyDictionary<string, float> parameters)
        {
            var effects = new List<CombatBuffApplication>(CaptureSlowBuff(slowBuffId, parameters));
            if (parameters.TryGetValue("isolateDuration", out var isolateDuration) && isolateDuration != 0f)
            {
                if (isolationBuffId == 0 || !EntityValidation.IsFiniteNonNegative(isolateDuration) || isolateDuration <= 0f)
                    throw new InvalidOperationException("装盒缺少Buff或有效持续时间：" + skillId);
                effects.Add(new CombatBuffApplication(isolationBuffId, isolateDuration));
            }
            if (!parameters.TryGetValue("poisonMaxLayers", out var cap) || cap == 0f) return effects;
            var damage = parameters["poisonDamage"];
            var duration = parameters["poisonDuration"];
            var interval = parameters["poisonInterval"];
            if (poisonBuffId == 0 || !EntityValidation.IsFiniteNonNegative(damage) || damage >= int.MaxValue || damage != Math.Truncate(damage) ||
                !EntityValidation.IsFiniteNonNegative(cap) || cap < 1f || cap >= int.MaxValue || cap != Math.Truncate(cap) ||
                !EntityValidation.IsFiniteNonNegative(duration) || duration <= 0f ||
                !EntityValidation.IsFiniteNonNegative(interval) || interval <= 0f)
                throw new InvalidOperationException("中毒命中参数无效：" + skillId);
            effects.Add(new CombatBuffApplication(poisonBuffId, duration, new Dictionary<string, float>
            {
                { "poisonDamage", damage }, { "poisonInterval", interval }, { "poisonMaxLayers", cap }, { "skillId", skillId }
            }));
            return effects;
        }
    }
}
