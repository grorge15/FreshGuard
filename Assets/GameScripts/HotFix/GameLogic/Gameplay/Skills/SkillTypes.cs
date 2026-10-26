using System;
using System.Collections.Generic;
using GameConfig;
using UnityEngine;

namespace GameLogic
{
    public enum SkillType { Active, Passive }
    public enum SkillBehaviorType { BasicProjectile, DamageThenApplySlow }
    public enum SkillTriggerSource { RobotContact, PlayerInput, BossAi, Other }

    public sealed class SkillDefinition
    {
        public int Id { get; }
        public string Name { get; }
        public SkillType SkillType { get; }
        public SkillBehaviorType BehaviorType { get; }
        public bool RequiresTarget { get; }
        public int MaxLevel { get; }

        public SkillDefinition(int id, SkillType skillType, SkillBehaviorType behaviorType,
            bool requiresTarget, int maxLevel = 1, string name = null)
        {
            EntityValidation.RequirePositive(id, nameof(id));
            EntityValidation.RequirePositive(maxLevel, nameof(maxLevel));
            Id = id; SkillType = skillType; BehaviorType = behaviorType;
            Name = name ?? string.Empty; RequiresTarget = requiresTarget; MaxLevel = maxLevel;
        }
    }

    /// <summary>一个技能某一级的数值定义，独立于技能身份与行为。</summary>
    public sealed class SkillLevelDefinition
    {
        public int SkillId { get; }
        public int Level { get; }
        public IReadOnlyList<int> IntValues { get; }
        public IReadOnlyList<float> FloatValues { get; }

        public SkillLevelDefinition(int skillId, int level, IReadOnlyList<int> intValues,
            IReadOnlyList<float> floatValues)
        {
            EntityValidation.RequirePositive(skillId, nameof(skillId));
            EntityValidation.RequirePositive(level, nameof(level));
            if (intValues == null) throw new ArgumentNullException(nameof(intValues));
            if (floatValues == null) throw new ArgumentNullException(nameof(floatValues));
            SkillId = skillId; Level = level;
            IntValues = new List<int>(intValues).AsReadOnly();
            FloatValues = new List<float>(floatValues).AsReadOnly();
        }
    }

    public sealed class SkillDefinitionRegistry
    {
        private readonly Dictionary<int, SkillDefinition> _definitions = new Dictionary<int, SkillDefinition>();
        private readonly Dictionary<(int SkillId, int Level), SkillLevelDefinition> _levels =
            new Dictionary<(int SkillId, int Level), SkillLevelDefinition>();

        public void Register(SkillDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _definitions[definition.Id] = definition;
        }

        public bool TryGet(int id, out SkillDefinition definition) => _definitions.TryGetValue(id, out definition);

        public void RegisterLevel(SkillLevelDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!TryGet(definition.SkillId, out var skill) || definition.Level > skill.MaxLevel)
                throw new ArgumentException("技能等级必须引用已注册技能且不超过最高等级。", nameof(definition));
            _levels[(definition.SkillId, definition.Level)] = definition;
        }

        public bool TryGetLevel(int skillId, int level, out SkillLevelDefinition definition) =>
            _levels.TryGetValue((skillId, level), out definition);

