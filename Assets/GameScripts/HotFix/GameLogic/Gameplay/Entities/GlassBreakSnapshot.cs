namespace GameLogic
{
    /// <summary>玻璃碎裂回收前复制的值，不持有池对象引用。</summary>
    public readonly struct GlassBreakSnapshot
    {
        public int InstanceId { get; }
        public int ConfigId { get; }
        public BattleSide Side { get; }
        public BoardCoordinate Coordinate { get; }
        public GlassKind GlassKind { get; }

        internal GlassBreakSnapshot(GlassEntity glass)
        {
            InstanceId = glass.InstanceId;
            ConfigId = glass.ConfigId;
            Side = glass.Side;
            Coordinate = glass.Coordinate;
            GlassKind = glass.GlassKind;
        }
    }
}
