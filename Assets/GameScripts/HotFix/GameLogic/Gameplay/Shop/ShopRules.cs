using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameConfig;

namespace GameLogic
{
    /// <summary>随机源的浮点范围为[0,1)，整数范围为[0,maxExclusive)。</summary>
    public interface IShopRandom
    {
        double NextDouble();
        int NextInt(int maxExclusive);
    }

    internal sealed class ShopRandom : IShopRandom
    {
        private readonly Random _random = new Random();
        public double NextDouble() => _random.NextDouble();
        public int NextInt(int maxExclusive) => _random.Next(maxExclusive);
    }

    /// <summary>单局不可变规则。生产Drop按槽配置、候选序号映射；机器人ID权重构造仅是测试便捷入口。</summary>
    public sealed class ShopRules
    {
        private readonly IReadOnlyDictionary<int, double> _sharedRobotWeights;
        public int InitialCoins { get; }
        public int SlotCount => 3;
        public int RefreshPrice { get; }
        public int MaxRobotLevel => 5;
        public int AdditionalRerollLimit { get; }
        public double SecondStarProbability { get; }
        public IReadOnlyList<IReadOnlyDictionary<int, double>> SlotCandidateWeights { get; }
        public IReadOnlyDictionary<int, int> QualityPrices { get; }
        public IReadOnlyDictionary<ShopEnemyKind, int> EnemyRewards { get; }
        public IReadOnlyDictionary<ShopGlassKind, int> GlassRewards { get; }

        public ShopRules(IReadOnlyDictionary<int, double> dropWeights)
            : this(DefaultSlotWeights(), DefaultPrices())
        {
            if (dropWeights == null) throw new ArgumentNullException(nameof(dropWeights));
            _sharedRobotWeights = CopyWeights(dropWeights, false);
        }

        /// <summary>每槽权重字典的key是1..3候选序号，顺序对应本方传入候选池。</summary>
        public ShopRules(IReadOnlyList<IReadOnlyDictionary<int, double>> slotCandidateWeights,
            IReadOnlyDictionary<int, int> qualityPrices, int initialCoins = 20, int refreshPrice = 5,
            double secondStarProbability = 0.05, int additionalRerollLimit = 10,
            IReadOnlyDictionary<ShopEnemyKind, int> enemyRewards = null,
            IReadOnlyDictionary<ShopGlassKind, int> glassRewards = null)
        {
            if (slotCandidateWeights == null) throw new ArgumentNullException(nameof(slotCandidateWeights));
            if (slotCandidateWeights.Count != SlotCount) throw new ArgumentException("必须配置三个商品槽。", nameof(slotCandidateWeights));
            if (initialCoins < 0) throw new ArgumentOutOfRangeException(nameof(initialCoins));
            if (refreshPrice < 0) throw new ArgumentOutOfRangeException(nameof(refreshPrice));
            if (additionalRerollLimit < 0) throw new ArgumentOutOfRangeException(nameof(additionalRerollLimit));
            if (double.IsNaN(secondStarProbability) || secondStarProbability < 0 || secondStarProbability > 1)
                throw new ArgumentOutOfRangeException(nameof(secondStarProbability));
            var slots = new List<IReadOnlyDictionary<int, double>>();
            foreach (var weights in slotCandidateWeights) slots.Add(CopyWeights(weights, true));
            SlotCandidateWeights = slots.AsReadOnly();
            QualityPrices = CopyAmounts(qualityPrices, new[] { 2, 3, 4, 5, 6 }, int.MaxValue / 2);
            EnemyRewards = CopyAmounts(enemyRewards ?? new Dictionary<ShopEnemyKind, int>
            {
                { ShopEnemyKind.Normal, 4 }, { ShopEnemyKind.Elite, 20 }, { ShopEnemyKind.Boss, 50 }
            }, new[] { ShopEnemyKind.Normal, ShopEnemyKind.Elite, ShopEnemyKind.Boss });
            GlassRewards = CopyAmounts(glassRewards ?? new Dictionary<ShopGlassKind, int>
            {
                { ShopGlassKind.Normal, 10 }, { ShopGlassKind.Rainbow, 10 }
            }, new[] { ShopGlassKind.Normal, ShopGlassKind.Rainbow });
            InitialCoins = initialCoins;
            RefreshPrice = refreshPrice;
            AdditionalRerollLimit = additionalRerollLimit;
            SecondStarProbability = secondStarProbability;
        }

