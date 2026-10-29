using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>主界面展示、开始战斗入口及结算后临时神秘商店的归属窗口。</summary>
    [Window(UILayer.UI, "MainMenuUI", fullScreen: true)]
    public sealed class MainMenuUI : UIWindow
    {
        private Text _playerName;
        private Text _trophies;
        private Text _energy;
        private Text _energyCountdown;
        private GameObject _energyCool;
        private Text _coins;
        private Button _startButton;
        private Button _energyPlusButton;
        private Button _coinsPlusButton;
        private bool _starting;
        private string _statusMessage;
        private float _statusUntil;
        private MysteriousStoreWidget _mysteriousStore;
        private readonly CancellationTokenSource _storeLifetime = new CancellationTokenSource();
        private long _lastMysteriousStoreBattleId;
        private bool _openingMysteriousStore;
        private bool _disposed;

        public static MainMenuPlayerState State => GameApp.PlayerState;

        protected override void ScriptGenerator()
        {
            _playerName = FindText("PlayerName", "Name");
            _trophies = FindText("Trophies", "Cup", "Trophy");
            _energy = FindText("Energy", "Stamina");
            _energyCountdown = FindText("EnergyCountdown", "Countdown", "Recovery");
            _energyCool = transform.Find("EnergyPanel/energyCool")?.gameObject;
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
            var previousEnergy = State?.Energy;
            State?.Refresh(DateTimeOffset.UtcNow);
            if (previousEnergy != State?.Energy) GameApp.SavePlayerStateAsync().Forget();
            RefreshView();
        }

        private void OnStartClicked()
        {
            if (_starting || _openingMysteriousStore || _mysteriousStore != null || State == null) return;
            _starting = true;
            if (_startButton != null) _startButton.interactable = false;
            GameApp.BeginBattleFromMainMenu(this).Forget();
        }

        private void OnEnergyPlusClicked()
        {
            State?.AddEnergy(5);
            GameApp.SavePlayerStateAsync().Forget();
            RefreshView();
        }

        private void OnCoinsPlusClicked()
        {
            State?.AddCoins(100);
            GameApp.SavePlayerStateAsync().Forget();
            RefreshView();
        }

        public void ResetStartButton()
        {
            _starting = false;
            RefreshView();
        }

        public UniTask ShowMysteriousStorePreviewAsync(long battleId) =>
            ShowMysteriousStorePreviewAsync(battleId, MysteriousStorePreviewSession.CreateDefault());

        /// <summary>允许传入独立预览数据；不读取或写入真实玩家商店状态。</summary>
        public async UniTask ShowMysteriousStorePreviewAsync(long battleId, MysteriousStorePreviewSession session)
        {
            if (_disposed || IsDestroyed || battleId <= 0 || battleId == _lastMysteriousStoreBattleId ||
                _openingMysteriousStore || _mysteriousStore != null) return;
            _lastMysteriousStoreBattleId = battleId;
            if (session == null || session.Offers.Count == 0) return;
            _openingMysteriousStore = true;
            RefreshView();
            GameObject instance = null;
            MysteriousStoreWidget widget = null;
            try
            {
                // 先持有原始实例，取消或节点绑定失败时也能清理，不留下透明拦截层。
                instance = await UIModule.Resource.LoadGameObjectAsync("MysteriousStoreWidget", transform, _storeLifetime.Token);
                if (_disposed || IsDestroyed || gameObject == null) return;
                if (instance == null) throw new InvalidOperationException("神秘商店 Prefab 加载失败。");
                instance.SetActive(false);
                widget = new MysteriousStoreWidget();
                if (!widget.Create(this, instance, false)) throw new InvalidOperationException("神秘商店节点初始化失败。");
                widget.Bind(session, CloseMysteriousStore);
                widget.transform.SetAsLastSibling();
                _mysteriousStore = widget;
                widget.Visible = true;
                // 所有权转移给主菜单和框架；finally 仅清理尚未移交的对象。
                widget = null;
                instance = null;
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!_disposed && !IsDestroyed)
                {
                    ShowStatus("神秘商店加载失败");
                    Debug.LogWarning("神秘商店加载失败：" + exception.Message);
                }
            }
            finally
            {
                if (widget != null && widget.Parent != null) widget.Destroy();
                else if (instance != null) UnityEngine.Object.Destroy(instance);
                _openingMysteriousStore = false;
                if (!_disposed && !IsDestroyed) RefreshView();
            }
        }

        private void CloseMysteriousStore()
        {
            var widget = _mysteriousStore;
            _mysteriousStore = null;
            // Destroy 延迟到帧末，先隐藏以立即解除射线遮挡。
            if (widget != null) { widget.Visible = false; widget.Destroy(); }
            RefreshView();
        }

        private void RefreshView()
        {
            if (_startButton != null) _startButton.interactable = !_starting && !_openingMysteriousStore && _mysteriousStore == null;
            if (State == null) return;
            if (_energyCool != null) _energyCool.SetActive(State.Energy < State.EnergyCapacity);
            if (_playerName != null) _playerName.text = State.PlayerName;
            if (_trophies != null) _trophies.text = State.Trophies.ToString();
            if (_energy != null) _energy.text = $"{State.Energy}/{State.EnergyCapacity}";
            if (_coins != null) _coins.text = State.Coins.ToString();
            if (_energyCountdown != null)
            {
                if (!string.IsNullOrEmpty(_statusMessage) && Time.unscaledTime < _statusUntil)
                {
                    _energyCountdown.text = _statusMessage;
                    return;
                }
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
            _statusMessage = message;
            _statusUntil = Time.unscaledTime + 3f;
            if (_energyCountdown != null) _energyCountdown.text = message;
        }

        public void ShowStatus(string message) { SetStatus(message); }

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
            _disposed = true;
            _storeLifetime.Cancel();
            _storeLifetime.Dispose();
            // 子 Widget 已由 UIWindow 先行清理，不重复销毁。
            _mysteriousStore = null;
            if (_startButton != null) _startButton.onClick.RemoveListener(OnStartClicked);
            if (_energyPlusButton != null) _energyPlusButton.onClick.RemoveListener(OnEnergyPlusClicked);
            if (_coinsPlusButton != null) _coinsPlusButton.onClick.RemoveListener(OnCoinsPlusClicked);
        }
    }
}
