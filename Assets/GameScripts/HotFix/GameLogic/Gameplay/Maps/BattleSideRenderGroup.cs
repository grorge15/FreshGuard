using UnityEngine;

namespace GameLogic
{
    /// <summary>显式保存一组表现的战场归属；对象池禁用时解除登记，重新取出时恢复。</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BattleSideRenderGroup : MonoBehaviour
    {
        public BattleSideRenderRegistry Registry { get; private set; }
        public BattleSide Side { get; private set; }

        public static void BindRoot(GameObject root, BattleSideRenderRegistry registry, BattleSide side)
        {
            if (root == null) return;
            var group = root.GetComponent<BattleSideRenderGroup>();
            if (registry == null) { if (group != null) group.Unbind(); return; }
            if (group == null) group = root.AddComponent<BattleSideRenderGroup>();
            group.Bind(registry, side);
        }

        public void Bind(BattleSideRenderRegistry registry, BattleSide side)
        {
            EntityValidation.RequireSide(side);
            if (Registry != registry) Unbind();
            Registry = registry;
            Side = side;
            if (Registry != null) Registry.Attach(this);
        }

        public void Unbind()
        {
            var registry = Registry;
            Registry = null;
            if (registry != null) registry.Detach(this);
        }

        private void OnEnable() { if (Registry != null) Registry.Refresh(this); }
        private void OnDisable() { if (Registry != null) Registry.Refresh(this); }
        private void OnDestroy() { Unbind(); }
        private void OnTransformChildrenChanged() { if (Registry != null) Registry.Refresh(this); }
    }
}
