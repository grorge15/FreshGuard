using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>结算展示窗口。胜负由战斗层提供，不在窗口里计算或发奖。</summary>
    [Window(UILayer.Top, "CheckOutUI", fullScreen: true)]
    public sealed class CheckOutUI : UIWindow
    {
        private GameObject _victory;
        private GameObject _defeat;
        private GameObject _playerCrown;
        private GameObject _opponentCrown;
        private GameObject _victoryVs;
        private GameObject _defeatVs;
        private GameObject _victoryRewards;
        private GameObject _defeatRewards;
        private Text _rewardTitle;
        private Font _font;
        private Button _doubleReward;
        private Button _returnMain;
        private Text _rewardAmount;
        private BattleSettlementReward _reward;
        private Func<UniTask> _returnToMain;
        private bool _busy;

        protected override void ScriptGenerator()
        {
            _victory = FindChild("m_go_Victory").gameObject;
            _defeat = FindChild("m_go_Defeat").gameObject;
            const string content = "m_rect_Content/";
            _playerCrown = FindChild(content + "m_rect_Player/m_img_Crown").gameObject;
            _opponentCrown = FindChild(content + "m_rect_Opponent/m_img_Crown").gameObject;
            _victoryVs = FindChild(content + "m_img_VictoryVS").gameObject;
            _defeatVs = FindChild(content + "m_img_DefeatVS").gameObject;
            _victoryRewards = FindChild(content + "m_rect_Rewards/m_img_VictoryBG").gameObject;
            _defeatRewards = FindChild(content + "m_rect_Rewards/m_img_DefeatBG").gameObject;
            _rewardTitle = FindChildComponent<Text>(content + "m_rect_Rewards/m_text_Title");
            _rewardAmount = FindChildComponent<Text>(content + "m_rect_Rewards/m_item_Reward/m_text_Amount");
            _doubleReward = FindChildComponent<Button>(content + "m_btn_DoubleReward");
            _returnMain = FindChildComponent<Button>(content + "m_btn_ReturnMain");
        }

        protected override void OnCreate()
        {
            foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
            {
                if (text.font != null) continue;
                if (_font == null)
                    _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 28);
                text.font = _font;
            }
            _doubleReward.onClick.AddListener(OnDoubleReward);
            _returnMain.onClick.AddListener(OnReturnMain);
        }

        protected override void OnRefresh()
        {
            _reward = null;
            _returnToMain = null;
            _busy = false;
            _doubleReward.interactable = _returnMain.interactable = false;
            Render(UserData is BattleSettlementSnapshot snapshot && snapshot.Result == BattleSettlementResult.Victory);
        }

        public void BindReward(BattleSettlementReward reward, Func<UniTask> returnToMain)
        {
            _reward = reward;
            _returnToMain = returnToMain;
            RefreshReward();
        }

        private void RefreshReward()
        {
            _rewardAmount.text = _reward.DisplayAmount.ToString();
            _doubleReward.interactable = !_busy && (!_reward.IsDoubled || !_reward.IsSaved);
            _returnMain.interactable = !_busy;
        }

        private void OnDoubleReward() => ClaimAsync(true).Forget();
        private void OnReturnMain() => ClaimAsync(false).Forget();

        private async UniTask ClaimAsync(bool doubleReward)
        {
            if (_busy || _reward == null || IsDestroyed) return;
            _busy = true;
            var reward = _reward;
            var returnToMain = _returnToMain;
            RefreshReward();
            try
            {
                await reward.ClaimAsync(doubleReward);
                if (IsDestroyed || reward != _reward) return;
                _rewardTitle.text = "获得奖励";
                if (!doubleReward) await returnToMain();
            }
            catch (Exception exception)
            {
                Debug.LogError("结算奖励保存或返回失败：" + exception.Message);
                if (!IsDestroyed && reward == _reward) _rewardTitle.text = "操作失败，请点击重试";
            }
            finally
            {
                if (!IsDestroyed && reward == _reward)
                {
                    _busy = false;
                    RefreshReward();
                }
            }
        }

        public void Render(bool victory)
        {
            _victory.SetActive(victory);
            _defeat.SetActive(!victory);
            _playerCrown.SetActive(victory);
            _opponentCrown.SetActive(!victory);
            _victoryVs.SetActive(victory);
            _defeatVs.SetActive(!victory);
            _victoryRewards.SetActive(victory);
            _defeatRewards.SetActive(!victory);
            _rewardTitle.color = victory ? new Color(0.39f, 0.17f, 0.04f) : new Color(0.19f, 0.12f, 0.36f);
        }

        protected override void OnDestroy()
        {
            if (_doubleReward != null) _doubleReward.onClick.RemoveListener(OnDoubleReward);
            if (_returnMain != null) _returnMain.onClick.RemoveListener(OnReturnMain);
            if (_font != null) UnityEngine.Object.Destroy(_font);
        }
    }
}
