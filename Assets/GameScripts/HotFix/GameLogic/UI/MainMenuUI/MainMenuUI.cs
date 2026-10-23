using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>主界面第一版：只承载玩家状态、资源展示和开始战斗入口。</summary>
    [Window(UILayer.UI, "MainMenuUI", fullScreen: true)]
    public sealed class MainMenuUI : UIWindow
    {
        private Text _playerName;
        private Text _trophies;
        private Text _energy;
        private Text _energyCountdown;
        private Text _coins;
        private Button _startButton;
        private Button _energyPlusButton;
        private Button _coinsPlusButton;
        private bool _starting;

        public static MainMenuPlayerState State => GameApp.PlayerState;

        protected override void ScriptGenerator()
        {
            _playerName = FindText("PlayerName", "Name");
            _trophies = FindText("Trophies", "Cup", "Trophy");
            _energy = FindText("Energy", "Stamina");
            _energyCountdown = FindText("EnergyCountdown", "Countdown", "Recovery");
            _coins = FindText("Coins", "Money");
            _startButton = FindButton("StartBattleButton", "StartButton", "BattleButton");
            _energyPlusButton = FindButton("EnergyPlusButton", "EnergyPlus", "EnergyAdd");
            _coinsPlusButton = FindButton("CoinsPlusButton", "CoinsPlus", "CoinsAdd");
        }

        protected override void OnCreate()
        {
            if (_startButton != null) _startButton.onClick.AddListener(OnStartClicked);
            if (_energyPlusButton != null) _energyPlusButton.onClick.AddListener(OnEnergyPlusClicked);
            if (_coinsPlusButton != null) _coinsPlusButton.onClick.AddListener(OnCoinsPlusClicked);
        }

        protected override void OnRefresh() { RefreshView(); }

        protected override void OnUpdate()
        {
            State?.Refresh(DateTimeOffset.UtcNow);
            RefreshView();
        }

        private void OnStartClicked()
        {
            if (_starting || State == null) return;
            State.Refresh(DateTimeOffset.UtcNow);
            if (!State.TrySpendEnergy(MainMenuPlayerState.DefaultEnergyCost))
            {
                SetStatus("当前体力不足");
                return;
            }
            _starting = true;
            if (_startButton != null) _startButton.interactable = false;
            GameApp.BeginBattleFromMainMenu(this).Forget();
        }

        private void OnEnergyPlusClicked()
        {
            State?.AddEnergy(5);
            RefreshView();
        }

        private void OnCoinsPlusClicked()
        {
            State?.AddCoins(100);
            RefreshView();
        }

        public void ResetStartButton()
        {
            _starting = false;
            if (_startButton != null) _startButton.interactable = true;
            RefreshView();
        }

        private void RefreshView()
        {
            if (State == null) return;
            if (_playerName != null) _playerName.text = State.PlayerName;
            if (_trophies != null) _trophies.text = State.Trophies.ToString();
            if (_energy != null) _energy.text = $"{State.Energy}/{State.EnergyCapacity}";
            if (_coins != null) _coins.text = State.Coins.ToString();
            if (_energyCountdown != null)
            {
                var remaining = State.Energy >= State.EnergyCapacity
                    ? TimeSpan.Zero
                    : State.NextEnergyRecoveryUtc - DateTimeOffset.UtcNow;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                _energyCountdown.text = remaining == TimeSpan.Zero
                    ? "已满"
                    : $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
            }
        }

        private void SetStatus(string message)
        {
            if (_energyCountdown != null) _energyCountdown.text = message;
        }

        private Text FindText(params string[] names)
        {
            foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
                foreach (var name in names)
                    if (string.Equals(text.name, name, StringComparison.OrdinalIgnoreCase)) return text;
            return null;
        }

        private Button FindButton(params string[] names)
        {
            foreach (var button in gameObject.GetComponentsInChildren<Button>(true))
                foreach (var name in names)
                    if (string.Equals(button.name, name, StringComparison.OrdinalIgnoreCase)) return button;
            return null;
        }

        protected override void OnDestroy()
        {
            if (_startButton != null) _startButton.onClick.RemoveListener(OnStartClicked);
            if (_energyPlusButton != null) _energyPlusButton.onClick.RemoveListener(OnEnergyPlusClicked);
            if (_coinsPlusButton != null) _coinsPlusButton.onClick.RemoveListener(OnCoinsPlusClicked);
        }
    }
}
