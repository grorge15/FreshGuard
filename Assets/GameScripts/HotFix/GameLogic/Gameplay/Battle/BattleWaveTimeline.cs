using System;
using System.Collections.Generic;
using System.Linq;
using GameConfig;
using GameConfig.enemy;
using GameConfig.wave;

namespace GameLogic
{
    /// <summary>只推进配置生成序号；累计时钟和双方实体由 EnemyBattleController 持有。</summary>
    public sealed class BattleWaveTimeline
    {
        public sealed class Group
        {
            public WaveSpawn Rule { get; }
            public Enemy Config { get; }
            public int Wave { get; }
            public int Cycle { get; }
            public int NextSpawnIndex { get; internal set; }
            public double NextTime => NextSpawnIndex < Rule.EnemyCount
                ? Rule.SpawnSecRange[0] + NextSpawnIndex * ((double)Rule.SpawnSecRange[1] - Rule.SpawnSecRange[0]) / Rule.EnemyCount
                : double.PositiveInfinity;

            internal Group(WaveSpawn rule, Enemy config, int wave, int cycle)
            { Rule = rule; Config = config; Wave = wave; Cycle = cycle; }
        }

        public readonly struct SpawnCommand
        {
            public Group Group { get; }
            public int SpawnIndex { get; }
            public double SpawnTime { get; }
            internal SpawnCommand(Group group)
            { Group = group; SpawnIndex = group.NextSpawnIndex; SpawnTime = group.NextTime; }
        }

        private readonly Group[] _groups;
        private readonly double[] _waveStarts;
        private double _lastCollected;
        public IReadOnlyList<Group> Groups { get; }
        public int CurrentWave { get; private set; } = 1;
        public bool IsAllSpawnsCompleted => _groups.All(g => g.NextSpawnIndex == g.Rule.EnemyCount);
        public double NextEventTime => _groups.Min(g => g.NextTime);

        private BattleWaveTimeline(Group[] groups, double[] waveStarts)
        { _groups = groups; Groups = Array.AsReadOnly(groups); _waveStarts = waveStarts; }

        public static BattleWaveTimeline FromTables(Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var cycleValue = tables.TbGlobal.GetOrDefault(48)?.Value ?? 0f;
            if (float.IsNaN(cycleValue) || float.IsInfinity(cycleValue) || cycleValue <= 0 ||
                (double)cycleValue > int.MaxValue || cycleValue != Math.Truncate(cycleValue))
                throw new ArgumentException("Global 48 必须为每 Cycle 的正整数波数。");
            var wavesPerCycle = (int)cycleValue;
            var plans = tables.TbWavePlan.DataList.OrderBy(p => p.Wave).ToArray();
            if (plans.Length == 0) throw new ArgumentException("WavePlan 没有波次方案。");
            var groups = new List<Group>();
            var used = new HashSet<int>();
            var starts = new double[plans.Length];
            for (var i = 0; i < plans.Length; i++)
            {
                var plan = plans[i];
                if (plan.Id <= 0 || plan.Wave != i + 1 || plan.EnemySpawnId == null || plan.EnemySpawnId.Length == 0)
                    throw new ArgumentException("WavePlan 波次须从1连续且至少引用一个生成组：" + plan.Id);
                var cycle = (plan.Wave - 1) / wavesPerCycle + 1;
                if (cycle > 2) throw new ArgumentException("敌人当前仅支持两个 Cycle，WavePlan：" + plan.Id);
                var start = double.PositiveInfinity;
                foreach (var id in plan.EnemySpawnId.OrderBy(id => id))
                {
                    var rule = tables.TbWaveSpawn.GetOrDefault(id);
                    if (id <= 0 || rule == null || !used.Add(id))
                        throw new ArgumentException("WavePlan " + plan.Id + " 的生成组缺失或重复引用：" + id);
                    var range = rule.SpawnSecRange;
                    if (range == null || range.Length != 2 || range[0] < 0 || range[1] <= range[0] || rule.EnemyCount <= 0)
                        throw new ArgumentException("WaveSpawn 窗口须满足0≤A<B且数量为正：" + id);
                    var enemy = tables.TbEnemy.GetOrDefault(rule.EnemyId);
                    try { BattleEntityRegistry.ValidateEnemyConfig(enemy); }
                    catch (ArgumentException error)
                    {
                        throw new ArgumentException("WaveSpawn " + id + " 的敌人配置无效：" + rule.EnemyId, error);
                    }
                    start = Math.Min(start, range[0]);
                    groups.Add(new Group(rule, enemy, plan.Wave, cycle));
                }
                if (i > 0 && start <= starts[i - 1])
                    throw new ArgumentException("WavePlan 的开始时点必须随波次递增：" + plan.Id);
                starts[i] = start;
            }
            return new BattleWaveTimeline(groups.ToArray(), starts);
        }

        /// <summary>输出按时间和配置顺序排列的到期指令；相同时间重复调用不会重复出兵。</summary>
        public void CollectDue(double elapsedSeconds, List<SpawnCommand> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < _lastCollected)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            destination.Clear();
            _lastCollected = elapsedSeconds;
            while (CurrentWave < _waveStarts.Length && _waveStarts[CurrentWave] <= elapsedSeconds + 1e-7d)
                CurrentWave++;
            while (true)
            {
                Group next = null;
                foreach (var group in _groups)
                    if (group.NextTime <= elapsedSeconds + 1e-7d && (next == null || group.NextTime < next.NextTime)) next = group;
                if (next == null) break;
                destination.Add(new SpawnCommand(next));
                next.NextSpawnIndex++;
            }
        }
    }
}
