using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic
{
    /// <summary>使用现有 EventSystem 的指针捕获，禁用实体碰撞后仍可收到松手。</summary>
    [DisallowMultipleComponent]
    public sealed class RobotBoardPointerInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler
    {
        private BattleShopSceneController _host;
        private int _instanceId;
        public void Bind(BattleShopSceneController host, int instanceId) { _host = host; _instanceId = instanceId; }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                _host?.PressBoardRobot(_instanceId, e.pointerId, e.position, Time.unscaledTime);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _host?.ShopWindow?.Drag.Release(e.pointerId, e.position);
        }
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold = false; }
        public void OnBeginDrag(PointerEventData e) { _host?.ShopWindow?.Drag.Move(e.pointerId, e.position); }
        public void OnDrag(PointerEventData e) { _host?.ShopWindow?.Drag.Move(e.pointerId, e.position); }
    }
}
