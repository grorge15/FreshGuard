using System;
using System.Collections.Generic;
using GameConfig.robot;
using GameConfig;
using Luban;

namespace GameLogic.Tests
{
    internal static class ShopTestData
    {
        internal static readonly int[] Candidates = { 1001, 1002, 1003 };

        internal static ShopRules Rules(double first = 1, double second = 1, double third = 1)
        {
            return new ShopRules(new Dictionary<int, double>
            {
                { 1001, first }, { 1002, second }, { 1003, third }
            });
        }

        internal static Robot Robot(int id, int quality = 2)
        {
            var shapeBuffer = new ByteBuf();
            shapeBuffer.WriteInt(1);
            shapeBuffer.WriteSize(2);
            foreach (var x in new[] { 0, 1 })
            {
                shapeBuffer.WriteSize(2);
                shapeBuffer.WriteInt(x);
                shapeBuffer.WriteInt(0);
            }
            var robotBuffer = new ByteBuf();
            robotBuffer.WriteInt(id);
            robotBuffer.WriteString("Robot " + id);
            robotBuffer.WriteInt(quality);
            robotBuffer.WriteString("RobotPrefab");
            robotBuffer.WriteString("RobotIcon");
            robotBuffer.WriteInt(1);
            robotBuffer.WriteInt(0);
            return new Robot(robotBuffer) { ShapeId_Ref = new RobotShape(shapeBuffer) };
        }

        internal static BattleShopContext Context(ShopRules rules = null, IShopRandom random = null,
            int quality = 2)
        {
            return new BattleShopContext(rules ?? Rules(1, 0, 0), Candidates, Candidates,
                id => Robot(id, quality), random ?? new ShopSequenceRandom());
        }

        internal static Dictionary<int, float> Globals()
        {
            return new Dictionary<int, float>
            {
                { 22, 20 }, { 23, 3 }, { 24, 4 }, { 25, 20 }, { 26, 50 },
                { 27, 10 }, { 28, 10 }, { 29, 5 }, { 30, 0.05f }, { 31, 10 },
                { 36, 6 }, { 37, 8 }, { 38, 12 }, { 39, 16 }, { 40, 20 }
            };
        }

        internal static Tables Tables(Dictionary<int, float> globals = null, int[][] packages = null, int dropType = 1)
        {
            globals = globals ?? Globals();
            packages = packages ?? new[]
            {
                new[] { 1, 2000, 2, 2000, 3, 2000 },
                new[] { 1, 2000, 2, 2000, 3, 2000 },
                new[] { 1, 2000, 2, 2000, 3, 2000 }
            };
            var globalBuffer = new ByteBuf();
            globalBuffer.WriteSize(globals.Count);
            foreach (var row in globals) { globalBuffer.WriteInt(row.Key); globalBuffer.WriteFloat(row.Value); }
            var dropBuffer = new ByteBuf();
            dropBuffer.WriteSize(packages.Length);
            for (var slot = 0; slot < packages.Length; slot++)
            {
                dropBuffer.WriteInt(slot + 1);
                dropBuffer.WriteInt(dropType);
                dropBuffer.WriteSize(packages[slot].Length);
                foreach (var value in packages[slot]) dropBuffer.WriteInt(value);
            }
            return new Tables(name =>
            {
                if (name == "globalcfg_tbglobal") return globalBuffer;
                if (name == "drop_tbdrop") return dropBuffer;
                throw new InvalidOperationException("测试不应读取其它表：" + name);
            });
        }

        internal static RobotEntity Purchase(BattleShopContext context, int slot = 0,
            BattleSide side = BattleSide.Player)
        {
            var offer = context.GetState(side).Slots[slot].Offer;
            RobotEntity robot;
            NUnit.Framework.Assert.AreEqual(ShopOperationResult.Success,
                context.TryPurchase(side, slot, offer.OfferId, out robot));
            return robot;
        }
    }

    internal sealed class ShopSequenceRandom : IShopRandom
    {
        private readonly Queue<double> _values = new Queue<double>();
        internal double DefaultValue;
        internal int SlotIndex;
        internal int DoubleCalls;
        internal int IntCalls;

        internal void Enqueue(params double[] values)
        {
            foreach (var value in values) _values.Enqueue(value);
        }

        public double NextDouble()
        {
            DoubleCalls++;
            return _values.Count == 0 ? DefaultValue : _values.Dequeue();
        }

        public int NextInt(int maxExclusive)
        {
            IntCalls++;
            if (SlotIndex < 0 || SlotIndex >= maxExclusive) throw new InvalidOperationException();
            return SlotIndex;
        }
    }
}
