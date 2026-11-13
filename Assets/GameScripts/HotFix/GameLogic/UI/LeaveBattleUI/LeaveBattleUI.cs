using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>确认主动离开当前对局；取消不改变战斗，确定后清理并返回主菜单。</summary>
    [Window(UILayer.Top, "LeaveBattleUI", fullScreen: false)]
    public sealed class LeaveBattleUI : UIWindow
    {
        private Button _confirm, _cancel, _close, _outside;
        private Text _message;
        private RectTransform _content;
        private MainCombatUI _owner;
        private BattleShopContext _context;
        private bool _busy, _leftBattle;
        private Rect _lastSafeArea;
        private int _lastWidth, _lastHeight;

        protected override void ScriptGenerator()
        {
            _content = FindChildComponent<RectTransform>("m_rect_Content");
            _message = FindChildComponent<Text>("m_rect_Content/m_text_Message");
            _confirm = FindChildComponent<Button>("m_rect_Content/m_btn_Confirm");
            _cancel = FindChildComponent<Button>("m_rect_Content/m_btn_Cancel");
            _close = FindChildComponent<Button>("m_rect_Content/m_btn_Close");
            _outside = FindChildComponent<Button>("m_btn_Outside");
        }

        protected override void OnCreate()
        {
            _confirm.onClick.AddListener(OnConfirmClicked);
            _cancel.onClick.AddListener(OnCancelClicked);
            _close.onClick.AddListener(OnCancelClicked);
            _outside.onClick.AddListener(OnCancelClicked);
        }

        protected override void OnRefresh()
        {
            if (_busy) return;
            _owner = UserData as MainCombatUI;
            _context = _owner?.Context;
            _leftBattle = false;
            var cupLoss = -BattleSettlementPresenter.Instance.LeaveCupDelta;
            _message.text = $"离开将扣除{cupLoss}杯，且无金币奖励。\n是否离开当前战斗？";
            RefreshButtons();
            FitToSafeArea();
            GameSoundService.Play(GameSound.UiOpen);
        }

        private bool OwnsCurrentBattle() => _owner != null && _owner.gameObject != null && _owner.Visible &&
            _context != null && !_context.IsEnded && !_context.IsEndRequested &&
            ReferenceEquals(_owner.Context, _context) && _owner.Host != null &&
            ReferenceEquals(_owner.Host.Context, _context);

        protected override void OnUpdate()
        {
            // 战斗自然结束或切换到另一局时，旧确认框不再接受确定。
            if (!_leftBattle && !_busy && !OwnsCurrentBattle()) { Close(); return; }
            if (!_busy && Input.GetKeyDown(KeyCode.Escape)) OnCancelClicked();
            if (_lastWidth != Screen.width || _lastHeight != Screen.height || _lastSafeArea != Screen.safeArea)
                FitToSafeArea();
        }

        private void OnCancelClicked()
        {
            if (_busy || _leftBattle) return;
            GameSoundService.Play(GameSound.UiClose);
            Close();
        }

        private void OnConfirmClicked() => ConfirmAsync().Forget();

        private async UniTask ConfirmAsync()
        {
            if (_busy || (!_leftBattle && !OwnsCurrentBattle())) return;
            _busy = true;
            RefreshButtons();
            GameSoundService.Play(GameSound.UiClick);
            try
            {
                if (!_leftBattle)
                {
                    var host = _owner.Host;
                    BattleSettlementPresenter.Instance.AbandonBattle(_context.BattleId);
                    _leftBattle = true;
                    _context.RequestEndBattle();
                    GameModule.UI.CloseUI<SettingsUI>();
                    host.EndBattle();
                }
                await GameApp.SavePlayerStateAsync();
                var menu = await GameModule.UI.ShowUIAsyncAwait<MainMenuUI>();
                if (menu == null || menu.IsDestroyed) throw new InvalidOperationException("主界面加载失败。");
                if (!IsDestroyed) Close();
            }
            catch (Exception exception)
            {
                Debug.LogError("离开战斗或返回主界面失败：" + exception.Message);
                if (!IsDestroyed)
                {
                    _message.text = "保存或返回失败，请点击确定重试";
                    GameSoundService.Play(GameSound.Error);
                }
            }
            finally
            {
                if (!IsDestroyed)
                {
                    _busy = false;
                    RefreshButtons();
                }
            }
        }

        private void RefreshButtons()
        {
            _confirm.interactable = !_busy;
            _cancel.interactable = _close.interactable = _outside.interactable = !_busy && !_leftBattle;
        }

        private void FitToSafeArea()
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            _lastSafeArea = Screen.safeArea;
            var root = Canvas.rootCanvas;
            var eventCamera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            Vector2 minimum, maximum;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, _lastSafeArea.min, eventCamera, out minimum) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, _lastSafeArea.max, eventCamera, out maximum)) return;
            _content.anchoredPosition = (minimum + maximum) * 0.5f;
            var space = maximum - minimum;
            var scale = Mathf.Min(1f, (space.x - 64f) / _content.sizeDelta.x, (space.y - 64f) / _content.sizeDelta.y);
            _content.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        protected override void OnDestroy()
        {
            _confirm.onClick.RemoveListener(OnConfirmClicked);
            _cancel.onClick.RemoveListener(OnCancelClicked);
            _close.onClick.RemoveListener(OnCancelClicked);
            _outside.onClick.RemoveListener(OnCancelClicked);
            _owner = null;
            _context = null;
            _userDatas = null;
        }
    }
}
