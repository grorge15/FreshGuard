using System.IO;

namespace GameLogic
{
    /// <summary>FGMP固定小端30字节头；读V1/V2，写V2。旧图只在内存迁移。</summary>
    public static class MapBinarySerializer
    {
        public const int HEADER_SIZE = 30;
        public static byte[] Serialize(MapData map)
        {
            MapValidator.ThrowIfInvalid(map);
            using (var stream = new MemoryStream(HEADER_SIZE + 2 * map.Cells.Length))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'F', (byte)'G', (byte)'M', (byte)'P' });
                writer.Write((ushort)MapData.CURRENT_VERSION);
                writer.Write(map.Columns); writer.Write(map.Rows); writer.Write(map.CellSize);
                writer.Write(map.NormalGlassId); writer.Write(map.ColoredGlassId);
                writer.Write(map.Cells.Length);
                foreach (var cell in map.Cells)
                { writer.Write((byte)cell.BaseTerrain); writer.Write((byte)cell.Obstacle); }
                return stream.ToArray();
            }
        }

        public static MapData Deserialize(byte[] bytes) => Deserialize(bytes, out _);
        public static MapData Deserialize(byte[] bytes, out ushort sourceVersion)
        {
            sourceVersion = 0;
            if (bytes == null || bytes.Length < HEADER_SIZE) throw new InvalidDataException("地图文件头不完整。");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != 'F' || reader.ReadByte() != 'G' || reader.ReadByte() != 'M' || reader.ReadByte() != 'P')
                    throw new InvalidDataException("不是FGMP地图文件。");
                sourceVersion = reader.ReadUInt16();
                if (sourceVersion != 1 && sourceVersion != 2) throw new InvalidDataException("不支持的地图版本：" + sourceVersion);
                var map = new MapData { Columns = reader.ReadInt32(), Rows = reader.ReadInt32(),
                    CellSize = reader.ReadSingle(), NormalGlassId = reader.ReadInt32(), ColoredGlassId = reader.ReadInt32() };
                var count = reader.ReadInt32();
                var stride = sourceVersion == 1 ? 1 : 2;
                if (map.Columns < 1 || map.Columns > MapData.MAX_DIMENSION || map.Rows < 1 || map.Rows > MapData.MAX_DIMENSION ||
                    count != (long)map.Columns * map.Rows || bytes.Length != HEADER_SIZE + (long)count * stride)
                    throw new InvalidDataException("地图尺寸、数量或文件长度不正确（可能截断或含尾随数据）。");
                map.Cells = new MapCellData[count];
                for (var i = 0; i < count; i++)
                    map.Cells[i] = sourceVersion == 1 ? DecodeV1(reader.ReadByte()) :
                        new MapCellData((BaseTerrainType)reader.ReadByte(), (ObstacleType)reader.ReadByte());
                MapValidator.ThrowIfInvalid(map);
                return map;
            }
        }

        private static MapCellData DecodeV1(byte terrain)
        {
            switch (terrain)
            {
                case 1: return new MapCellData(BaseTerrainType.Track);
                case 2: return new MapCellData(BaseTerrainType.Empty);
                case 3: return new MapCellData(BaseTerrainType.Empty, ObstacleType.GlassNormal);
                case 4: return new MapCellData(BaseTerrainType.Empty, ObstacleType.GlassColored);
                default: throw new InvalidDataException("V1地图含未知地形编号：" + terrain);
            }
        }
    }
}
