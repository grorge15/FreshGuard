using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameLogic
{
    public sealed class MatchBuff
    {
        public int BuffId { get; }
        public string Name { get; }
        public string IconLocation { get; }
        public int Layers { get; }
        public MatchBuff(int id, string name, string iconLocation, int layers)
        {
            if (id <= 0 || layers <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            BuffId = id; Name = name; IconLocation = iconLocation; Layers = layers;
        }
    }
    public static class MatchBuffGenerator
    {
        public static IReadOnlyList<MatchBuff> Draw(IReadOnlyList<MatchBuff> unlocked, int playerKinds, IShopRandom random)
        {
            if (unlocked == null || random == null) throw new ArgumentNullException();
            if (playerKinds < 0 || playerKinds > unlocked.Count) throw new ArgumentOutOfRangeException(nameof(playerKinds));
            var result = new List<MatchBuff>();
            if (playerKinds == 0) return result.AsReadOnly();
            var count = CheckedInt(random, playerKinds) + 1;
            var pool = new List<MatchBuff>(unlocked);
            for (var i = 0; i < count; i++)
            {
                var index = CheckedInt(random, pool.Count);
                var buff = pool[index];
                result.Add(new MatchBuff(buff.BuffId, buff.Name, buff.IconLocation, 1));
                pool.RemoveAt(index);
            }
            return result.AsReadOnly();
        }
        private static int CheckedInt(IShopRandom random, int max)
        {
            var value = random.NextInt(max);
            if (value < 0 || value >= max) throw new InvalidOperationException("随机数越界。");
            return value;
        }
    }
    /// <summary>复制所有可变配置；原始候选序号用于保留Drop权重映射。</summary>
    public sealed class MatchEntrySnapshot
    {
        public string EntryId { get; }
        public string PlayerName { get; }
        public string PlayerAvatar { get; }
        public string OpponentName { get; }
        public string OpponentAvatar { get; }
        public int Trophies { get; }
        public IReadOnlyList<int> Candidates { get; }
        public IReadOnlyDictionary<int, int> CandidateOrdinals { get; }
        public IReadOnlyDictionary<int, int> OpponentLevelCaps { get; }
        public IReadOnlyList<MatchBuff> PlayerBuffs { get; }
        public IReadOnlyList<MatchBuff> OpponentBuffs { get; }

        public MatchEntrySnapshot(DemoBattleProfile profile, MainMenuPlayerState player, ShopRules rules, IShopRandom random = null)
        {
            if (profile == null || player == null || rules == null) throw new ArgumentNullException();
            if (string.IsNullOrWhiteSpace(profile.PlayerName) || string.IsNullOrWhiteSpace(profile.OpponentName) ||
                string.IsNullOrWhiteSpace(profile.PlayerAvatar) || string.IsNullOrWhiteSpace(profile.OpponentAvatar))
                throw new ArgumentException("Demo玩家和对手的名称、头像不能为空。");
            var ids = new List<int>(); var ordinals = new Dictionary<int, int>(); var caps = new Dictionary<int, int>();
            var seen = new HashSet<int>();
            for (var i = 0; i < profile.Robots.Count; i++)
            {
                var robot = profile.Robots[i];
                if (robot == null || robot.RobotId <= 0 || !seen.Add(robot.RobotId) || robot.ReferenceLevel < 1 || robot.ReferenceLevel > 5)
                    throw new ArgumentException("Demo机器人ID须唯一且等级为1～5。");
                if (!robot.Unlocked) continue;
                ids.Add(robot.RobotId); ordinals.Add(robot.RobotId, i); caps.Add(robot.RobotId, robot.ReferenceLevel);
            }
            if (ids.Count < rules.SlotCount) throw new ArgumentException("Demo至少需要三个已解锁机器人。");
            for (var slot = 0; slot < rules.SlotCount; slot++)
            {
                var positive = false;
                foreach (var id in ids) positive |= rules.GetCandidateWeight(slot, ordinals[id], id) > 0;
                if (!positive) throw new ArgumentException("Demo已解锁机器人在槽位" + slot + "没有正Drop权重。");
            }
            var unlocked = new List<MatchBuff>(); var catalogue = new Dictionary<int, DemoBuffDefinition>();
            foreach (var buff in profile.Buffs)
            {
                if (buff == null || buff.BuffId <= 0 || catalogue.ContainsKey(buff.BuffId) || string.IsNullOrWhiteSpace(buff.Name) || string.IsNullOrWhiteSpace(buff.IconLocation))
                    throw new ArgumentException("Demo BUFF目录无效或重复。");
                catalogue.Add(buff.BuffId, buff);
                if (buff.Unlocked) unlocked.Add(new MatchBuff(buff.BuffId, buff.Name, buff.IconLocation, 1));
            }
            var carried = new List<MatchBuff>(); seen.Clear();
            foreach (var carry in profile.PlayerBuffs)
            {
                DemoBuffDefinition buff;
                if (carry == null || carry.Layers < 1 || !seen.Add(carry.BuffId) || !catalogue.TryGetValue(carry.BuffId, out buff) || !buff.Unlocked)
                    throw new ArgumentException("玩家携带BUFF必须已解锁、层数为正且种类不重复。");
                carried.Add(new MatchBuff(buff.BuffId, buff.Name, buff.IconLocation, carry.Layers));
            }
            EntryId = Guid.NewGuid().ToString("N"); PlayerName = player.PlayerName; PlayerAvatar = profile.PlayerAvatar;
            OpponentName = profile.OpponentName; OpponentAvatar = profile.OpponentAvatar; Trophies = player.Trophies;
            Candidates = ids.AsReadOnly(); CandidateOrdinals = new ReadOnlyDictionary<int, int>(ordinals);
            OpponentLevelCaps = new ReadOnlyDictionary<int, int>(caps); PlayerBuffs = carried.AsReadOnly();
            OpponentBuffs = MatchBuffGenerator.Draw(unlocked, carried.Count, random ?? new ShopRandom());
        }
    }
}
