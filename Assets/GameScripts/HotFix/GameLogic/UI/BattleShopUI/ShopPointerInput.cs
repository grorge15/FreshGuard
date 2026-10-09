using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic
{
    /// <summary>只转发指针事件，不购买、不占格。来源图标隐藏时组件仍保持启用。</summary>
    [DisallowMultipleComponent]
    public sealed class ShopPointerInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler
    {
        public Action<int, Vector2, float> Pressed;
        public Action<int, Vector2> Moved;
        public Action<int, Vector2> Released;

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                Pressed?.Invoke(e.pointerId, e.position, Time.unscaledTime);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) Released?.Invoke(e.pointerId, e.position);
        }
        public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold = false; }
        public void OnBeginDrag(PointerEventData e) { Moved?.Invoke(e.pointerId, e.position); }
        public void OnDrag(PointerEventData e) { Moved?.Invoke(e.pointerId, e.position); }
        private void OnDestroy() { Pressed = null; Moved = null; Released = null; }
    }
}
