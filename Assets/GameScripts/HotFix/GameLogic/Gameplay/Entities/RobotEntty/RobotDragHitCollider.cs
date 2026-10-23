using UnityEngine;

namespace GameLogic
{
    /// <summary>Prefab 中预先保存的拖拽命中 Collider 标记，不参与机器人实体物理。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotDragHitCollider : MonoBehaviour
    {
    }
}
