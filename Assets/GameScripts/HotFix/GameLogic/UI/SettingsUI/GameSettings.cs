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

        private const string BattleMusic = "Electrodoodle";
        private const string InterfaceMusic = "Wallpaper";
        private static AudioAgent _music;
        private static BattleShopContext _musicBattle;
        private static bool _musicInitialized;

        private static bool _countingDown;

        internal static void EnterBattleInterface(BattleShopContext battle)
        {
            if (!_musicInitialized || battle == null || !Application.isPlaying || ReferenceEquals(_musicBattle, battle)) return;
            _music?.Stop();
            _music = null;
            _musicBattle = battle;
            _countingDown = true;
        }

        internal static void BeginBattleMusic(BattleShopContext battle)
        {
            if (!_countingDown || !ReferenceEquals(_musicBattle, battle)) return;
            _countingDown = false;
            ApplyMusic(MusicEnabled);
        }

        internal static void EndBattleMusic(BattleShopContext battle)
        {
            if (battle != null && ReferenceEquals(_musicBattle, battle))
            {
                _musicBattle = null;
                _countingDown = false;
                _music?.Stop();
                _music = null;
                if (_musicInitialized) ApplyMusic(MusicEnabled);
            }
        }

        private static void ApplyMusic(bool enabled)
        {
            GameModule.Audio.MusicEnable = enabled;
            if (!enabled) _music = null;
            else if (_musicInitialized && !_countingDown && _music == null)
            {
                bool inBattle = _musicBattle != null;
                _music = GameModule.Audio.Play(TEngine.AudioType.Music, inBattle ? BattleMusic : InterfaceMusic,
                    bLoop: true, volume: inBattle ? .11f : .10f, bAsync: false);
            }
        }

        internal static void ReleaseMusic()
        {
            _musicInitialized = false;
            _countingDown = false;
            _musicBattle = null;
            _music?.Stop();
            _music = null;
        }

        public static void SetMusicEnabled(bool enabled)
        {
            ApplyMusic(enabled);
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
            _musicInitialized = true;
            ApplyMusic(MusicEnabled);
            GameModule.Audio.SoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.SoundMuted, false);
            GameModule.Audio.UISoundEnable = !Utility.PlayerPrefs.GetBool(Constant.Setting.UISoundMuted, false);
        }
    }
}
