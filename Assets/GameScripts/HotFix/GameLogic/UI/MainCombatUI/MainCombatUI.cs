namespace GameLogic
{
    /// <summary>全屏战斗窗口；Prefab 提供基准布局，屏幕适配只调整区域根节点。</summary>
    [Window(UILayer.UI, "MainCombatUI", fullScreen: true)]
    public sealed partial class MainCombatUI : UIWindow
    {
        public BattleInfoUI Info { get; private set; }
        public BattleShopUI Shop { get; private set; }
        public BattleShopContext Context { get; private set; }
        public BattleShopSceneController Host { get; private set; }
        public BattleNoticeWidget Notices { get; private set; }
        public GlassCurrencyFlightWidget CurrencyFlight { get; private set; }

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
            Host = UserDatas != null && UserDatas.Length > 1 ? UserDatas[1] as BattleShopSceneController : null;
            Shop.Bind(Context, Host);
            CurrencyFlight.Bind(Context, Host != null && Host.Placement != null ? Host.Placement.SceneCamera : null,
                Shop.transform.Find("m_rect_ShopPanel/EnergyBalance/m_img_EnergyCoin") as UnityEngine.RectTransform);
            if (UserDatas != null && UserDatas.Length > 2 && UserDatas[2] is BattleProgressSnapshot progress)
                Info.Bind(progress);
        }

        protected override void OnSetVisible(bool visible)
        {
            if (!visible)
            {
                Shop?.Drag?.Cancel();
                Notices?.Clear();
                CurrencyFlight?.Clear();
            }
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
            _userDatas = null;
        }
    }
}
