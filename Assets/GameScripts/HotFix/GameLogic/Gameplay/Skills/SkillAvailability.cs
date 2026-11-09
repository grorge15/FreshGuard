using System.Collections.Generic;
using GameConfig;

namespace GameLogic
{
    public static class SkillAvailability
    {
        public static bool IsRobotEnabled(Tables tables, int robotId)
        {
            var robot = tables.TbRobot.GetOrDefault(robotId);
            return robot != null && robot.SkillId > 0 && tables.TbSkill.GetOrDefault(robot.SkillId)?.Enabled == true;
        }
        public static IReadOnlyList<int> Filter(Tables tables, IReadOnlyList<int> candidates)
        {
            var result = new List<int>();
            foreach (var id in candidates) if (IsRobotEnabled(tables, id)) result.Add(id);
            return result;
        }
    }
}