        /// <summary>只解析传入Tables，不主动加载资源或访问ConfigSystem。</summary>
        public static ShopRules FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var slots = ReadInteger(tables, 23);
            if (slots != 3) throw new ArgumentException("当前商店只支持三个商品槽，Global 23必须为3。", nameof(tables));
            var weights = new List<IReadOnlyDictionary<int, double>>();
            for (var slot = 1; slot <= slots; slot++)
            {
                var drop = tables.TbDrop.GetOrDefault(slot);
                if (drop == null || drop.Type != 1 || drop.Package == null || drop.Package.Length == 0 || drop.Package.Length % 2 != 0)
                    throw new ArgumentException("无效Drop：" + slot + "；需要type=1和非空成对package。", nameof(tables));
                var slotWeights = new Dictionary<int, double>();
                for (var index = 0; index < drop.Package.Length; index += 2)
                {
                    var ordinal = drop.Package[index];
                    if (ordinal < 1 || ordinal > 3 || slotWeights.ContainsKey(ordinal) || drop.Package[index + 1] < 0)
                        throw new ArgumentException("Drop候选序号须为1..3且不重复，权重须非负：" + slot, nameof(tables));
                    slotWeights.Add(ordinal, drop.Package[index + 1]);
                }
                weights.Add(slotWeights);
            }
            var prices = new Dictionary<int, int>();
            for (var quality = 2; quality <= 6; quality++) prices.Add(quality, ReadInteger(tables, quality + 34));
            return new ShopRules(weights, prices, ReadInteger(tables, 22), ReadInteger(tables, 29),
                ReadValue(tables, 30), ReadInteger(tables, 31),
                new Dictionary<ShopEnemyKind, int>
                {
                    { ShopEnemyKind.Normal, ReadInteger(tables, 24) },
                    { ShopEnemyKind.Elite, ReadInteger(tables, 25) },
                    { ShopEnemyKind.Boss, ReadInteger(tables, 26) }
                }, new Dictionary<ShopGlassKind, int>
                {
                    { ShopGlassKind.Normal, ReadInteger(tables, 27) },
                    { ShopGlassKind.Rainbow, ReadInteger(tables, 28) }
                });
        }

        public double GetCandidateWeight(int slotId, int candidateIndex, int robotId)
        {
            if (slotId < 0 || slotId >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slotId));
            if (candidateIndex < 0 || candidateIndex >= 3) throw new ArgumentOutOfRangeException(nameof(candidateIndex));
            double weight;
            return _sharedRobotWeights != null
                ? (_sharedRobotWeights.TryGetValue(robotId, out weight) ? weight : 0)
                : (SlotCandidateWeights[slotId].TryGetValue(candidateIndex + 1, out weight) ? weight : 0);
        }

        public int GetPrice(int qualityId, int level)
        {
            if (level != 1 && level != 2) throw new ArgumentOutOfRangeException(nameof(level));
            int price;
            if (!QualityPrices.TryGetValue(qualityId, out price)) throw new ArgumentOutOfRangeException(nameof(qualityId));
            return checked(price * level);
        }

        private static double ReadValue(Tables tables, int id)
        {
            var row = tables.TbGlobal.GetOrDefault(id);
            if (row == null || float.IsNaN(row.Value) || float.IsInfinity(row.Value))
                throw new ArgumentException("Global缺失或非有限值：" + id, nameof(tables));
            return row.Value;
        }

        private static int ReadInteger(Tables tables, int id)
        {
            var value = ReadValue(tables, id);
            if (value < 0 || value > int.MaxValue || value != Math.Truncate(value))
                throw new ArgumentException("Global必须为非负且在int范围内的整数：" + id, nameof(tables));
            return (int)value;
        }

        private static IReadOnlyDictionary<int, double> CopyWeights(IReadOnlyDictionary<int, double> source, bool ordinal)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Dictionary<int, double>();
            var hasPositive = false;
            foreach (var pair in source)
            {
                if (pair.Key <= 0 || (ordinal && pair.Key > 3) || double.IsNaN(pair.Value) || double.IsInfinity(pair.Value) || pair.Value < 0)
                    throw new ArgumentOutOfRangeException(nameof(source), "权重须有限非负，候选序号须在1..3内。");
                copy.Add(pair.Key, pair.Value);
                hasPositive |= pair.Value > 0;
            }
            if (ordinal && !hasPositive) throw new ArgumentException("每个商品槽至少需要一个正权重候选。", nameof(source));
            return new ReadOnlyDictionary<int, double>(copy);
        }

        private static IReadOnlyDictionary<T, int> CopyAmounts<T>(IReadOnlyDictionary<T, int> source, T[] keys, int maximum = int.MaxValue)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var copy = new Dictionary<T, int>();
            foreach (var key in keys)
            {
                int amount;
                if (!source.TryGetValue(key, out amount) || amount < 0 || amount > maximum)
                    throw new ArgumentException("价格或奖励配置缺失、为负或越界：" + key, nameof(source));
                copy.Add(key, amount);
            }
            return new ReadOnlyDictionary<T, int>(copy);
        }

        private static IReadOnlyDictionary<int, int> DefaultPrices()
        {
            return new Dictionary<int, int> { { 2, 6 }, { 3, 8 }, { 4, 12 }, { 5, 16 }, { 6, 20 } };
        }

        private static IReadOnlyList<IReadOnlyDictionary<int, double>> DefaultSlotWeights()
        {
            return new IReadOnlyDictionary<int, double>[]
            {
                new Dictionary<int, double> { { 1, 1 }, { 2, 1 }, { 3, 1 } },
                new Dictionary<int, double> { { 1, 1 }, { 2, 1 }, { 3, 1 } },
                new Dictionary<int, double> { { 1, 1 }, { 2, 1 }, { 3, 1 } }
            };
        }
    }
}
