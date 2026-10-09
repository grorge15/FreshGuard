using System;
using System.IO;
using System.Linq;
using GameConfig;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace GameLogic.Tests
{
    public class ShopConfigTests
    {
        private static Tables RealTables()
        {
            return new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
        }

        [Test]
        public void FromTablesReadsRealGeneratedShopGlobalsAndThreeDrops()
        {
            var tables = RealTables();
            var rules = ShopRules.FromTables(tables);
            Assert.AreEqual(20, rules.InitialCoins);
            Assert.AreEqual(3, rules.SlotCount);
            Assert.AreEqual(5, rules.RefreshPrice);
            Assert.AreEqual(10, rules.AdditionalRerollLimit);
            Assert.AreEqual(0.05, rules.SecondStarProbability, 0.00000001);
            CollectionAssert.AreEqual(new[] { 6, 8, 12, 16, 20 },
                Enumerable.Range(2, 5).Select(id => rules.GetPrice(id, 1)));
            foreach (var slot in rules.SlotCandidateWeights)
                CollectionAssert.AreEqual(new[] { 2000d, 2000d, 2000d }, Enumerable.Range(1, 3).Select(id => slot[id]));
            var random = new ShopSequenceRandom();
            random.Enqueue(0, 0.4, 0.8, 0, 0.4, 0.8);
            using (var context = new BattleShopContext(rules, ShopTestData.Candidates, ShopTestData.Candidates,
                id => tables.TbRobot.Get(id), random))
            {
                CollectionAssert.AreEqual(new[] { 6, 8, 12 }, context.GetState(BattleSide.Player).Slots.Select(s => s.Offer.Price));
                ShopTestData.Purchase(context, 1);
                Assert.AreEqual(12, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(20, context.GetState(BattleSide.Opponent).EnergyCoins);
            }
        }

        [Test]
        public void FromTablesUsesConfiguredAmountsInsteadOfHardcodedDefaults()
        {
            var globals = ShopTestData.Globals();
            globals[22] = 27;
            globals[29] = 7;
            globals[30] = 0.25f;
            globals[31] = 2;
            globals[36] = 9;
            globals[24] = 11;
            globals[25] = 21;
            globals[26] = 51;
            globals[27] = 12;
            globals[28] = 13;
            var rules = ShopRules.FromTables(ShopTestData.Tables(globals));
            Assert.AreEqual(27, rules.InitialCoins);
            Assert.AreEqual(7, rules.RefreshPrice);
            Assert.AreEqual(2, rules.AdditionalRerollLimit);
            Assert.AreEqual(0.25, rules.SecondStarProbability);
            Assert.AreEqual(9, rules.GetPrice(2, 1));
            Assert.AreEqual(18, rules.GetPrice(2, 2));
            using (var context = ShopTestData.Context(rules))
            {
                Assert.AreEqual(27, context.GetState(BattleSide.Player).EnergyCoins);
                ShopTestData.Purchase(context);
                Assert.AreEqual(18, context.GetState(BattleSide.Player).EnergyCoins);
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.NormalEnemy, 1);
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.EliteEnemy, 2);
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.Boss, 3);
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.NormalGlass, 1);
                context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.ColoredGlass, 2);
                Assert.AreEqual(135, context.GetState(BattleSide.Opponent).EnergyCoins, "27+11+21+51+12+13。");
            }
        }

        [Test]
        public void AsymmetricSlotDropsMapOrdinalsToEachSidesCandidateOrder()
        {
            var rules = ShopRules.FromTables(ShopTestData.Tables(packages: new[]
            {
                new[] { 2, 10 }, new[] { 3, 10 }, new[] { 1, 10 }
            }));
            using (var context = new BattleShopContext(rules, new[] { 1001, 1002, 1003 }, new[] { 1003, 1001, 1002 },
                id => ShopTestData.Robot(id), new ShopSequenceRandom { DefaultValue = 0.8 }))
            {
                CollectionAssert.AreEqual(new[] { 1002, 1003, 1001 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
                CollectionAssert.AreEqual(new[] { 1001, 1002, 1003 }, context.GetState(BattleSide.Opponent).LastBatchRobotIds);
                Assert.AreNotSame(rules.SlotCandidateWeights[0], rules.SlotCandidateWeights[1]);
            }
        }

        [Test]
        public void FallbackUsesSelectedSlotsPositiveWeightsAndDoesNotBorrowAnotherSlotsCandidates()
        {
            var globals = ShopTestData.Globals();
            globals[31] = 0;
            var rules = ShopRules.FromTables(ShopTestData.Tables(globals, new[]
            {
                new[] { 1, 1 }, new[] { 1, 1, 2, 1 }, new[] { 1, 1, 3, 1 }
            }));
            var random = new ShopSequenceRandom { SlotIndex = 2 };
            using (var context = ShopTestData.Context(rules, random))
            {
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1003 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
            }
            random = new ShopSequenceRandom { SlotIndex = 0 };
            using (var context = ShopTestData.Context(rules, random))
            {
                Assert.AreEqual(ShopOperationResult.Success, context.TryRefresh(BattleSide.Player));
                CollectionAssert.AreEqual(new[] { 1001, 1001, 1001 }, context.GetState(BattleSide.Player).LastBatchRobotIds);
            }
        }

        [TestCase(22)] [TestCase(23)] [TestCase(24)] [TestCase(25)] [TestCase(26)]
        [TestCase(27)] [TestCase(28)] [TestCase(29)] [TestCase(31)]
        [TestCase(36)] [TestCase(37)] [TestCase(38)] [TestCase(39)] [TestCase(40)]
        public void EveryIntegerGlobalRejectsFractionalFloatValues(int globalId)
        {
            var globals = ShopTestData.Globals();
            globals[globalId] = 3.5f;
            Assert.Throws<ArgumentException>(() => ShopRules.FromTables(ShopTestData.Tables(globals)));
        }

        [TestCase(22, float.NaN)] [TestCase(22, float.PositiveInfinity)]
        [TestCase(22, -1f)] [TestCase(22, 2147483648f)]
        [TestCase(23, 4f)] [TestCase(30, -0.01f)] [TestCase(30, 1.01f)]
        public void InvalidGlobalRangeOrNonfiniteValueRejectsRules(int id, float value)
        {
            var globals = ShopTestData.Globals();
            globals[id] = value;
            Assert.That(() => ShopRules.FromTables(ShopTestData.Tables(globals)), Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(new[] { 1, 1, 2 })]
        [TestCase(new[] { 1, -1 })]
        [TestCase(new[] { 4, 1 })]
        [TestCase(new[] { 1, 1, 1, 2 })]
        [TestCase(new[] { 1, 0, 2, 0, 3, 0 })]
        public void MalformedDropPackageRejectsRules(int[] package)
        {
            Assert.That(() => ShopRules.FromTables(ShopTestData.Tables(packages: new[]
                { package, new[] { 1, 1 }, new[] { 1, 1 } })), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void MissingGlobalDropOrUnsupportedTypeRejectsRules()
        {
            var globals = ShopTestData.Globals();
            globals.Remove(29);
            Assert.Throws<ArgumentException>(() => ShopRules.FromTables(ShopTestData.Tables(globals)));
            Assert.Throws<ArgumentException>(() => ShopRules.FromTables(ShopTestData.Tables(dropType: 2)));
            Assert.Throws<ArgumentException>(() => ShopRules.FromTables(ShopTestData.Tables(packages: new[] { new[] { 1, 1 } })));
        }

        [TestCase(ShopRewardSource.NormalEnemy, 4)]
        [TestCase(ShopRewardSource.EliteEnemy, 20)]
        [TestCase(ShopRewardSource.Boss, 50)]
        [TestCase(ShopRewardSource.NormalGlass, 10)]
        [TestCase(ShopRewardSource.ColoredGlass, 10)]
        public void FormalRewardApiReadsFiveRealConfigAmounts(ShopRewardSource source, int amount)
        {
            var rules = ShopRules.FromTables(RealTables());
            using (var context = ShopTestData.Context(rules))
            {
                Assert.AreEqual(ShopOperationResult.Success, context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, source, 991));
                Assert.AreEqual(20 + amount, context.GetState(BattleSide.Opponent).EnergyCoins);
                Assert.AreEqual(20, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.DuplicateReward, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, source, 991));
            }
        }

        [Test]
        public void DifferentEnemyOrGlassSubtypesShareDeduplicationCategory()
        {
            using (var context = ShopTestData.Context())
            {
                Assert.AreEqual(ShopOperationResult.Success, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.NormalEnemy, 1));
                Assert.AreEqual(ShopOperationResult.DuplicateReward, context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.Boss, 1));
                Assert.AreEqual(ShopOperationResult.Success, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.NormalGlass, 1));
                Assert.AreEqual(ShopOperationResult.DuplicateReward, context.Rewards.TryGrant(context.BattleId, BattleSide.Opponent, ShopRewardSource.ColoredGlass, 1));
                Assert.AreEqual(34, context.GetState(BattleSide.Player).EnergyCoins);
                Assert.AreEqual(ShopOperationResult.InvalidArgument, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, (ShopRewardSource)99, 2));
                Assert.AreEqual(ShopOperationResult.InvalidArgument, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.Boss, 0));
                context.EndBattle();
                Assert.AreEqual(ShopOperationResult.BattleEnded, context.Rewards.TryGrant(context.BattleId, BattleSide.Player, ShopRewardSource.Boss, 2));
            }
        }
    }
}
