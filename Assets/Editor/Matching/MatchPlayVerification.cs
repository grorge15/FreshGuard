using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.EditorTools
{
    /// <summary>通过真实Launcher及按钮验证；仅测试临时数据，结束后恢复原本地存档。</summary>
    [InitializeOnLoad]
    public static class MatchPlayVerification
    {
        private const string Pending="FreshGuard.MatchVerify.Pending";
        private const string Backup="FreshGuard.MatchVerify.Backup";
        private const string HadFile="FreshGuard.MatchVerify.HadFile";
        private const BindingFlags Private=BindingFlags.Static|BindingFlags.NonPublic;
        private static readonly List<string> Checks=new List<string>();
        private static string DirectoryPath => Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/MatchingVerification"));
        private static string SavePath => Path.Combine(Application.persistentDataPath,"match-player-v1.json");
        public static string Status {get;private set;}="idle";
        static MatchPlayVerification() {EditorApplication.playModeStateChanged+=OnPlayState;}
        [MenuItem("FreshGuard/Matching/Verify via Launcher")]
        public static void Launch()
        {
            if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("请停止Play并等编译完成。");
            for(var i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("场景尚有未保存修改。");
            Directory.CreateDirectory(DirectoryPath);
            SessionState.SetBool(HadFile,File.Exists(SavePath));
            SessionState.SetString(Backup,File.Exists(SavePath)?Convert.ToBase64String(File.ReadAllBytes(SavePath)):"");
            SessionState.SetBool(Pending,true);
            var helper=typeof(TEngine.UnityToolbarExtenderLeft).GetNestedType("SceneHelper",BindingFlags.NonPublic);
            helper.GetMethod("StartScene",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{"main"});
        }
        private static void OnPlayState(PlayModeStateChange state)
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))
            {
                SessionState.SetBool(Pending,false);
                RunAsync().Forget();
            }
        }
        private static void Require(bool value,string message)
        {
            if(!value)throw new InvalidOperationException(message);
            Checks.Add("PASS "+message);
        }
        private static async UniTask Wait(Func<bool> condition,int seconds=90)
            => await UniTask.WaitUntil(condition).Timeout(TimeSpan.FromSeconds(seconds));
        private static Button StartButton() => GameModule.UI.GetUI<MainMenuUI>().gameObject.GetComponentsInChildren<Button>(true)[0];
        private static void ClickStart()
        {
            var menu=GameModule.UI.GetUI<MainMenuUI>();
            foreach(var button in menu.gameObject.GetComponentsInChildren<Button>(true))
                if(button.name=="StartBattleButton"){button.onClick.Invoke();return;}
            throw new InvalidOperationException("未找到真实开始按钮");
        }
        private static async UniTask Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            ScreenCapture.CaptureScreenshot(Path.Combine(DirectoryPath,name+".png"));
            await UniTask.Yield();
        }
        private static async UniTaskVoid RunAsync()
        {
            Status="running";Checks.Clear();
            DemoBattleProfile original=null,fixture=null;
            Action<BattleSettlementSnapshot> settlement=s=>Checks.Add("UNEXPECTED_SETTLEMENT "+s.BattleId);
            try
            {
                Application.runInBackground=true;
                var gameView=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                gameView.Focus();
                await Wait(()=>GameModule.UI.GetUI<MainMenuUI>()?.IsPrepare==true);
                var initialMenu=GameModule.UI.GetUI<MainMenuUI>();
                initialMenu.ShowStatus("验证提示保留");initialMenu.ResetStartButton();
                await UniTask.Delay(150,ignoreTimeScale:true);
                Require(Array.Exists(initialMenu.gameObject.GetComponentsInChildren<Text>(true),t=>t.text=="验证提示保留"),"主界面刷新不会立即覆盖3秒状态提示");
                GameApp.PlayerState.AddEnergy(30);await GameApp.SavePlayerStateAsync();
                var initial=GameApp.PlayerState.Energy;
                GameEvent.AddEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested,settlement);
                ClickStart();ClickStart();
                await Wait(()=>GameApp.CurrentMatch!=null&&GameModule.UI.GetUI<BattleLoadingUI>()?.Session!=null);
                Require(GameApp.PlayerState.Energy==initial-5,"重复点击仅扣5点体力（"+initial+"→"+GameApp.PlayerState.Energy+"）");
                var loadingRect=GameModule.UI.GetUI<BattleLoadingUI>().rectTransform;
                Require(loadingRect.lossyScale.x>0&&loadingRect.rect.width>0&&loadingRect.rect.height>0,"匹配Canvas缩放与全屏Rect非零");
                var screenCorners=new Vector3[4];loadingRect.GetWorldCorners(screenCorners);
                var uiCamera=GameModule.UI.UICamera;
                var bottom=RectTransformUtility.WorldToScreenPoint(uiCamera,screenCorners[0]);
                var top=RectTransformUtility.WorldToScreenPoint(uiCamera,screenCorners[2]);
                Require(bottom.x<1&&bottom.y<1&&top.x>=Screen.width-1&&top.y>=Screen.height-1,"匹配背景边界覆盖整个屏幕");
                var timerText=GameModule.UI.GetUI<BattleLoadingUI>().transform.Find("m_rect_Matching/m_text_Timer").GetComponent<Text>();
                Require(timerText.rectTransform.rect.height>=timerText.preferredHeight,"匹配计时Text高度容纳中文字体行高");
                var emptyBuffs=GameModule.UI.GetUI<BattleLoadingUI>().transform.Find("m_rect_Success/m_item_Player/m_rect_BuffSlots");
                Require(emptyBuffs.childCount==1&&!emptyBuffs.GetChild(0).gameObject.activeSelf,"默认空BUFF保留容器且隐藏模板");
                await Capture("matching");
                var window=GameModule.UI.GetUI<BattleLoadingUI>();
                window.gameObject.transform.Find("m_rect_Matching/m_btn_Cancel").GetComponent<Button>().onClick.Invoke();
                await Wait(()=>GameApp.CurrentMatch==null);
                var host=UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                Require(GameApp.PlayerState.Energy==initial,"取消返还体力且受上限限制");
                Require(host==null||host.Context==null,"取消清理本局Context");
                Require(GameModule.UI.GetUI<MainCombatUI>()==null,"取消后无迟到MainCombatUI");
                Require(!Checks.Exists(c=>c.StartsWith("UNEXPECTED")),"取消不触发结算");
                original=(DemoBattleProfile)typeof(GameApp).GetField("_demoProfile",Private).GetValue(null);
                fixture=UnityEngine.Object.Instantiate(original);
                fixture.Buffs.Add(new DemoBuffDefinition{BuffId=900001,Name="测试助力A",IconLocation="U_cup"});
                fixture.Buffs.Add(new DemoBuffDefinition{BuffId=900002,Name="测试助力B",IconLocation="U_lock"});
                fixture.PlayerBuffs.Add(new DemoBuffCarry{BuffId=900001,Layers=3});
                fixture.PlayerBuffs.Add(new DemoBuffCarry{BuffId=900002,Layers=1});
                typeof(GameApp).GetField("_demoProfile",Private).SetValue(null,fixture);
                ClickStart();
                await Wait(()=>GameApp.CurrentMatch!=null&&GameModule.UI.GetUI<BattleLoadingUI>()?.Session!=null);
                var session=GameApp.CurrentMatch;
                var appState=GameModule.UI.GetUI<BattleLoadingUI>().gameObject.GetComponent<MatchApplicationState>();
                appState.SendMessage("OnApplicationPause",true);
                var paused=session.MatchingSeconds;
                await UniTask.Delay(600,ignoreTimeScale:true);
                Require(Math.Abs(session.MatchingSeconds-paused)<.02f,"后台暂停期间匹配时长不增加");
                appState.SendMessage("OnApplicationPause",false);
                await Wait(()=>UnityEngine.Object.FindObjectOfType<BattleShopSceneController>()?.IsReady==true);
                host=UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                if(host.IsWaitingForMatch)
                {
                    Require(Math.Abs(host.PreparationRemaining-3f)<.01f&&!host.IsCombatRunning,"Ready后匹配放行前开局倒计时保持3秒");
                    Require(host.Context.GetState(BattleSide.Opponent).EnergyCoins==host.Context.Rules.InitialCoins,"匹配期间AI尚未消费金币");
                }
                await Wait(()=>GameApp.CurrentMatch?.Phase==BattleMatchPhase.Success&&
                    GameModule.UI.GetUI<BattleLoadingUI>().transform.Find("m_rect_Success").gameObject.activeInHierarchy);
                var successStart=Time.realtimeSinceStartup;
                GameApp.CancelBattleMatch();
                Require(GameApp.CurrentMatch==session&&session.Phase==BattleMatchPhase.Success,"匹配成功后取消被拒绝");
                Require(session.MatchingSeconds>=2f,"匹配至少累计2秒前台时间");
                var successWindow=GameModule.UI.GetUI<BattleLoadingUI>();
                Require(!successWindow.transform.Find("m_rect_Matching").gameObject.activeInHierarchy,"成功时匹配中容器隐藏且成功容器可见");
                await Wait(()=>Array.TrueForAll(successWindow.gameObject.GetComponentsInChildren<Image>(false),i=>i.name!="m_img_Icon"||i.sprite!=null),1);
                var buffSlots=successWindow.transform.Find("m_rect_Success/m_item_Player/m_rect_BuffSlots");
                var visibleBuffs=0;
                for(var i=0;i<buffSlots.childCount;i++)if(buffSlots.GetChild(i).gameObject.activeSelf)visibleBuffs++;
                Require(host.Context.Entry.PlayerBuffs.Count==2&&visibleBuffs==2,"BUFF携带数据传入战斗且双方容器按种类生成");
                await Capture("success-buffs");
                await Wait(()=>GameApp.CurrentMatch==null);
                Require(Time.realtimeSinceStartup-successStart>=.9f,"匹配成功展示约1秒后才放行");
                Require(!host.IsCombatRunning&&host.PreparationRemaining>2.8f,"界面关闭后才开始原3秒开局倒计时");
                await Wait(()=>host.IsCombatOperational,40);
                await Capture("combat");
                Require(host.IsCombatRunning,"放行后双方发球、首敌、物理反弹进入运行态");
                Require(!Checks.Exists(c=>c.StartsWith("UNEXPECTED")),"验证期间未伪造胜负结算");
                host.EndBattle();
                Status="passed";
            }
            catch(Exception e){Status="failed";Checks.Add("FAIL "+e);Debug.LogError("[MatchPlayVerification] "+e);}
            finally
            {
                GameEvent.RemoveEventListener<BattleSettlementSnapshot>(BattleSettlementEvents.Requested,settlement);
                if(original!=null)typeof(GameApp).GetField("_demoProfile",Private).SetValue(null,original);
                if(fixture!=null)UnityEngine.Object.Destroy(fixture);
                File.WriteAllText(Path.Combine(DirectoryPath,"result.txt"),Status+Environment.NewLine+string.Join(Environment.NewLine,Checks));
                Debug.Log("[MatchPlayVerification] "+Status+" "+string.Join("；",Checks));
                EditorApplication.isPlaying=false;
                RestoreSaveAfterStop();
            }
        }
        private static async void RestoreSaveAfterStop()
        {
            await UniTask.WaitUntil(()=>!EditorApplication.isPlayingOrWillChangePlaymode);
            if(SessionState.GetBool(HadFile,false))File.WriteAllBytes(SavePath,Convert.FromBase64String(SessionState.GetString(Backup,"")));
            else if(File.Exists(SavePath))File.Delete(SavePath);
            var temporary=SavePath+".tmp";if(File.Exists(temporary))File.Delete(temporary);
        }
    }
}
