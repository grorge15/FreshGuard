using System;
using GameConfig;

namespace GameLogic
{
    /// <summary>开局读取并校验的能源球规则快照，物理帧不加载配置资源。</summary>
    public sealed class BallGlobalRules
    {
        public float NormalSpeed { get; }
        public float RageSpeed { get; }
        public int ComboTriggerCount { get; }
        public float RageStayInterval { get; }
        public float LowSpeedThreshold { get; }
        public float LowSpeedDuration { get; }
        public float NoInteractionDuration { get; }
        public int LaunchAngleMin { get; }
        public int LaunchAngleMax { get; }
        public int ExtraBallCount { get; }
        public float ReentryBlockSeconds { get; }

        public static BallGlobalRules FromTables(Tables tables) => new BallGlobalRules(tables);

        private BallGlobalRules(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            NormalSpeed = Read(tables, 2);
            RageSpeed = Read(tables, 3);
            ComboTriggerCount = ReadCount(tables, 10);
            RageStayInterval = Read(tables, 16);
            LowSpeedThreshold = Read(tables, 18, true);
            LowSpeedDuration = Read(tables, 19);
            NoInteractionDuration = Read(tables, 20);
            var range = tables.TbGlobal.GetOrDefault(21)?.IntArrVal;
            if (range == null || range.Length != 2 || range[0] < 0 || range[1] > 360 || range[0] > range[1])
                throw new ArgumentException("Global21 发射角范围必须为0到360内的两个递增角度（可相等）。");
            LaunchAngleMin = range[0]; LaunchAngleMax = range[1];
            ExtraBallCount = ReadCount(tables, 35, true);
            ReentryBlockSeconds = Read(tables, 49, true);
        }

        private static float Read(Tables tables, int id, bool allowZero = false)
        {
            var row = tables.TbGlobal.GetOrDefault(id);
            if (row == null || float.IsNaN(row.Value) || float.IsInfinity(row.Value) ||
                row.Value < 0f || !allowZero && row.Value == 0f)
                throw new ArgumentException("Global" + id + " 必须为有限" + (allowZero ? "非负数。" : "正数。"));
            return row.Value;
        }

        private static int ReadCount(Tables tables, int id, bool allowZero = false)
        {
            var value = Read(tables, id, allowZero);
            if ((double)value > int.MaxValue || value != Math.Truncate(value))
                throw new ArgumentException("Global" + id + " 必须为int范围内的整数。");
            return (int)value;
        }
    }
}
