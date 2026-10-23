using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>主界面进入战斗期间的阶段进度遮罩。</summary>
    [Window(UILayer.System, "BattleLoadingUI", fullScreen: true)]
    public sealed class BattleLoadingUI : UIWindow
    {
        private Slider _progressBar;
        private Text _progressText;
        private Text _stageText;
        private BattleLoadingProgress _last;

        public BattleLoadingProgress LastProgress => _last;

        protected override void ScriptGenerator()
        {
            _progressBar = FindComponent<Slider>("ProgressBar", "Progress", "LoadingBar");
            _progressText = FindComponent<Text>("ProgressText", "Percent", "LoadingPercent");
            _stageText = FindComponent<Text>("StageText", "Message", "LoadingText");
        }

        public void Report(BattleLoadingProgress progress)
        {
            if (progress.Progress < _last.Progress && progress.Stage != BattleLoadingStage.Failed) return;
            _last = progress;
            if (_progressBar != null) _progressBar.value = progress.Progress;
            if (_progressText != null) _progressText.text = $"{Mathf.RoundToInt(progress.Progress * 100f)}%";
            if (_stageText != null) _stageText.text = progress.Message;
        }

        private T FindComponent<T>(params string[] names) where T : Component
        {
            foreach (var component in gameObject.GetComponentsInChildren<T>(true))
                foreach (var name in names)
                    if (string.Equals(component.name, name, StringComparison.OrdinalIgnoreCase)) return component;
            return null;
        }
    }
}
