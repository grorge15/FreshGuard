using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class SkillAvailabilityShopTests
    {
        private static ShopRules OrdinalRules()
        {
            var slots = Enumerable.Range(0, 3).Select(_ =>
                (IReadOnlyDictionary<int, double>)new Dictionary<int, double>
                { { 1, 0 }, { 2, 0 }, { 3, 1 }, { 4, 0 } }).ToArray();
            return new ShopRules(slots, new Dictionary<int, int>
                { { 2, 6 }, { 3, 8 }, { 4, 12 }, { 5, 16 }, { 6, 20 } });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledSkillKeepsOriginalDropOrdinalWithAndWithoutEntry(bool useEntry)
        {
            var profile = ScriptableObject.CreateInstance<DemoBattleProfile>();
            try
            {
                var candidates = new[] { 501, 502, 505, 601 };
                foreach (var id in candidates)
                    profile.Robots.Add(new DemoRobotReference { RobotId = id });
                Func<int, bool> enabled = id => id != 502;
                var rules = OrdinalRules();
                var player = new MainMenuPlayerState(30, TimeSpan.FromMinutes(5), 30, 0, 0, DateTimeOffset.UtcNow);
                var entry = useEntry ? new MatchEntrySnapshot(profile, player, rules,
                    new ShopSequenceRandom(), enabled) : null;
                if (entry != null)
                {
                    Assert.That(entry.Candidates, Is.EqualTo(new[] { 501, 505, 601 }));
                    Assert.That(entry.CandidateOrdinals[505], Is.EqualTo(2));
                }
                var resolved = new HashSet<int>();
                using (var context = new BattleShopContext(rules, candidates, candidates, id =>
                    { resolved.Add(id); return ShopTestData.Robot(id); },
                    new ShopSequenceRandom(), entry, enabled))
                {
                    foreach (var side in new[] { BattleSide.Player, BattleSide.Opponent })
                        Assert.That(context.GetState(side).Slots.All(slot => slot.Offer.RobotId == 505), Is.True,
                            "过滤不能把第3候选的权重移给第4候选。");
                    Assert.That(resolved.Contains(502), Is.False, "禁用机器人不能进入配置及资源准备流程。");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
