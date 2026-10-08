namespace GameLogic
{
    /// <summary>回收前复制的结果，不包含实体引用，可在怪物归还池后安全读取。</summary>
    public readonly struct EntityRemovalSnapshot
    {
        public int InstanceId { get; }
        public int ConfigId { get; }
        public BattleSide Side { get; }
        public EntityKind Kind { get; }
        public EntityRemovalReason RemovalReason { get; }
        public int? RobotLevel { get; }
        public int? EnemyHp { get; }
        public float? EnemyRouteProgress { get; }

        internal EntityRemovalSnapshot(Entity entity, EntityRemovalReason reason)
        {
            InstanceId = entity.InstanceId;
            ConfigId = entity.ConfigId;
            Side = entity.Side;
            Kind = entity.Kind;
            RemovalReason = reason;
            RobotLevel = (entity as RobotEntity)?.Level;
            EnemyHp = (entity as EnemyEntity)?.CurrentHp;
            EnemyRouteProgress = (entity as EnemyEntity)?.RouteProgress;
        }
    }
}
