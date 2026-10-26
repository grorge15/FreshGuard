using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum SkillType { NormalAttack, Robot, Battlefield, Enemy }
    public enum SkillTargetRule { SameSideEnemy, SameSideBattlefield, SameSidePosition, None, SameSideRobot, SameSideEntity }
    public sealed class SkillModel
    {
        public int Id { get; }
        public SkillType Type { get; }
        public string TimelineKey { get; }
        public int MaxLevel { get; }
        public float Cooldown { get; }
        public SkillTargetRule TargetRule { get; }
        public IReadOnlyList<int> PassiveBuffIds { get; }
        public SkillModel(int id, SkillType type, string timelineKey, int maxLevel, float cooldown,
            SkillTargetRule targetRule, IReadOnlyList<int> passiveBuffIds = null)
        {
            EntityValidation.RequirePositive(id, nameof(id)); EntityValidation.RequirePositive(maxLevel, nameof(maxLevel));
            EntityValidation.RequireFiniteNonNegative(cooldown, nameof(cooldown));
            if (string.IsNullOrWhiteSpace(timelineKey)) throw new ArgumentException(nameof(timelineKey));
            Id = id; Type = type; TimelineKey = timelineKey; MaxLevel = maxLevel; Cooldown = cooldown; TargetRule = targetRule;
            PassiveBuffIds = new List<int>(passiveBuffIds ?? Array.Empty<int>()).AsReadOnly();
        }
    }
    public sealed class SkillLevelModel
    {
        public int SkillId { get; }
        public int Level { get; }
        public IReadOnlyDictionary<string, float> Parameters { get; }
        public SkillLevelModel(int skillId, int level, IReadOnlyDictionary<string, float> parameters)
        { SkillId = skillId; Level = level; Parameters = BuffObj.Copy(parameters); }
    }
    public sealed class SkillObj
    {
        public SkillModel Model { get; }
        public int Level { get; private set; } = 1;
        public float CooldownRemaining { get; internal set; }
        public SkillObj(SkillModel model) { Model = model ?? throw new ArgumentNullException(nameof(model)); }
        public void SetLevel(int level)
        { if (level < 1 || level > Model.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level)); Level = level; }
    }
    public sealed class SkillContainer
    {
        private readonly Dictionary<int, SkillObj> _items = new Dictionary<int, SkillObj>();
        public int Count => _items.Count;
        public bool TryGet(int id, out SkillObj skill) => _items.TryGetValue(id, out skill);
        public SkillObj Add(SkillModel model)
        {
            if (_items.TryGetValue(model.Id, out var existing)) return existing;
            var obj = new SkillObj(model); _items.Add(model.Id, obj); return obj;
        }
        public bool Remove(int id) => _items.Remove(id);
        public void Clear() => _items.Clear();
        public void Advance(float deltaTime)
        {
            EntityValidation.RequireFiniteNonNegative(deltaTime, nameof(deltaTime));
            foreach (var skill in _items.Values) skill.CooldownRemaining = Math.Max(0f, skill.CooldownRemaining - deltaTime);
        }
    }
    public sealed class SkillCatalog
    {
        private readonly Dictionary<int, SkillModel> _skills = new Dictionary<int, SkillModel>();
        private readonly Dictionary<(int, int), SkillLevelModel> _levels = new Dictionary<(int, int), SkillLevelModel>();
        private readonly Dictionary<int, BuffModel> _buffs = new Dictionary<int, BuffModel>();
        public void Add(SkillModel model) => _skills.Add(model.Id, model);
        public void Add(SkillLevelModel model) => _levels.Add((model.SkillId, model.Level), model);
        public void Add(BuffModel model) => _buffs.Add(model.Id, model);
        public bool TryGet(int id, out SkillModel model) => _skills.TryGetValue(id, out model);
        public bool TryGetLevel(int id, int level, out SkillLevelModel model) => _levels.TryGetValue((id, level), out model);
        public bool TryGetBuff(int id, out BuffModel model) => _buffs.TryGetValue(id, out model);
    }
}
