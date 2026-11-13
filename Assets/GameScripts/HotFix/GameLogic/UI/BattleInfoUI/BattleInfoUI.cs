using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>MainCombatUI 中的信息区域，布局由预制体保存。</summary>
    public sealed class BattleInfoUI : UIWidget
    {
        private bool _hasBattle;
        private Button _settingsButton;
        private Button _leaveButton;

        /// <summary>当前窗口归属的对局；未绑定或关闭后为 0，供 host 判断关闭所有权。</summary>
        public long BattleId { get; private set; }
        public BattleInfoView View { get; private set; }
        public void BindMatch(MatchEntrySnapshot entry)
        {
            if (entry == null) return;
            View.SetPlayerInfo(entry.PlayerName, entry.Trophies);
            View.SetEnemyInfo(entry.OpponentName, entry.Trophies);
        }

        public void Bind(BattleProgressSnapshot snapshot)
        {
            _userDatas = new object[] { snapshot };
            OnRefresh();
        }
        protected override void ScriptGenerator()
        {
            View = gameObject.GetComponent<BattleInfoView>();
            _settingsButton = FindChildComponent<Button>("m_rect_BattleInfo/m_btn_Settings");
            _leaveButton = FindChildComponent<Button>("m_rect_BattleInfo/m_btn_Leave");
        }

        protected override void OnCreate()
        {
            if (_settingsButton != null) _settingsButton.onClick.AddListener(OnSettingsClicked);
            if (_leaveButton != null) _leaveButton.onClick.AddListener(OnLeaveClicked);
            GameSoundService.BindButtons(gameObject);
        }

        private void OnSettingsClicked()
        {
            GameModule.UI.ShowUIAsync<SettingsUI>(OwnerWindow);
        }

        private void OnLeaveClicked()
        {
            GameModule.UI.ShowUIAsync<LeaveBattleUI>(OwnerWindow);
        }

        protected override void RegisterEvent()
        {
            AddUIEvent<BattleProgressSnapshot>(BattleProgressEvents.Changed, OnBattleProgressChanged);
        }

        protected override void OnRefresh()
        {
            _hasBattle = UserData is BattleProgressSnapshot;
            BattleId = 0;
            if (UserData is BattleProgressSnapshot snapshot)
            {
                BattleId = snapshot.BattleId;
                Display(snapshot);
            }
            else
            {
                View.SetBattleProgress(1, 0f);
            }
        }

        private void OnBattleProgressChanged(BattleProgressSnapshot snapshot)
        {
            if (!_hasBattle || snapshot.BattleId != BattleId) return;
            Display(snapshot);
        }

        private void Display(BattleProgressSnapshot snapshot)
        {
            View.SetBattleProgress(snapshot.CurrentWave, (float)snapshot.ElapsedSeconds);
        }

        protected override void OnDestroy()
        {
            // AddUIEvent 由父窗口销毁 Widget 的流程统一移除。
            _hasBattle = false;
            if (_settingsButton != null) _settingsButton.onClick.RemoveListener(OnSettingsClicked);
            if (_leaveButton != null) _leaveButton.onClick.RemoveListener(OnLeaveClicked);
            _settingsButton = null;
            _leaveButton = null;
            BattleId = 0;
            _userDatas = null;
            View = null;
        }
    }
}
