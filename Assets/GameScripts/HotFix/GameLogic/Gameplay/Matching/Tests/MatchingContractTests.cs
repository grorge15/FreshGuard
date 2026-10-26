using System;
using NUnit.Framework;
namespace GameLogic.Tests
{
    public sealed class MatchingContractTests
    {
        [TestCase("DemoBattleProfile")]
        [TestCase("MatchEntrySnapshot")]
        [TestCase("BattleMatchSession")]
        [TestCase("MatchPlayerRecord")]
        public void MatchingTypesExist(string name)
        {
            Assert.That(Type.GetType("GameLogic." + name + ", GameLogic"), Is.Not.Null,
                "匹配入口需要配置、不可变快照、前台状态机和扣费恢复记录。");
        }
    }
}
