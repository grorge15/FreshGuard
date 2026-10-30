namespace GameLogic
{
    /// <summary>全屏战斗窗口；子区域的 RectTransform 完全由预制体控制。</summary>
    [Window(UILayer.UI, "MainCombatUI", fullScreen: true)]
    public sealed class MainCombatUI : UIWindow
    {
        public BattleInfoUI Info { get; private set; }
        public BattleShopUI Shop { get; private set; }
        public BattleShopContext Context { get; private set; }
        public BattleShopSceneController Host { get; private set; }
        public BattleNoticeWidget Notices { get; private set; }

        protected override void RegisterEvent()
        {
            AddUIEvent<long, string>(BattleNoticeEvents.Requested, OnNoticeRequested);
        }

        protected override void OnCreate()
        {
            Info = CreateWidget<BattleInfoUI>("BattleInfoUI");
            Shop = CreateWidget<BattleShopUI>("BattleShopUI");
            Notices = CreateWidget<BattleNoticeWidget>("BattleNotices");
        }

        protected override void OnRefresh()
        {
            var context = UserData as BattleShopContext;
            if (!ReferenceEquals(Context, context)) Notices.Clear();
            Context = context;
            Host = UserDatas != null && UserDatas.Length > 1 ? UserDatas[1] as BattleShopSceneController : null;
            Shop.Bind(Context, Host);
            if (UserDatas != null && UserDatas.Length > 2 && UserDatas[2] is BattleProgressSnapshot progress)
                Info.Bind(progress);
        }

        protected override void OnSetVisible(bool visible)
        {
            if (!visible)
            {
                Shop?.Drag?.Cancel();
                Notices?.Clear();
            }
        }

        private void OnNoticeRequested(long battleId, string message)
        {
            if (Context == null || Context.BattleId != battleId || Context.IsEnded ||
                Context.IsEndRequested || !Visible) return;
            Notices.Show(message);
        }

        protected override void OnDestroy()
        {
            // 子 Widget 的监听、按钮和拖拽由框架统一销毁。
            Info = null;
            Shop = null;
            Context = null;
            Host = null;
            Notices = null;
            _userDatas = null;
        }
    }
}
