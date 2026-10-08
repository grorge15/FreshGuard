using System;
using GameConfig;
using UnityEngine;

namespace GameLogic
{
    /// <summary>预制体的配置关联；实体仍由单局管理器统一管理。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotView : MonoBehaviour
    {
        [SerializeField] private int _configId;

        public int ConfigId => _configId;

        /// <summary>业务方加载预制体后显式创建实体，不在 Awake 中自动注册。</summary>
        public RobotEntity CreateEntity(BattleEntityRegistry registry, BattleSide side, int level = 1, int maxLevel = 5)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return registry.CreateRobotFromConfig(ConfigSystem.Instance.Tables.TbRobot.Get(_configId), side, level, maxLevel);
        }
    }
}
