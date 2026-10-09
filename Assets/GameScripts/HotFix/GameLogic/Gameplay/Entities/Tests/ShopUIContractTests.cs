using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public class ShopUIContractTests
    {
        [Test]
        public void PlayerShopUsesFrameworkWindowAndSeparateDragController()
        {
            var window = typeof(RobotEntity).Assembly.GetType("GameLogic.BattleShopUI");
            Assert.IsNotNull(window, "玩家商店必须接入 TEngine UIWindow。");
            Assert.IsTrue(typeof(UIWindow).IsAssignableFrom(window));
            var attribute = (WindowAttribute)Attribute.GetCustomAttribute(window, typeof(WindowAttribute));
            Assert.IsNotNull(attribute);
            Assert.AreEqual("BattleShopUI", attribute.Location);
            Assert.IsFalse(attribute.FullScreen);
            Assert.IsNotNull(typeof(RobotEntity).Assembly.GetType("GameLogic.RobotDragController"));
        }
    }
}
