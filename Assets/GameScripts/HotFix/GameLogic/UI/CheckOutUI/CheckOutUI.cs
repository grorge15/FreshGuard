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
        private Text _playerTrophies;
        private RectTransform _playerCup;
        private Text _opponentTrophies;
        private RectTransform _opponentCup;
        private RectTransform _content;
        private Vector3 _contentBaseScale;
        private Vector2 _contentBasePosition;
        private Vector2 _lastWindowSize;
        private MatchInfoWidget _playerInfo;
        private MatchInfoWidget _opponentInfo;
        private BattleSettlementReward _reward;
        private Func<UniTask> _returnToMain;
        private bool _busy;
        private int _rewardSoundStage;

        protected override void ScriptGenerator()
        {
            _victory = FindChild("m_go_Victory").gameObject;
            _defeat = FindChild("m_go_Defeat").gameObject;
            const string content = "m_rect_Content/";
            _content = FindChildComponent<RectTransform>("m_rect_Content");
            _contentBaseScale = _content.localScale;
            _contentBasePosition = _content.anchoredPosition;
            _playerCrown = FindChild(content + "m_rect_Player/m_img_Crown").gameObject;
            _opponentCrown = FindChild(content + "m_rect_Opponent/m_img_Crown").gameObject;
            _victoryVs = FindChild(content + "m_img_VictoryVS").gameObject;
            _defeatVs = FindChild(content + "m_img_DefeatVS").gameObject;
            _victoryRewards = FindChild(content + "m_rect_Rewards/m_img_VictoryBG").gameObject;
            _defeatRewards = FindChild(content + "m_rect_Rewards/m_img_DefeatBG").gameObject;
            _rewardTitle = FindChildComponent<Text>(content + "m_rect_Rewards/m_text_Title");
            _rewardAmount = FindChildComponent<Text>(content + "m_rect_Rewards/m_item_Reward/m_text_Amount");
            _playerTrophies = FindChildComponent<Text>(content + "m_rect_Player/m_text_Trophies");
            _playerCup = FindChildComponent<RectTransform>(content + "m_rect_Player/m_img_Cup");
            _opponentTrophies = FindChildComponent<Text>(content + "m_rect_Opponent/m_text_Trophies");
            _opponentCup = FindChildComponent<RectTransform>(content + "m_rect_Opponent/m_img_Cup");
            _doubleReward = FindChildComponent<Button>(content + "m_btn_DoubleReward");
            _returnMain = FindChildComponent<Button>(content + "m_btn_ReturnMain");
        }

        protected override void OnCreate()
        {
            _playerInfo = CreateWidget<MatchInfoWidget>("m_rect_Content/m_rect_Player");
            _opponentInfo = CreateWidget<MatchInfoWidget>("m_rect_Content/m_rect_Opponent");
            foreach (var text in gameObject.GetComponentsInChildren<Text>(true))
            {
                if (text.font != null) continue;
                if (_font == null)
                    _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 28);
                text.font = _font;
            }
            _doubleReward.onClick.AddListener(OnDoubleReward);
            _returnMain.onClick.AddListener(OnReturnMain);
            GameSoundService.BindButtons(gameObject);
        }

        protected override void OnRefresh()
        {
            _reward = null;
            _returnToMain = null;
            _busy = false;
            _rewardSoundStage = 0;
            _playerTrophies.text = string.Empty;
            _opponentTrophies.text = string.Empty;
            _doubleReward.interactable = _returnMain.interactable = false;
            Render(UserData is BattleSettlementSnapshot snapshot && snapshot.Result == BattleSettlementResult.Victory);
            FitContentToWindow();
            if (UserData is BattleSettlementSnapshot result)
                GameSoundService.Play(result.Result == BattleSettlementResult.Victory ? GameSound.Victory : GameSound.Defeat);
        }

        protected override void OnUpdate()
        {
            if (_lastWindowSize != rectTransform.rect.size) FitContentToWindow();
        }

        private void FitContentToWindow()
        {
            var size = rectTransform.rect.size;
            _lastWindowSize = size;
            if (size.x <= 48f || size.y <= 48f) return;
            var design = Vector2.Scale(_content.rect.size, new Vector2(_contentBaseScale.x, _contentBaseScale.y));
            if (design.x <= 0f || design.y <= 0f) return;
            var fit = Mathf.Min(1f, (size.x - 48f) / design.x, (size.y - 48f) / design.y);
            _content.localScale = _contentBaseScale * fit;
            _content.anchoredPosition = _contentBasePosition * fit;
        }

        /// <summary>复用匹配页的信息组件，展示本局入场时确定的双方资料和携带 BUFF。</summary>
        public void BindMatch(MatchEntrySnapshot entry)
        {
            if (entry == null) return;
            _playerInfo.Render(entry.PlayerName, entry.PlayerAvatar, entry.Trophies, entry.PlayerBuffs);
            _opponentInfo.Render(entry.OpponentName, entry.OpponentAvatar, entry.Trophies, entry.OpponentBuffs);
            if (_reward != null) RefreshTrophies();
        }

        public void BindReward(BattleSettlementReward reward, Func<UniTask> returnToMain)
        {
            _reward = reward;
            _returnToMain = returnToMain;
            RefreshTrophies();
            RefreshReward();
        }

        private void RefreshTrophies()
        {
            RenderTrophies(_playerTrophies, _playerCup, _reward.TrophiesBefore, _reward.TrophyDelta);
            RenderTrophies(_opponentTrophies, _opponentCup, _reward.OpponentTrophiesBefore, _reward.OpponentTrophyDelta);
        }

        private static void RenderTrophies(Text text, RectTransform cup, int before, int delta)
        {
            var color = delta > 0 ? "#31D158" : delta < 0 ? "#FF3B30" : "#CBD5E1";
            text.supportRichText = true;
            text.text = $"{before} <color={color}>{(delta > 0 ? "+" : "")}{delta}</color>";
            // 将奖杯、杯数和本局增减作为一整行居中，长杯数仍由原有字号适配处理。
            var textWidth = Mathf.Min(164f, text.preferredWidth + 2f);
            var textRect = text.rectTransform;
            var gap = 6f;
            textRect.sizeDelta = new Vector2(textWidth, textRect.sizeDelta.y);
            textRect.anchoredPosition = new Vector2((cup.sizeDelta.x + gap) * 0.5f, textRect.anchoredPosition.y);
            cup.anchoredPosition = new Vector2(-(textWidth + gap) * 0.5f, cup.anchoredPosition.y);
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
                var soundStage = reward.IsDoubled ? 2 : 1;
                if (_rewardSoundStage < soundStage)
                {
                    _rewardSoundStage = soundStage;
                    GameSoundService.Play(GameSound.RewardClaim, pitch: soundStage == 2 ? 1.12f : 1f);
                }
                _rewardTitle.text = "获得奖励";
                if (!doubleReward) await returnToMain();
            }
            catch (Exception exception)
            {
                GameSoundService.Play(GameSound.Error);
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
