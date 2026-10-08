namespace GameLogic
{
    /// <summary>单局实体身份；不承载 Unity 物体或两类实体的专属战斗属性。</summary>
    public abstract class Entity
    {
        public int InstanceId { get; private set; }
        public int ConfigId { get; private set; }
        public BattleSide Side { get; private set; }
        public EntityRemovalReason RemovalReason { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsRemoved => !IsInitialized || RemovalReason != EntityRemovalReason.None;
        public abstract EntityKind Kind { get; }

        protected Entity() { }

        protected void InitializeIdentity(int instanceId, int configId, BattleSide side)
        {
            InstanceId = instanceId;
            ConfigId = configId;
            Side = side;
            RemovalReason = EntityRemovalReason.None;
            IsInitialized = true;
        }

        protected void ClearIdentity()
        {
            InstanceId = 0;
            ConfigId = 0;
            Side = default(BattleSide);
            RemovalReason = EntityRemovalReason.None;
            IsInitialized = false;
        }

        internal void MarkRemoved(EntityRemovalReason reason)
        {
            RemovalReason = reason;
            OnRemoved();
        }

        protected virtual void OnRemoved() { }
    }
}
