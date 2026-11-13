using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameConfig;

namespace GameLogic
{
    /// <summary>真实商店会话。候选存档写入成功前不发布任何购买结果。</summary>
    public sealed class MysteriousStoreSession : MysteriousStorePreviewSession
    {
        private readonly IReadOnlyList<MysteriousStoreOfferData> _catalog;
        private readonly Func<MainMenuPlayerState, UniTask> _save;
        private readonly Func<DateTimeOffset> _clock;
        private bool _busy;
        private string _lastError = string.Empty;
        public override bool IsBusy => _busy;
        public override string LastError => _lastError;

        public override IReadOnlyList<MysteriousStoreHeldBuffData> HeldBuffs
        {
            get
            {
                var now = _clock();
                var views = new List<MysteriousStoreHeldBuffData>();
                foreach (var held in PlayerState.HeldAssists)
                {
                    if (held.IsExpired(now)) continue;
                    var offer = _catalog.FirstOrDefault(item => !item.IsInstantReward && item.BuffId == held.BuffId);
                    if (offer != null && held.Level <= offer.MaxLevel)
                        views.Add(new MysteriousStoreHeldBuffData(offer, held.Level, held.Slot));
                }
                return views.AsReadOnly();
            }
        }

        public static MysteriousStoreSession Create(MainMenuPlayerState player, Tables tables, IShopRandom random,
            Func<MainMenuPlayerState, UniTask> save, Func<DateTimeOffset> clock = null) =>
            new MysteriousStoreSession(player, AssistConfigAdapter.FromTables(tables), random, save, clock);

        public MysteriousStoreSession(MainMenuPlayerState player, IReadOnlyList<MysteriousStoreOfferData> catalog,
            IShopRandom random, Func<MainMenuPlayerState, UniTask> save, Func<DateTimeOffset> clock = null)
            : base(player, Draw(player, catalog, random, (clock ?? (() => DateTimeOffset.UtcNow))()))
        {
            _catalog = catalog.ToList().AsReadOnly();
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        private static IReadOnlyList<MysteriousStoreOfferData> Draw(MainMenuPlayerState player,
            IReadOnlyList<MysteriousStoreOfferData> catalog, IShopRandom random, DateTimeOffset now)
        {
            if (player == null || catalog == null || random == null) throw new ArgumentNullException();
            if (catalog.Any(offer => offer == null) || catalog.Select(offer => offer.GoodsId).Distinct().Count() != catalog.Count)
                throw new ArgumentException("商品目录不能为空或包含重复商品。");
            var valid = player.HeldAssists.Where(held => !held.IsExpired(now)).ToDictionary(held => held.BuffId);
            var pool = catalog.Where(offer => offer.IsInstantReward || !valid.TryGetValue(offer.BuffId, out var held) ||
                held.Level < offer.MaxLevel).ToList();
            var result = new List<MysteriousStoreOfferData>();
            while (result.Count < 3 && pool.Count > 0)
            {
                var index = random.NextInt(pool.Count);
                if (index < 0 || index >= pool.Count) throw new InvalidOperationException("神秘商店随机数越界。");
                result.Add(pool[index]); pool.RemoveAt(index);
            }
            return result.AsReadOnly();
        }

        public override MysteriousStorePurchaseResult TryBuy(int index) => MysteriousStorePurchaseResult.Unavailable;
        public override bool Remove(int buffId) => false;

        public override async UniTask<MysteriousStorePurchaseResult> TryBuyAsync(int index)
        {
            if (_busy) return MysteriousStorePurchaseResult.Busy;
            if (HasPurchased) return MysteriousStorePurchaseResult.SoldOut;
            if (index < 0 || index >= Offers.Count) return MysteriousStorePurchaseResult.Unavailable;
            _busy = true; _lastError = string.Empty;
            try
            {
                var now = _clock();
                var candidate = PlayerState.Clone();
                candidate.PruneExpiredAssists(now);
                var offer = Offers[index];
                var held = candidate.HeldAssists.FirstOrDefault(record => record.BuffId == offer.BuffId);
                var level = (held?.Level ?? 0) + 1;
                if (!offer.IsInstantReward && level > offer.MaxLevel) return MysteriousStorePurchaseResult.Unavailable;
                var price = offer.PriceAt(offer.IsInstantReward ? 1 : level);
                if (candidate.Coins < price) return MysteriousStorePurchaseResult.InsufficientBalance;
                if (!offer.IsInstantReward && held == null && candidate.HeldAssists.Count >= SlotCount)
                    return MysteriousStorePurchaseResult.SlotsFull;
                if (!candidate.TrySpendCoins(price)) return MysteriousStorePurchaseResult.InsufficientBalance;
                if (offer.IsInstantReward) candidate.AddEnergy(10);
                else
                {
                    var slot = held?.Slot ?? Enumerable.Range(0, SlotCount)
                        .First(value => candidate.HeldAssists.All(record => record.Slot != value));
                    var expiry = held?.ExpiresUtcTicks ?? HeldAssistRecord.NextBeijingMidnight(now).UtcTicks;
                    var records = candidate.HeldAssists.Where(record => record.BuffId != offer.BuffId).ToList();
                    records.Add(new HeldAssistRecord(offer.BuffId, level, slot, expiry));
                    candidate.ReplaceHeldAssists(records);
                }
                await _save(candidate);
                PlayerState.ApplyFrom(candidate);
                HasPurchased = true;
                return MysteriousStorePurchaseResult.Success;
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                return MysteriousStorePurchaseResult.SaveFailed;
            }
            finally { _busy = false; }
        }

        public override async UniTask<bool> RemoveAsync(int buffId)
        {
            if (_busy) return false;
            _busy = true; _lastError = string.Empty;
            try
            {
                var candidate = PlayerState.Clone();
                var removed = candidate.HeldAssists.Any(record => record.BuffId == buffId);
                if (!removed) return false;
                candidate.ReplaceHeldAssists(candidate.HeldAssists.Where(record => record.BuffId != buffId));
                candidate.PruneExpiredAssists(_clock());
                await _save(candidate);
                PlayerState.ApplyFrom(candidate);
                return true;
            }
            catch (Exception exception) { _lastError = exception.Message; return false; }
            finally { _busy = false; }
        }

        public override async UniTask<bool> RefreshExpiredAsync()
        {
            if (_busy) return false;
            _busy = true; _lastError = string.Empty;
            try
            {
                var candidate = PlayerState.Clone();
                if (!candidate.PruneExpiredAssists(_clock())) return false;
                await _save(candidate);
                PlayerState.ApplyFrom(candidate);
                return true;
            }
            catch (Exception exception) { _lastError = exception.Message; return false; }
            finally { _busy = false; }
        }
    }
}
