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

        protected override void ScriptGenerator()
        {
            _victory = FindChild("m_go_Victory").gameObject;
            _defeat = FindChild("m_go_Defeat").gameObject;
            const string content = "m_rect_Content/";
            _playerCrown = FindChild(content + "m_rect_Player/m_img_Crown").gameObject;
            _opponentCrown = FindChild(content + "m_rect_Opponent/m_img_Crown").gameObject;
            _victoryVs = FindChild(content + "m_text_VictoryVS").gameObject;
            _defeatVs = FindChild(content + "m_img_DefeatVS").gameObject;
            _victoryRewards = FindChild(content + "m_rect_Rewards/m_img_VictoryBG").gameObject;
            _defeatRewards = FindChild(content + "m_rect_Rewards/m_img_DefeatBG").gameObject;
            _rewardTitle = FindChildComponent<Text>(content + "m_rect_Rewards/m_text_Title");
        }

        protected override void OnCreate()
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 28);
            foreach (var text in gameObject.GetComponentsInChildren<Text>(true)) text.font = _font;
        }

        protected override void OnRefresh()
        {
            Render(UserData is BattleSettlementSnapshot snapshot && snapshot.Result == BattleSettlementResult.Victory);
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
            if (_font != null) Object.Destroy(_font);
        }
    }
}
