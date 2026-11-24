using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    // 输入始终由最初按住按钮的指针持有，离开按钮后仍可拖到战场。
    public sealed class PlayerBlastButton : MaskableGraphic, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, ICancelHandler
    {
        private BattleShopSceneController _host;
        private int? _pointer;
        private bool _dragged;
        private Vector2 _screen;
        private float _remainingFraction;
        private bool _ready;
        private BlastTouchRing _ring;
        private BlastAimArrow _arrow;
        private double _flashUntil;
        private static readonly Color Gold = new Color32(255, 214, 108, 255);
        private static readonly Color Warm = new Color32(255, 248, 222, 255);

        internal static PlayerBlastButton Create(RectTransform panel)
        {
            var go = new GameObject("PlayerBlast", typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = panel.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.095f, .45f);
            rect.sizeDelta = new Vector2(112, 112);
            return go.AddComponent<PlayerBlastButton>();
        }

        internal void Bind(BattleShopSceneController host)
        {
            CancelAim();
            _host = host;
        }

        private bool Available => _host != null && _host.CanInteractWithRobots &&
            _host.ShopWindow?.Drag?.Phase == RobotDragPhase.Idle && _host.Enemies != null && _host.Enemies.BlastReady;

        public void OnPointerDown(PointerEventData e)
        {
            if (!Available || _pointer.HasValue || e.button != PointerEventData.InputButton.Left) return;
            _pointer = e.pointerId; _dragged = false; _screen = e.position;
        }
        public void OnDrag(PointerEventData e)
        {
            if (_pointer != e.pointerId) return;
            _dragged = true; _screen = e.position;
            Preview();
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (_pointer != e.pointerId) return;
            bool released = Available && _dragged && !CanceledTouch(e.pointerId) &&
                _host.TryPlayerBlastPointer(e.position, out var world) && _host.Enemies.TryReleaseBlast(world, out _);
            CancelAim();
            if (released)
            {
                // 松手位置重新校验；空放也显示一次命中圈。
                DrawRing(e.position, Warm);
                _flashUntil = _host.Enemies.ElapsedSeconds + .22;
            }
        }
        private static bool CanceledTouch(int id)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.fingerId == id) return touch.phase == TouchPhase.Canceled;
            }
            return false;
        }
        public void OnCancel(BaseEventData e) => CancelAim();
        internal void CancelAim()
        {
            _pointer = null; _dragged = false; _flashUntil = 0;
            if (_arrow != null) _arrow.enabled = false;
            if (_ring != null) _ring.enabled = false;
        }
        private void Preview()
        {
            if (Available && _dragged) DrawRing(_screen, Gold);
        }
        private void Update()
        {
            var ready = Available;
            var remaining = _host != null && _host.Enemies != null
                ? Mathf.Clamp01(_host.Enemies.BlastRemaining / EnemyBattleController.PlayerBlastCooldown) : 0f;
            if (_remainingFraction != remaining || _ready != ready)
            {
                _remainingFraction = remaining; _ready = ready;
                SetVerticesDirty();
            }
            if (_pointer.HasValue) { if (!ready) CancelAim(); else Preview(); }
            else if (_ring != null && _ring.enabled && (_host == null || _host.Enemies == null ||
                !_host.IsReady || _host.Enemies.ElapsedSeconds >= _flashUntil)) _ring.enabled = false;
        }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelAim(); }
        protected override void OnDisable() { CancelAim(); base.OnDisable(); }
        protected override void OnDestroy()
        {
            if (_ring != null)
            {
                if (Application.isPlaying) Destroy(_ring.gameObject); else DestroyImmediate(_ring.gameObject);
            }
            base.OnDestroy();
        }

        private void DrawRing(Vector2 screen, Color tint)
        {
            // 预览属于屏幕输入层，不能被任一战场相机的视口裁掉。
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root == null) return;
            var parent = (RectTransform)root.transform;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var radiusPx = _host.Placement.GetScreenCellHeight(BattleSide.Player);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var local) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen + Vector2.up * radiusPx,
                    camera, out var edge)) return;
            if (_ring == null)
            {
                var go = new GameObject("BlastTouchRadius", typeof(RectTransform), typeof(CanvasRenderer));
                go.layer = root.gameObject.layer;
                _ring = go.AddComponent<BlastTouchRing>();
                _ring.raycastTarget = false;
                _ring.rectTransform.SetParent(parent, false);
            }
            _ring.rectTransform.localPosition = local;
            _ring.rectTransform.sizeDelta = Vector2.one * (2f * Vector2.Distance(edge, local));
            _ring.color = tint; _ring.enabled = true;
            if (_dragged) DrawArrow(parent, local);
        }

        private void DrawArrow(RectTransform parent, Vector2 tip)
        {
            if (_arrow == null)
            {
                var go = new GameObject("BlastAimArrow", typeof(RectTransform), typeof(CanvasRenderer));
                go.layer = _ring.gameObject.layer;
                _arrow = go.AddComponent<BlastAimArrow>();
                _arrow.raycastTarget = false; _arrow.color = Gold;
                _arrow.rectTransform.SetParent(_ring.rectTransform, false);
                _arrow.rectTransform.pivot = new Vector2(0, .5f);
            }
            Vector2 start = parent.InverseTransformPoint(rectTransform.TransformPoint(rectTransform.rect.center));
            var delta = tip - start;
            var rect = _arrow.rectTransform;
            rect.localPosition = -delta;
            rect.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            rect.sizeDelta = new Vector2(delta.magnitude, 20f);
            _arrow.enabled = delta.sqrMagnitude > 1f;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float r = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .5f;
            Disc(mesh, r, new Color32(89, 54, 35, 255));
            Disc(mesh, r * .93f, (_ready || _remainingFraction > 0f) ? Gold : new Color32(167, 155, 137, 255));
            Disc(mesh, r * .82f, (_ready || _remainingFraction > 0f) ? new Color32(216, 84, 34, 255) : new Color32(93, 86, 80, 255));
            // 准星图标：双环和四向刻度，在不同 DPI 下直接由矢量几何绘制。
            Disc(mesh, r * .49f, Warm);
            Disc(mesh, r * .39f, (_ready || _remainingFraction > 0f) ? new Color32(216, 84, 34, 255) : new Color32(93, 86, 80, 255));
            Disc(mesh, r * .13f, Warm);
            for (int i = 0; i < 4; i++)
            {
                var axis = (Vector2)(Quaternion.Euler(0, 0, 90 * i) * Vector3.up);
                var edge = new Vector2(-axis.y, axis.x) * (r * .045f);
                var n = mesh.currentVertCount;
                foreach (var p in new[] { axis * (.32f*r) - edge, axis * (.64f*r) - edge,
                    axis * (.64f*r) + edge, axis * (.32f*r) + edge }) mesh.AddVert(p, Warm, Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2); mesh.AddTriangle(n,n+2,n+3);
            }
            if (_remainingFraction <= 0f) return;
            // 十二点起顺时针揭开遮罩，剩余扇形对应剩余冷却时间。
            int start = mesh.currentVertCount;
            var shade = new Color32(24, 18, 22, 186);
            mesh.AddVert(Vector3.zero, shade, Vector2.zero);
            int steps = Mathf.CeilToInt(48 * _remainingFraction);
            for (int i = 0; i <= steps; i++)
            {
                float a = 2 * Mathf.PI * (1 - _remainingFraction + _remainingFraction * i / steps);
                mesh.AddVert(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (r * .93f), shade, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start+i+1, start+i);
            }
        }
        private static void Disc(VertexHelper mesh, float radius, Color tint)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(Vector3.zero, tint, Vector2.zero);
            for (int i = 0; i < 48; i++)
            {
                float a = i * (Mathf.PI / 24);
                mesh.AddVert(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, tint, Vector2.zero);
                mesh.AddTriangle(n, n+1+i, n+1+(i+1)%48);
            }
        }
    }
    internal sealed class BlastAimArrow : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float width = rectTransform.rect.width;
            float head = Mathf.Min(20f, width * .5f);
            float end = width - head;
            for (float x = 0; x < end; x += 22f)
            {
                float next = Mathf.Min(x + 13f, end);
                int n = mesh.currentVertCount;
                mesh.AddVert(new Vector2(x, -2), color, Vector2.zero);
                mesh.AddVert(new Vector2(next, -2), color, Vector2.zero);
                mesh.AddVert(new Vector2(next, 2), color, Vector2.zero);
                mesh.AddVert(new Vector2(x, 2), color, Vector2.zero);
                mesh.AddTriangle(n,n+1,n+2); mesh.AddTriangle(n,n+2,n+3);
            }
            int i = mesh.currentVertCount;
            mesh.AddVert(new Vector2(end, -head * .45f), color, Vector2.zero);
            mesh.AddVert(new Vector2(width, 0), color, Vector2.zero);
            mesh.AddVert(new Vector2(end, head * .45f), color, Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);
        }
    }

    internal sealed class BlastTouchRing : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            float radius = rectTransform.rect.width * .5f;
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI / 24;
                var outer = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                mesh.AddVert(outer, color, Vector2.zero);
                mesh.AddVert(outer * .925f, color, Vector2.zero);
                int n = i*2, next = (n+2)%96;
                mesh.AddTriangle(n,next,n+1); mesh.AddTriangle(next,next+1,n+1);
            }
        }
    }
}