        public static SkillDefinitionRegistry FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var registry = new SkillDefinitionRegistry();
            foreach (var row in tables.TbSkill.DataList)
            {
                SkillBehaviorType behavior;
                if (!Enum.TryParse(row.BehaviorType, true, out behavior))
                    throw new ArgumentException("未知技能行为：" + row.BehaviorType, nameof(tables));
                if (row.SkillType != 1 && row.SkillType != 2)
                    throw new ArgumentOutOfRangeException(nameof(row.SkillType));
                registry.Register(new SkillDefinition(row.Id,
                    row.SkillType == 1 ? SkillType.Active : SkillType.Passive,
                    behavior, row.RequiresTarget, row.MaxLevel, row.Name));
            }
            foreach (var row in tables.TbSkillLevel.DataList)
            {
                if (registry.TryGetLevel(row.SkillId, row.Level, out _))
                    throw new ArgumentException($"技能{row.SkillId}的等级{row.Level}配置重复。", nameof(tables));
                registry.RegisterLevel(new SkillLevelDefinition(row.SkillId, row.Level, row.IntValues, row.FloatValues));
            }
            foreach (var skill in registry._definitions.Values)
                for (var level = 1; level <= skill.MaxLevel; level++)
                    if (!registry.TryGetLevel(skill.Id, level, out _))
                        throw new ArgumentException($"技能{skill.Id}缺少等级{level}配置。", nameof(tables));
            return registry;
        }

        public static SkillDefinitionRegistry CreateDefaults()
        {
            var registry = new SkillDefinitionRegistry();
            registry.Register(new SkillDefinition(1505, SkillType.Active,
                SkillBehaviorType.DamageThenApplySlow, true));
            registry.RegisterLevel(new SkillLevelDefinition(1505, 1, new[] { 20 }, new[] { 0.3f, 3f }));
            return registry;
        }
    }

    public sealed class SkillCastRequest
    {
        public long BattleId { get; }
        public int SourceInstanceId { get; }
        public BattleSide SourceSide { get; }
        public CombatSourceKind SourceKind { get; }
        public int SkillId { get; }
        public int SkillLevel { get; }
        public SkillTriggerSource TriggerSource { get; }
        public int? TargetInstanceId { get; }
        public Vector3? TargetPosition { get; }
        public Vector3 Origin { get; }
        public Vector3 AimPoint { get; }
        public int BaseDamage { get; }
        public float ProjectileFlightTime { get; }
        public float ProjectileArcHeight { get; }

        public SkillCastRequest(long battleId, int sourceInstanceId, BattleSide sourceSide,
            CombatSourceKind sourceKind, int skillId, SkillTriggerSource triggerSource,
            int? targetInstanceId, Vector3? targetPosition, Vector3 origin, Vector3 aimPoint,
            int baseDamage, float projectileFlightTime, float projectileArcHeight, int skillLevel = 1)
        {
            if (battleId <= 0 || skillId < 0 || baseDamage < 0) throw new ArgumentOutOfRangeException(nameof(battleId));
            EntityValidation.RequirePositive(sourceInstanceId, nameof(sourceInstanceId));
            EntityValidation.RequirePositive(skillLevel, nameof(skillLevel));
            EntityValidation.RequireSide(sourceSide);
            EntityValidation.RequireFiniteNonNegative(projectileFlightTime, nameof(projectileFlightTime));
            EntityValidation.RequireFiniteNonNegative(projectileArcHeight, nameof(projectileArcHeight));
            BattleId = battleId; SourceInstanceId = sourceInstanceId; SourceSide = sourceSide;
            SourceKind = sourceKind; SkillId = skillId; SkillLevel = skillLevel; TriggerSource = triggerSource;
            TargetInstanceId = targetInstanceId; TargetPosition = targetPosition; Origin = origin;
            AimPoint = aimPoint; BaseDamage = baseDamage; ProjectileFlightTime = projectileFlightTime;
            ProjectileArcHeight = projectileArcHeight;
        }
    }

    public sealed class SkillCastSnapshot
    {
        private readonly List<int> _targets;
        private readonly List<int> _intValues;
        private readonly List<float> _floatValues;
        public long CastId { get; }
        public long BattleId { get; }
        public int SourceInstanceId { get; }
        public BattleSide SourceSide { get; }
        public int SkillId { get; }
        public int SkillLevel { get; }
        public SkillBehaviorType BehaviorType { get; }
        public IReadOnlyList<int> TargetInstanceIds => _targets.AsReadOnly();
        public IReadOnlyList<int> IntValues => _intValues.AsReadOnly();
        public IReadOnlyList<float> FloatValues => _floatValues.AsReadOnly();
        public Vector3 Origin { get; }
        public Vector3 AimPoint { get; }
        public float ProjectileFlightTime { get; }
        public float ProjectileArcHeight { get; }
        public CombatActionSnapshot DamageAction { get; }
        public bool IsFinished { get; private set; }

        internal SkillCastSnapshot(long castId, SkillCastRequest request, SkillDefinition definition,
            IReadOnlyList<int> targets, IReadOnlyList<int> intValues, IReadOnlyList<float> floatValues,
            CombatActionSnapshot damageAction)
        {
            CastId = castId; BattleId = request.BattleId; SourceInstanceId = request.SourceInstanceId;
            SourceSide = request.SourceSide; SkillId = request.SkillId; SkillLevel = request.SkillLevel;
            BehaviorType = definition.BehaviorType;
            _targets = new List<int>(targets); _intValues = new List<int>(intValues); _floatValues = new List<float>(floatValues);
            Origin = request.Origin; AimPoint = request.AimPoint; ProjectileFlightTime = request.ProjectileFlightTime;
            ProjectileArcHeight = request.ProjectileArcHeight; DamageAction = damageAction;
        }

        internal void Finish() { IsFinished = true; }
    }

    public sealed class SkillCastResult
    {
        public bool Succeeded { get; }
        public SkillCastSnapshot Cast { get; }
        internal SkillCastResult(bool succeeded, SkillCastSnapshot cast) { Succeeded = succeeded; Cast = cast; }
        public static SkillCastResult Rejected() => new SkillCastResult(false, null);
    }

    public sealed class SkillRuntimeContext
    {
        public BattleRuntime Battle { get; }
        public ICombatResolutionPort ResolutionPort { get; }
        public ICombatStatusPort StatusPort { get; }
        public Func<SkillCastRequest, int, bool> IsTargetValid { get; }

        public SkillRuntimeContext(BattleRuntime battle, ICombatResolutionPort resolutionPort,
            ICombatStatusPort statusPort, Func<SkillCastRequest, int, bool> isTargetValid)
        {
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            ResolutionPort = resolutionPort; StatusPort = statusPort; IsTargetValid = isTargetValid;
        }
    }

    public interface ISkillBehavior
    {
        SkillCastResult TryCast(SkillCastRequest request, SkillRuntimeContext context,
            SkillDefinition definition, SkillLevelDefinition level, long castId);
        void OnHit(SkillCastSnapshot cast, SkillRuntimeContext context);
    }
}
