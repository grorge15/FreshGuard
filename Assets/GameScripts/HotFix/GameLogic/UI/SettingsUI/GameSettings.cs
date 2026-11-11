using TEngine;
using UnityEngine;

namespace GameLogic
{
    /// <summary>设置沿用框架音频存储键与现有震动开关。</summary>
    public static class GameSettings
    {
        public static bool MusicEnabled => !Utility.PlayerPrefs.GetBool(Constant.Setting.MusicMuted, false);
        public static bool SoundEnabled => !Utility.PlayerPrefs.GetBool(Constant.Setting.SoundMuted, false) &&
            !Utility.PlayerPrefs.GetBool(Constant.Setting.UISoundMuted, false);

        public static void SetMusicEnabled(bool enabled)
        {
            GameModule.Audio.MusicEnable = enabled;
            Utility.PlayerPrefs.SetBool(Constant.Setting.MusicMuted, !enabled);
        }

        public static void SetSoundEnabled(bool enabled)
        {
            // 一个开关同时管理棋盘音效与按钮、结算等界面音效。
            GameModule.Audio.SoundEnable = enabled;
            GameModule.Audio.UISoundEnable = enabled;
            Utility.PlayerPrefs.SetInt(Constant.Setting.SoundMuted, enabled ? 0 : 1, false);
            Utility.PlayerPrefs.SetInt(Constant.Setting.UISoundMuted, enabled ? 0 : 1, false);
            PlayerPrefs.Save();
        }

        public static void ApplySavedAudio()
        {
            // 启动流程设置音量可能覆盖音乐静音，在音频初始化后恢复保存的开关。
            GameModule.Audio.MusicEnable = MusicEnabled;
            GameModule.Audio.SoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.SoundMuted, false);
            GameModule.Audio.UISoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.UISoundMuted, false);
        }
    }
}
