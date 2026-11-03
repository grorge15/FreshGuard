using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.System,"BattleLoadingUI",fullScreen:true)]
    public sealed class BattleLoadingUI : UIWindow
    {
        private const float DOT_INTERVAL = 0.6f;
        // 隐藏的点仍保留字宽，动画切换时整行保持居中且不左右跳动。
        private static readonly string[] ENTERING_FRAMES = {
            "即将进入战斗<color=#FFFFFF00>...</color>",
            "即将进入战斗.<color=#FFFFFF00>..</color>",
            "即将进入战斗..<color=#FFFFFF00>.</color>",
            "即将进入战斗..."
        };
        private GameObject _matching;
        private GameObject _success;
        private Text _timer;
        private Button _cancel;
        private Text _entering;
        private MatchInfoWidget _player;
        private MatchInfoWidget _opponent;
        private BattleMatchSession _session;
        private Action _onCancel;
        private float _dotTime;
        private int _dotFrame;
        private BattleLoadingProgress _last;
        public BattleLoadingProgress LastProgress => _last;
        public BattleMatchSession Session => _session;
        protected override void ScriptGenerator()
        {
            _matching=transform.Find("m_rect_Matching").gameObject;
            _success=transform.Find("m_rect_Success").gameObject;
            _timer=FindChildComponent<Text>("m_rect_Matching/m_text_Timer");
            _cancel=FindChildComponent<Button>("m_rect_Matching/m_btn_Cancel");
            _entering=FindChildComponent<Text>("m_rect_Success/m_rect_Banner/m_text_Entering");
        }
        protected override void OnCreate()
        {
            _player=CreateWidget<MatchInfoWidget>("m_rect_Success/m_item_Player");
            _opponent=CreateWidget<MatchInfoWidget>("m_rect_Success/m_item_Opponent");
            _cancel.onClick.AddListener(OnCancel);
            if(gameObject.GetComponent<MatchApplicationState>()==null) gameObject.AddComponent<MatchApplicationState>();
        }
        public void Bind(MatchEntrySnapshot entry,BattleMatchSession session,Action onCancel)
        {
            _session=session; _onCancel=onCancel; _last=default; _dotTime=0; _dotFrame=0;
            _entering.text=ENTERING_FRAMES[0];
            _player.Render(entry.PlayerName,entry.PlayerAvatar,entry.Trophies,entry.PlayerBuffs);
            _opponent.Render(entry.OpponentName,entry.OpponentAvatar,entry.Trophies,entry.OpponentBuffs);
            Render();
        }
        public void Report(BattleLoadingProgress progress)
        {
            if (progress.Progress>=_last.Progress || progress.Stage==BattleLoadingStage.Failed) _last=progress;
        }
        public void Render()
        {
            if (_session==null || IsDestroyed) return;
            var matching=_session.Phase==BattleMatchPhase.Matching;
            _matching.SetActive(matching); _success.SetActive(!matching);
            _cancel.interactable=matching;
            var seconds=(int)_session.MatchingSeconds;
            _timer.text=$"{seconds/60:00}:{seconds%60:00}";
        }
        protected override void OnUpdate()
        {
            if (_session==null || _session.Phase!=BattleMatchPhase.Success || !MatchApplicationState.IsForeground) return;
            _dotTime+=Time.unscaledDeltaTime;
            var shown=(int)(_dotTime/DOT_INTERVAL)%ENTERING_FRAMES.Length;
            if(shown==_dotFrame)return;
            _dotFrame=shown;
            _entering.text=ENTERING_FRAMES[shown];
        }
        private void OnCancel() { if (_session?.Phase==BattleMatchPhase.Matching) _onCancel?.Invoke(); }
        protected override void OnDestroy()
        {
            _onCancel=null; _session=null;
            if(_cancel!=null) _cancel.onClick.RemoveListener(OnCancel);
        }
    }
}
