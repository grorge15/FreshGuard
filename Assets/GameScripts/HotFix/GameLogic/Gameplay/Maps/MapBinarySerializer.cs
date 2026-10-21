using System;
using System.IO;

namespace GameLogic
{
    /// <summary>固定小端FGMP V1，头部30字节；不依赖BinaryFormatter或场景序列化。</summary>
    public static class MapBinarySerializer
    {
        public const int HEADER_SIZE = 30;
        public static byte[] Serialize(MapData map)
        {
            MapValidator.ThrowIfInvalid(map);
            using (var stream = new MemoryStream(HEADER_SIZE + map.Terrains.Length))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'F', (byte)'G', (byte)'M', (byte)'P' });
                writer.Write((ushort)map.Version);
                writer.Write(map.Columns); writer.Write(map.Rows); writer.Write(map.CellSize);
                writer.Write(map.NormalGlassId); writer.Write(map.ColoredGlassId);
                writer.Write(map.Terrains.Length);
                foreach (var terrain in map.Terrains) writer.Write((byte)terrain);
                return stream.ToArray();
            }
        }

        public static MapData Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HEADER_SIZE) throw new InvalidDataException("地图文件头不完整。");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != 'F' || reader.ReadByte() != 'G' || reader.ReadByte() != 'M' || reader.ReadByte() != 'P')
                    throw new InvalidDataException("不是FGMP地图文件。");
                var map = new MapData { Version = reader.ReadUInt16(), Columns = reader.ReadInt32(), Rows = reader.ReadInt32(),
                    CellSize = reader.ReadSingle(), NormalGlassId = reader.ReadInt32(), ColoredGlassId = reader.ReadInt32() };
                var count = reader.ReadInt32();
                if (map.Version != MapData.CURRENT_VERSION) throw new InvalidDataException("不支持的地图版本：" + map.Version);
                if (map.Columns < 1 || map.Columns > MapData.MAX_DIMENSION || map.Rows < 1 || map.Rows > MapData.MAX_DIMENSION ||
                    count != (long)map.Columns * map.Rows || bytes.Length != HEADER_SIZE + count)
                    throw new InvalidDataException("地图尺寸、数量或文件长度不正确（可能截断或含尾随数据）。");
                map.Terrains = new TerrainType[count];
                for (var i = 0; i < count; i++) map.Terrains[i] = (TerrainType)reader.ReadByte();
                MapValidator.ThrowIfInvalid(map);
                return map;
            }
        }
    }
}
