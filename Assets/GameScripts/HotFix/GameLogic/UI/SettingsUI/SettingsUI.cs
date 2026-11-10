using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.Top, "SettingsUI", fullScreen: false)]
    public sealed class SettingsUI : UIWindow
    {
        private Button _music, _sound, _haptic, _close, _outside;
        private RectTransform _content;
        private UIWindow _owner;
        private Rect _lastSafeArea;
        private int _lastWidth, _lastHeight;

        protected override void ScriptGenerator()
        {
            _content = FindChildComponent<RectTransform>("m_rect_Content");
            _music = FindChildComponent<Button>("m_rect_Content/m_rect_Music/m_btn_Switch");
            _sound = FindChildComponent<Button>("m_rect_Content/m_rect_Sound/m_btn_Switch");
            _haptic = FindChildComponent<Button>("m_rect_Content/m_rect_Haptic/m_btn_Switch");
            _close = FindChildComponent<Button>("m_rect_Content/m_btn_Close");
            _outside = FindChildComponent<Button>("m_btn_Outside");
        }

        protected override void OnCreate()
        {
            _music.onClick.AddListener(OnMusicClicked);
            _sound.onClick.AddListener(OnSoundClicked);
            _haptic.onClick.AddListener(OnHapticClicked);
            _close.onClick.AddListener(OnCloseClicked);
            _outside.onClick.AddListener(OnCloseClicked);
        }

        protected override void OnRefresh()
        {
            _owner = UserData as UIWindow;
            RefreshSwitches();
            FitToSafeArea();
            GameSoundService.Play(GameSound.UiOpen);
        }

        protected override void OnUpdate()
        {
            if (_owner != null && (_owner.IsDestroyed || _owner.gameObject == null || !_owner.Visible ||
                (_owner is MainCombatUI combat && combat.Context != null &&
                    (combat.Context.IsEnded || combat.Context.IsEndRequested))))
            {
                Close();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { OnCloseClicked(); return; }
            if (_lastWidth != Screen.width || _lastHeight != Screen.height || _lastSafeArea != Screen.safeArea)
                FitToSafeArea();
        }

        private void OnMusicClicked()
        {
            GameSettings.SetMusicEnabled(!GameSettings.MusicEnabled);
            RefreshSwitches();
            GameSoundService.Play(GameSound.UiClick);
        }

        private void OnSoundClicked()
        {
            GameSettings.SetSoundEnabled(!GameSettings.SoundEnabled);
            RefreshSwitches();
            // 开启后立即给出反馈，关闭时保持静音。
            GameSoundService.Play(GameSound.UiClick);
        }

        private void OnHapticClicked()
        {
            HapticFeedbackService.Enabled = !HapticFeedbackService.Enabled;
            RefreshSwitches();
            GameSoundService.Play(GameSound.UiClick);
            if (HapticFeedbackService.Enabled) HapticFeedbackService.PlayMergePulse();
        }

        private void OnCloseClicked()
        {
            GameSoundService.Play(GameSound.UiClose);
            Close();
        }

        private void RefreshSwitches()
        {
            SetSwitch(_music, GameSettings.MusicEnabled);
            SetSwitch(_sound, GameSettings.SoundEnabled);
            SetSwitch(_haptic, HapticFeedbackService.Enabled);
        }

        private static void SetSwitch(Button button, bool enabled)
        {
            button.transform.Find("m_img_On").gameObject.SetActive(enabled);
            button.transform.Find("m_img_Off").gameObject.SetActive(!enabled);
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
            _music.onClick.RemoveListener(OnMusicClicked);
            _sound.onClick.RemoveListener(OnSoundClicked);
            _haptic.onClick.RemoveListener(OnHapticClicked);
            _close.onClick.RemoveListener(OnCloseClicked);
            _outside.onClick.RemoveListener(OnCloseClicked);
            _owner = null;
            _userDatas = null;
        }
    }
}
