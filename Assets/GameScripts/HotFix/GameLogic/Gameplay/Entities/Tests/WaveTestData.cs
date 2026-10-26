using System;
using System.IO;
using GameConfig;
using GameConfig.wave;
using Luban;
using UnityEngine;

namespace GameLogic.Tests
{
    internal static class WaveTestData
    {
        internal static WaveSpawn Spawn(int id, int start, int end, int enemyId, int count)
        {
            var buf = new ByteBuf();
            WriteSpawn(buf, id, start, end, enemyId, count);
            return new WaveSpawn(buf);
        }

        internal static WavePlan Plan(int wave, params int[] groups)
        {
            var buf = new ByteBuf();
            WritePlan(buf, wave, groups);
            return new WavePlan(buf);
        }

        internal static Tables SingleWaveTables() => Tables(new[] { Plan(1, 1) }, new[] { Spawn(1, 0, 5, 2001, 10) });

        internal static Tables Tables(WavePlan[] plans, WaveSpawn[] spawns, int? wavesPerCycle = null)
        {
            var planBuf = new ByteBuf();
            planBuf.WriteSize(plans.Length);
            foreach (var plan in plans) WritePlan(planBuf, plan.Wave, plan.EnemySpawnId);
            var spawnBuf = new ByteBuf();
            spawnBuf.WriteSize(spawns.Length);
            foreach (var spawn in spawns)
                WriteSpawn(spawnBuf, spawn.Id, spawn.SpawnSecRange[0], spawn.SpawnSecRange[1], spawn.EnemyId, spawn.EnemyCount);
            var tables = new Tables(name => name == "wave_tbwaveplan" ? new ByteBuf(planBuf.CopyData()) :
                name == "wave_tbwavespawn" ? new ByteBuf(spawnBuf.CopyData()) :
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "AssetRaw/Configs/bytes", name + ".bytes"))));
            if (wavesPerCycle.HasValue)
            {
                var globalBuf = new ByteBuf();
                globalBuf.WriteSize(tables.TbGlobal.DataList.Count);
                foreach (var global in tables.TbGlobal.DataList)
                {
                    globalBuf.WriteInt(global.Id);
                    globalBuf.WriteFloat(global.Id == 48 ? wavesPerCycle.Value : global.Value);
                }
                tables.TbGlobal = new GameConfig.globalcfg.TbGlobal(globalBuf);
            }
            return tables;
        }

        private static void WritePlan(ByteBuf buf, int wave, int[] groups)
        {
            buf.WriteInt(1000 + wave); buf.WriteInt(wave); buf.WriteSize(groups.Length);
            foreach (var id in groups) buf.WriteInt(id);
        }

        private static void WriteSpawn(ByteBuf buf, int id, int start, int end, int enemyId, int count)
        {
            buf.WriteInt(id); buf.WriteSize(2); buf.WriteInt(start); buf.WriteInt(end);
            buf.WriteInt(enemyId); buf.WriteInt(count);
        }
    }
}
