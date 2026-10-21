using System;
using NUnit.Framework;

namespace GameLogic.Tests
{
    public class ShopUIContractTests
    {
        [Test]
        public void PlayerShopUsesFrameworkWindowAndSeparateDragController()
        {
            var window = typeof(RobotEntity).Assembly.GetType("GameLogic.MainCombatUI");
            Assert.IsNotNull(window, "对局需要唯一全屏 MainCombatUI 窗口。");
            Assert.IsTrue(typeof(UIWindow).IsAssignableFrom(window));
            var attribute = (WindowAttribute)Attribute.GetCustomAttribute(window, typeof(WindowAttribute));
            Assert.IsNotNull(attribute);
            Assert.AreEqual("MainCombatUI", attribute.Location);
            Assert.IsTrue(attribute.FullScreen);
            Assert.IsTrue(typeof(UIWidget).IsAssignableFrom(typeof(BattleShopUI)));
            Assert.IsTrue(typeof(UIWidget).IsAssignableFrom(typeof(BattleInfoUI)));
            Assert.IsNotNull(typeof(RobotEntity).Assembly.GetType("GameLogic.RobotDragController"));
        }
    }
}
