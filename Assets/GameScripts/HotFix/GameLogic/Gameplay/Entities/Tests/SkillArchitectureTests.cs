using NUnit.Framework;

namespace GameLogic.Tests
{
    public sealed class SkillArchitectureTests
    {
        [Test]
        public void BattleAndEntityExposeSkillOwnershipWithoutAPlayerEntity()
        {
            var assembly = typeof(Entity).Assembly;
            var owner = assembly.GetType("GameLogic.ISkillOwner");
            Assert.IsNotNull(owner, "战场和实体需要共享技能持有者协议");
            var battlefield = assembly.GetType("GameLogic.BattlefieldRuntime");
            Assert.IsNotNull(battlefield);
            Assert.IsTrue(owner.IsAssignableFrom(battlefield));
            Assert.IsTrue(owner.IsAssignableFrom(typeof(RobotEntity)));
            Assert.IsFalse(typeof(Entity).IsAssignableFrom(battlefield));
            Assert.IsNull(assembly.GetType("GameLogic.PlayerEntity"));
            Assert.IsNull(assembly.GetType("GameLogic.ChaState"));
        }
    }
}
