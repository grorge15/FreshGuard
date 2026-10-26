using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public sealed class MainMenuLoadingContractTests
    {
        [Test]
        public void BattleControllerExposesProgressAwareInitialization()
        {
            var progressType = Type.GetType("GameLogic.BattleLoadingProgress, GameLogic");
            Assert.IsNotNull(progressType, "必须定义战斗加载进度类型。");
            var overload = typeof(BattleShopSceneController).GetMethods()
                .Where(method => method.Name == "InitializeAsync")
                .FirstOrDefault(method => method.GetParameters().Any(parameter =>
                    parameter.ParameterType.IsGenericType &&
                    parameter.ParameterType.GetGenericTypeDefinition() == typeof(IProgress<>) &&
                    parameter.ParameterType.GetGenericArguments()[0] == progressType));

            Assert.IsNotNull(overload, "战斗初始化必须公开可报告进度的异步入口。");
        }

        [Test]
        public void MainMenuStateSupportsConfiguredEnergyLifecycle()
        {
            var stateType = Type.GetType("GameLogic.MainMenuPlayerState, GameLogic");
            Assert.IsNotNull(stateType, "必须定义主界面玩家状态类型。");
            var state = Activator.CreateInstance(stateType, 30, TimeSpan.FromMinutes(5), 30, 0, 2500,
                DateTimeOffset.UtcNow);
            var trySpend = stateType.GetMethod("TrySpendEnergy");
            var addEnergy = stateType.GetMethod("AddEnergy");
            var addCoins = stateType.GetMethod("AddCoins");
            Assert.IsNotNull(trySpend);
            Assert.IsNotNull(addEnergy);
            Assert.IsNotNull(addCoins);
            Assert.IsFalse((bool)trySpend.Invoke(state, new object[] { 31 }));
            Assert.IsTrue((bool)trySpend.Invoke(state, new object[] { 5 }));
            Assert.AreEqual(25, stateType.GetProperty("Energy").GetValue(state));
            addEnergy.Invoke(state, new object[] { 5 });
            Assert.AreEqual(30, stateType.GetProperty("Energy").GetValue(state));
            addCoins.Invoke(state, new object[] { 100 });
            Assert.AreEqual(100, stateType.GetProperty("Coins").GetValue(state));
        }

        [Test]
        public void MainMenuUiAssetsArePresentBeforeSceneWiring()
        {
            var root = Application.dataPath;
            Assert.IsTrue(File.Exists(Path.Combine(root, "AssetRaw/UI/MainMenuUI.prefab")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "AssetRaw/UI/BattleLoadingUI.prefab")));
        }
    }
}
