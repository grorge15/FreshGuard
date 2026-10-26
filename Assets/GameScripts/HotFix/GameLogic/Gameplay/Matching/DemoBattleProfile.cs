using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    [Serializable]
    public sealed class DemoRobotReference
    {
        public int RobotId;
        public bool Unlocked = true;
        [Range(1, 5)] public int ReferenceLevel = 5;
    }
    [Serializable]
    public sealed class DemoBuffDefinition
    {
        public int BuffId;
        public string Name;
        public string IconLocation;
        public bool Unlocked = true;
    }
    [Serializable]
    public sealed class DemoBuffCarry
    {
        public int BuffId;
        [Min(1)] public int Layers = 1;
    }
    [CreateAssetMenu(menuName = "FreshGuard/Demo Battle Profile")]
    public sealed class DemoBattleProfile : ScriptableObject
    {
        public string PlayerName = MainMenuPlayerState.DefaultPlayerName;
        public string PlayerAvatar = "Main_playerAvatar 1";
        public int InitialTrophies = 2500;
        public string OpponentName = "美味料理家";
        public string OpponentAvatar = "Main_playerAvatar 1";
        public List<DemoRobotReference> Robots = new List<DemoRobotReference>();
        public List<DemoBuffDefinition> Buffs = new List<DemoBuffDefinition>();
        public List<DemoBuffCarry> PlayerBuffs = new List<DemoBuffCarry>();
    }
}
