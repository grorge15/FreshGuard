using System;

namespace GameLogic
{
    /// <summary>主界面展示与开始战斗按钮共用的玩家轻量状态。</summary>
    public sealed class MainMenuPlayerState
    {
        public const int DefaultEnergyCapacity = 30;
        public const int DefaultEnergyCost = 5;
        public const int DefaultCoins = 0;
        public const int DefaultTrophies = 2500;
        public const string DefaultPlayerName = "料理守护者";

        private readonly TimeSpan _recoveryInterval;
        private readonly int _energyCapacity;
        private DateTimeOffset _lastEnergyUpdateUtc;

        public MainMenuPlayerState(int energy, TimeSpan recoveryInterval, int energyCapacity, int coins,
            int trophies, DateTimeOffset utcNow)
        {
            if (recoveryInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(recoveryInterval));
            if (energyCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(energyCapacity));
            _recoveryInterval = recoveryInterval;
            _energyCapacity = energyCapacity;
            Energy = Clamp(energy, 0, energyCapacity);
            Coins = Math.Max(0, coins);
            Trophies = Math.Max(0, trophies);
            _lastEnergyUpdateUtc = utcNow.ToUniversalTime();
        }

        public int Energy { get; private set; }
        public int EnergyCapacity => _energyCapacity;
        public int Coins { get; private set; }
        public int Trophies { get; private set; }
        public string PlayerName { get; set; } = DefaultPlayerName;
        public TimeSpan RecoveryInterval => _recoveryInterval;
        public DateTimeOffset LastEnergyUpdateUtc => _lastEnergyUpdateUtc;
        public DateTimeOffset NextEnergyRecoveryUtc =>
            Energy >= _energyCapacity ? _lastEnergyUpdateUtc : _lastEnergyUpdateUtc + _recoveryInterval;

        public bool TrySpendEnergy(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Refresh(DateTimeOffset.UtcNow);
            if (Energy < amount) return false;
            Energy -= amount;
            return true;
        }

        public void AddEnergy(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Refresh(DateTimeOffset.UtcNow);
            Energy = Clamp(Energy + amount, 0, _energyCapacity);
        }

        public void AddCoins(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Coins = checked(Coins + amount);
        }

        public void Refresh(DateTimeOffset utcNow)
        {
            utcNow = utcNow.ToUniversalTime();
            if (Energy >= _energyCapacity)
            {
                _lastEnergyUpdateUtc = utcNow;
                return;
            }

            var elapsed = utcNow - _lastEnergyUpdateUtc;
            if (elapsed <= TimeSpan.Zero) return;
            var recovered = (int)(elapsed.Ticks / _recoveryInterval.Ticks);
            if (recovered <= 0) return;
            Energy = Clamp(Energy + recovered, 0, _energyCapacity);
            _lastEnergyUpdateUtc += TimeSpan.FromTicks(recovered * _recoveryInterval.Ticks);
            if (Energy >= _energyCapacity) _lastEnergyUpdateUtc = utcNow;
        }

        private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    }
}
