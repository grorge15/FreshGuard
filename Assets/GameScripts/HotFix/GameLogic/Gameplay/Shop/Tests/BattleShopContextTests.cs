using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public class BattleShopContextTests
    {
        [Test]
        public void BattleShopContext_IsAvailableInGameLogicAssembly()
        {
            var contextType = Type.GetType("GameLogic.BattleShopContext, GameLogic", false);

            Assert.That(contextType, Is.Not.Null,
                "缺少 GameLogic.BattleShopContext；商店纯逻辑核心尚未实现，这是预期的首次红测。");
        }
    }
}
