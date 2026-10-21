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

        protected override void OnCreate()
        {
            Info = CreateWidget<BattleInfoUI>("BattleInfoUI");
            Shop = CreateWidget<BattleShopUI>("BattleShopUI");
        }

        protected override void OnRefresh()
        {
            Context = UserData as BattleShopContext;
            Host = UserDatas != null && UserDatas.Length > 1 ? UserDatas[1] as BattleShopSceneController : null;
            Shop.Bind(Context, Host);
            if (UserDatas != null && UserDatas.Length > 2 && UserDatas[2] is BattleProgressSnapshot progress)
                Info.Bind(progress);
        }

        protected override void OnSetVisible(bool visible)
        {
            if (!visible) Shop?.Drag?.Cancel();
        }

        protected override void OnDestroy()
        {
            // 子 Widget 的监听、按钮和拖拽由框架统一销毁。
            Info = null;
            Shop = null;
            Context = null;
            Host = null;
            _userDatas = null;
        }
    }
}
