using System;

namespace GameLogic
{
    /// <summary>每方战场的助力派生数值；生命周期仍由战场Buff容器管理。</summary>
    public sealed class BattleAssistState
    {
        private int _speedStep, _damageStep;
        private float _speedRate, _speedCap, _damageRate, _damageCap;
        public long BattleId { get; }
        public BattleSide Side { get; }
        public int Combo { get; private set; }
        public float OpeningBallSpeedRate { get; internal set; }
        public float ComboBallSpeedRate { get; private set; }
        public float RobotDamageRate { get; private set; }
        public float NormalBallSpeedRate => OpeningBallSpeedRate + ComboBallSpeedRate;
        public int? NormalBallGlassLossOverride { get; internal set; }
        public int ExtraBerserkBallCount { get; internal set; }

        internal BattleAssistState(long battleId, BattleSide side) { BattleId = battleId; Side = side; }

        public void SetCombo(int combo)
        {
            if (combo < 0) throw new ArgumentOutOfRangeException(nameof(combo));
            Combo = combo;
            ComboBallSpeedRate = _speedStep > 0 ? Math.Min(_speedCap, (combo / _speedStep) * _speedRate) : 0f;
            RobotDamageRate = _damageStep > 0 ? Math.Min(_damageCap, (combo / _damageStep) * _damageRate) : 0f;
        }

        internal void ConfigureComboSpeed(int step, float rate, float cap)
        {
            _speedStep = step; _speedRate = rate; _speedCap = cap;
            SetCombo(Combo);
        }

        internal void ConfigureComboDamage(int step, float rate, float cap)
        {
            _damageStep = step; _damageRate = rate; _damageCap = cap;
            SetCombo(Combo);
        }

        internal void Clear()
        {
            OpeningBallSpeedRate = 0f; NormalBallGlassLossOverride = null; ExtraBerserkBallCount = 0;
            _speedStep = _damageStep = 0; _speedRate = _speedCap = _damageRate = _damageCap = 0f;
            SetCombo(0);
        }
    }
}
