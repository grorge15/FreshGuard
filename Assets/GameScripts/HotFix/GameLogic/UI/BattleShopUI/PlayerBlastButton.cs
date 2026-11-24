using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
    // 输入始终由最初按住按钮的指针持有，离开按钮后仍可拖到战场。
    public sealed class PlayerBlastButton : MaskableGraphic, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, ICancelHandler
    {
        private const float ButtonDiameter = 112f;
        private const float TouchMargin = 14f;
        private BattleShopSceneController _host;
        private int? _pointer;
        private bool _dragged;
        private Vector2 _screen;
        private float _remainingFraction;
        private bool _ready;
        private double _readyStartedAt;
        private Sprite _buttonSprite;
        public override Texture mainTexture => _buttonSprite != null ? _buttonSprite.texture : base.mainTexture;
        private Image _ring;
        private double _rangeStartedAt;
        private const float RangeFramesPerSecond = 20f;
        private const float RangeCanvasToVisibleRadius = 100f / 82f;
        private double _flashUntil;

        internal static PlayerBlastButton Create(RectTransform panel)
        {
            var go = new GameObject("PlayerBlast", typeof(RectTransform), typeof(CanvasRenderer));
            go.layer = panel.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.095f, .45f);
            rect.sizeDelta = Vector2.one * (ButtonDiameter + 2f * TouchMargin);
            return go.AddComponent<PlayerBlastButton>();
        }

        internal void Bind(BattleShopSceneController host)
        {
            CancelAim();
            _host = host;
            ResetReadyArt();
        }

        private Sprite DisabledSprite => _host != null && _host.Enemies != null ? _host.Enemies.PotLidDisabled : null;

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
                DrawRing(e.position);
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
            if (_ring != null) _ring.enabled = false;
        }
        private void Preview()
        {
            if (Available && _dragged) DrawRing(_screen);
        }
        private void Update()
        {
            var ready = Available;
            var remaining = _host != null && _host.Enemies != null
                ? Mathf.Clamp01(_host.Enemies.BlastRemaining / EnemyBattleController.PlayerBlastCooldown) : 0f;
            if (ready && !_ready) _readyStartedAt = _host.Enemies.ElapsedSeconds;
            var sprite = ready ? _host.Enemies.GetPotLidReadyFrame(Mathf.Max(0,
                (int)((_host.Enemies.ElapsedSeconds - _readyStartedAt) * 20d))) : DisabledSprite;
            if (_buttonSprite != sprite)
            {
                _buttonSprite = sprite;
                SetMaterialDirty();
                SetVerticesDirty();
            }
            if (_remainingFraction != remaining || _ready != ready)
            {
                _remainingFraction = remaining; _ready = ready;
                SetVerticesDirty();
            }
            if (_pointer.HasValue) { if (!ready) CancelAim(); else Preview(); }
            else if (_ring != null && _ring.enabled && (_host == null || _host.Enemies == null ||
                !_host.IsReady || _host.Enemies.ElapsedSeconds >= _flashUntil)) _ring.enabled = false;
            else if (_ring != null && _ring.enabled) SampleRangeFrame();
        }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelAim(); }
        private void ResetReadyArt()
        {
            _buttonSprite = DisabledSprite;
            _remainingFraction = 0f;
            _ready = false;
            SetMaterialDirty();
            SetVerticesDirty();
        }
        protected override void OnDisable() { CancelAim(); ResetReadyArt(); base.OnDisable(); }
        protected override void OnDestroy()
        {
            if (_ring != null)
            {
                if (Application.isPlaying) Destroy(_ring.gameObject); else DestroyImmediate(_ring.gameObject);
            }
            base.OnDestroy();
        }

        private void DrawRing(Vector2 screen)
        {
            // 预览属于屏幕输入层，不能被任一战场相机的视口裁掉。
            var root = canvas != null ? canvas.rootCanvas : null;
            if (root == null) return;
            var parent = (RectTransform)root.transform;
            var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            var radiusPx = _host.Placement.GetScreenCellHeight(BattleSide.Player) * EnemyBattleController.BlastDamageRadiusScale;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var local) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen + Vector2.up * radiusPx,
                    camera, out var edge)) return;
            if (_ring == null)
            {
                var go = new GameObject("BlastTouchRadius", typeof(RectTransform), typeof(CanvasRenderer));
                go.layer = root.gameObject.layer;
                _ring = go.AddComponent<Image>();
                _ring.enabled = false;
                _ring.raycastTarget = false;
                _ring.rectTransform.SetParent(parent, false);
            }
            _ring.rectTransform.localPosition = local;
            _ring.rectTransform.sizeDelta = Vector2.one * (2f * RangeCanvasToVisibleRadius * Vector2.Distance(edge, local));
            if (!_ring.enabled) _rangeStartedAt = _host.Enemies.ElapsedSeconds;
            _ring.enabled = true;
            SampleRangeFrame();
        }

        private void SampleRangeFrame()
        {
            var frame = (int)((_host.Enemies.ElapsedSeconds - _rangeStartedAt) * RangeFramesPerSecond);
            var sprite = _host.Enemies.GetBlastRangeFrame(Mathf.Max(0, frame));
            _ring.sprite = sprite;
            _ring.color = sprite != null ? Color.white : Color.clear;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_buttonSprite == null) return;
            var half = _buttonSprite.rect.size * .5f;
            var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(_buttonSprite);
            mesh.AddVert(new Vector2(-half.x, -half.y), Color.white, new Vector2(uv.x, uv.y));
            mesh.AddVert(new Vector2(-half.x, half.y), Color.white, new Vector2(uv.x, uv.w));
            mesh.AddVert(half, Color.white, new Vector2(uv.z, uv.w));
            mesh.AddVert(new Vector2(half.x, -half.y), Color.white, new Vector2(uv.z, uv.y));
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
            if (_remainingFraction <= 0f) return;
            // 十二点起顺时针揭开遮罩，剩余扇形对应剩余冷却时间。
            int start = mesh.currentVertCount;
            var shade = new Color32(24, 18, 22, 186);
            var centerUV = new Vector2((uv.x + uv.z) * .5f, (uv.y + uv.w) * .5f);
            var uvPerUnit = new Vector2((uv.z - uv.x) / (2f * half.x), (uv.w - uv.y) / (2f * half.y));
            mesh.AddVert(Vector3.zero, shade, centerUV);
            int steps = Mathf.CeilToInt(48 * _remainingFraction);
            for (int i = 0; i <= steps; i++)
            {
                float a = 2 * Mathf.PI * (1 - _remainingFraction + _remainingFraction * i / steps);
                var point = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (ButtonDiameter * .465f);
                mesh.AddVert(point, shade, centerUV + Vector2.Scale(point, uvPerUnit));
                if (i > 0) mesh.AddTriangle(start, start+i+1, start+i);
            }
        }
    }
}
