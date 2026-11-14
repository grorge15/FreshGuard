using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>全屏战斗窗口；Prefab 提供基准布局，屏幕适配只调整区域根节点。</summary>
    [Window(UILayer.UI, "MainCombatUI", fullScreen: true)]
    public sealed partial class MainCombatUI : UIWindow
    {
        private Text _preparationCountdown;
        private int _preparationDigit;
        private Vector3 _preparationBaseScale;
        private Color _preparationBaseColor;
        // x 为每个数字内的进度，y 为缩放，z 为透明度。
        private static readonly Vector3[] _preparationAnimationKeys =
        {
            new Vector3(0f, 0.45f, 0f),
            new Vector3(0.09f, 1.24f, 1f),
            new Vector3(0.17f, 0.93f, 1f),
            new Vector3(0.25f, 1f, 1f),
            new Vector3(0.73f, 1f, 1f),
            new Vector3(0.86f, 1.08f, 0.85f),
            new Vector3(1f, 0.82f, 0f)
        };

        public BattleInfoUI Info { get; private set; }
        public BattleShopUI Shop { get; private set; }
        public BattleShopContext Context { get; private set; }
        public BattleShopSceneController Host { get; private set; }
        public BattleNoticeWidget Notices { get; private set; }
        public GlassCurrencyFlightWidget CurrencyFlight { get; private set; }

        protected override void ScriptGenerator()
        {
            _preparationCountdown = FindChildComponent<Text>("m_text_PreparationCountdown");
            if (_preparationCountdown == null) return;
            _preparationBaseScale = _preparationCountdown.rectTransform.localScale;
            _preparationBaseColor = _preparationCountdown.color;
        }

        protected override void RegisterEvent()
        {
            AddUIEvent<long, string>(BattleNoticeEvents.Requested, OnNoticeRequested);
            AddUIEvent<BattleCurrencyFlightEvent>(BattleRewardAdapter.CURRENCY_FLIGHT_EVENT, OnCurrencyGranted);
        }

        protected override void OnCreate()
        {
            Info = CreateWidget<BattleInfoUI>("BattleInfoUI");
            Shop = CreateWidget<BattleShopUI>("BattleShopUI");
            CurrencyFlight = CreateWidget<GlassCurrencyFlightWidget>("GlassCurrencyFlight");
            Notices = CreateWidget<BattleNoticeWidget>("BattleNotices");
        }

        protected override void OnRefresh()
        {
            var context = UserData as BattleShopContext;
            if (!ReferenceEquals(Context, context)) Notices.Clear();
            Context = context;
            Info.BindMatch(context?.Entry);
            Host = UserDatas != null && UserDatas.Length > 1 ? UserDatas[1] as BattleShopSceneController : null;
            Shop.Bind(Context, Host);
            CurrencyFlight.Bind(Context, Host != null && Host.Placement != null ? Host.Placement.SceneCamera : null,
                Shop.transform.Find("m_rect_ShopPanel/EnergyBalance/m_img_EnergyCoin") as UnityEngine.RectTransform);
            if (UserDatas != null && UserDatas.Length > 2 && UserDatas[2] is BattleProgressSnapshot progress)
                Info.Bind(progress);
            RefreshPreparationCountdown();
        }

        protected override void OnUpdate() { RefreshPreparationCountdown(); }

        private void RefreshPreparationCountdown()
        {
            if (_preparationCountdown == null) return;
            // 共用战斗准备计时；匹配加载期间不显示，暂停时随原计时停留。
            var remaining = Host != null && ReferenceEquals(Host.Context, Context) && Host.IsReady &&
                !Host.IsWaitingForMatch ? Host.PreparationRemaining : 0f;
            var digit = Mathf.CeilToInt(remaining);
            if (_preparationCountdown.gameObject.activeSelf != (digit > 0))
            {
                _preparationCountdown.gameObject.SetActive(digit > 0);
                if (digit <= 0) ResetPreparationAppearance();
            }
            if (_preparationDigit != digit)
            {
                if (digit > 0) GameSoundService.Play(GameSound.Countdown, pitch: Mathf.Clamp(1.15f - digit * 0.035f, 0.85f, 1.15f));
                _preparationDigit = digit;
                _preparationCountdown.text = digit > 0 ? digit.ToString() : string.Empty;
            }
            if (digit > 0) AnimatePreparationCountdown(Mathf.Clamp01(digit - remaining));
        }

        private void AnimatePreparationCountdown(float progress)
        {
            // 直接由准备剩余时间求动画进度，暂停、掉帧与数字切换都不产生额外计时。
            for (var i = 1; i < _preparationAnimationKeys.Length; i++)
            {
                var previous = _preparationAnimationKeys[i - 1];
                var next = _preparationAnimationKeys[i];
                if (progress > next.x) continue;
                var blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(previous.x, next.x, progress));
                _preparationCountdown.rectTransform.localScale = _preparationBaseScale * Mathf.Lerp(previous.y, next.y, blend);
                var color = _preparationBaseColor;
                color.a *= Mathf.Lerp(previous.z, next.z, blend);
                _preparationCountdown.color = color;
                return;
            }
        }

        private void ResetPreparationAppearance()
        {
            _preparationCountdown.rectTransform.localScale = _preparationBaseScale;
            _preparationCountdown.color = _preparationBaseColor;
        }

        protected override void OnSetVisible(bool visible)
        {
            _screenLayout?.SetInformationVisible(visible);
            if (!visible)
            {
                Shop?.CancelSlotRefresh();
                Shop?.Drag?.Cancel();
                Notices?.Clear();
                CurrencyFlight?.Clear();
                if (_preparationCountdown != null)
                {
                    _preparationCountdown.gameObject.SetActive(false);
                    ResetPreparationAppearance();
                }
            }
            else RefreshPreparationCountdown();
        }

        private void OnNoticeRequested(long battleId, string message)
        {
            if (Context == null || Context.BattleId != battleId || Context.IsEnded ||
                Context.IsEndRequested || !Visible) return;
            Notices.Show(message);
        }

        private void OnCurrencyGranted(BattleCurrencyFlightEvent reward)
        {
            if (Context == null || Context.BattleId != reward.BattleId || Context.IsEnded ||
                Context.IsEndRequested || !Visible || reward.Recipient != BattleSide.Player) return;
            CurrencyFlight.Show(reward.WorldOrigin, reward.IconCount);
        }

        protected override void OnDestroy()
        {
            _screenLayout?.Unbind();
            // 子 Widget 的监听、按钮和拖拽由框架统一销毁。
            Info = null;
            Shop = null;
            Context = null;
            Host = null;
            Notices = null;
            CurrencyFlight = null;
            _preparationCountdown = null;
            _userDatas = null;
        }
    }
}
