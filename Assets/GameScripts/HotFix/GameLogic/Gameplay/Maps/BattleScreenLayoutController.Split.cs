using UnityEngine;

namespace GameLogic
{
    public sealed partial class BattleScreenLayoutController
    {
        [SerializeField] private Transform _playerWorldRoot;
        [SerializeField] private Transform _opponentWorldRoot;
        [SerializeField] private BattleSideRenderRegistry _renderRegistry;
        [SerializeField, Range(0.01f, 1f)] private float _opponentMaxSizeRatio = 1f;
        [SerializeField] private Color _playerClearColor = new Color32(33, 174, 216, 255);
        [SerializeField] private Color _opponentClearColor = new Color32(234, 178, 79, 255);
        [SerializeField] private Sprite _battleBackground;
        [SerializeField, Min(0.01f), Tooltip("9:16竖屏的环境背景缩放，以背景相机半高5为基准。")]
        private float _battleBackgroundScale9By16 = 0.57f;
        [SerializeField, Min(0.01f), Tooltip("9:18竖屏的环境背景缩放，以背景相机半高5为基准。")]
        private float _battleBackgroundScale9By18 = 0.52f;
        private const float BACKGROUND_REFERENCE_CAMERA_HEIGHT = 10f;
        private Camera _opponentCamera;
        private Camera _backgroundCamera;
        private SpriteRenderer _backgroundRenderer;
        private BattleInfoRenderLayer _infoRenderLayer;
        private CameraState _cameraState;
        private BattleScreenLayoutResult _layout;
        private Rect _lastInfoRect;
        private float _referencePixelsPerUnit;
        private float _referenceInfoScale;

        public Rect PlayerScreenRect => _layout.PlayerArea;
        public Rect OpponentScreenRect => _layout.OpponentArea;
        public Camera OpponentCamera => _opponentCamera;
        public BattleScreenLayoutResult Layout => _layout;

        private bool RefreshSplitLayout()
        {
            if (_window == null) return false;
            var window = _window;
            var generation = _bindingGeneration;
            _infoRenderLayer?.Refresh(window.Visible);
            var screen = new Vector2(Screen.width, Screen.height);
            var safe = BattleScreenLayout.Intersect(new Rect(Vector2.zero, screen), Screen.safeArea);
            Rect shop;
            Vector3 shopPosition, shopScale, infoPosition, infoScale;
            BattleScreenLayoutResult candidate;
            Vector2 playerCenter, opponentCenter;
            float playerSize, opponentSize;
            if (screen.x <= 0 || screen.y <= 0 || !window.TryCalculateShopLayout(safe, out shop, out shopPosition, out shopScale) ||
                !BattleScreenLayout.TryCalculateSplit(screen, safe, shop, window.ScreenLayoutScale, _playerBounds, _opponentBounds,
                    _referencePixelsPerUnit, _referenceInfoScale, _opponentMaxSizeRatio, out candidate) ||
                !window.TryCalculateInfoLayout(candidate.InfoTarget, out infoPosition, out infoScale) ||
                !BattleScreenLayout.TryCalculateCamera(candidate.PlayerViewport, candidate.PlayerTarget, _playerBounds,
                    candidate.PlayerPixelsPerUnit, out playerCenter, out playerSize) ||
                !BattleScreenLayout.TryCalculateCamera(candidate.OpponentViewport, candidate.OpponentTarget, _opponentBounds,
                    candidate.OpponentPixelsPerUnit, out opponentCenter, out opponentSize))
            {
                _placement.InvalidateScreenViews();
                window.Shop.Drag?.Cancel();
                return false;
            }
            window.Shop.Drag?.Cancel();
            // 事务取消通知可以同步终局或重绑；所有计算通过后也必须再次验证归属。
            if (_window != window || generation != _bindingGeneration || window.IsDestroyed || window.gameObject == null) return false;
            _placement.CancelDrag();
            EnsureCameras();
            window.ApplyScreenLayout(shopPosition, shopScale, infoPosition, infoScale);
            ApplyCamera(_sceneCamera, candidate.PlayerViewport, screen, playerCenter, playerSize);
            ApplyCamera(_opponentCamera, candidate.OpponentViewport, screen, opponentCenter, opponentSize);
            _sceneCamera.backgroundColor = _playerClearColor;
            _opponentCamera.backgroundColor = _opponentClearColor;
            RefreshBackground(screen);
            _layout = candidate;
            _availableScreenRect = Rect.MinMaxRect(candidate.PlayerArea.xMin, candidate.PlayerArea.yMin,
                candidate.OpponentArea.xMax, candidate.OpponentArea.yMax);
            _placement.SetScreenViews(_sceneCamera, candidate.PlayerArea, _opponentCamera, candidate.OpponentArea);
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
            _lastSafeArea = Screen.safeArea;
            _lastCanvasScale = window.ScreenLayoutScale;
            _lastShopRect = window.GetShopScreenRect();
            _lastInfoRect = window.GetInfoScreenRect();
            _hasLayout = true;
            return true;
        }

        private void EnsureCameras()
        {
            if (_opponentCamera != null) return;
            var backgroundMask = _battleBackground != null ? LayerMask.GetMask("BattleBackground") : 0;
            if (_battleBackground != null && backgroundMask == 0)
                throw new System.InvalidOperationException("战斗背景需要 BattleBackground 显示层。");
            _opponentCamera = CreateCamera("OpponentBattleCamera");
            _backgroundCamera = CreateCamera("BattleBackgroundCamera");
            var ui = _window.Canvas.rootCanvas.worldCamera;
            var depth = ui != null ? Mathf.Min(_cameraState.Depth, ui.depth - 2f) : _cameraState.Depth;
            _backgroundCamera.rect = new Rect(0, 0, 1, 1);
            _backgroundCamera.cullingMask = backgroundMask;
            _backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            _backgroundCamera.backgroundColor = _cameraState.Background;
            _backgroundCamera.depth = depth - 1f;
            if (_battleBackground != null)
            {
                var background = new GameObject("BattleBackground");
                background.layer = LayerMask.NameToLayer("BattleBackground");
                background.transform.SetParent(_backgroundCamera.transform, false);
                background.transform.localPosition = Vector3.forward *
                    Mathf.Lerp(_backgroundCamera.nearClipPlane, _backgroundCamera.farClipPlane, 0.5f);
                _backgroundRenderer = background.AddComponent<SpriteRenderer>();
                _backgroundRenderer.sprite = _battleBackground;
            }
            _sceneCamera.depth = depth;
            _opponentCamera.depth = depth + 0.1f;
            _sceneCamera.cullingMask &= ~((1 << LayerMask.NameToLayer("UI")) | backgroundMask | LayerMask.GetMask("BattleInfo"));
            _opponentCamera.cullingMask = _sceneCamera.cullingMask;
            // 背景相机先清屏，战场仅清深度，保留环境和先绘制的信息栏。
            _sceneCamera.clearFlags = _opponentCamera.clearFlags = CameraClearFlags.Depth;
            if (_renderRegistry == null) _renderRegistry = GetComponent<BattleSideRenderRegistry>();
            if (_renderRegistry == null) _renderRegistry = gameObject.AddComponent<BattleSideRenderRegistry>();
            _renderRegistry.BindCameras(_sceneCamera, _opponentCamera);
            RegisterStatic(_playerWorldRoot, _playerBoard, _playerFixedRenderers, BattleSide.Player);
            RegisterStatic(_opponentWorldRoot, _opponentBoard, _opponentFixedRenderers, BattleSide.Opponent);
            _backgroundCamera.enabled = true;
            _opponentCamera.enabled = true;
            _infoRenderLayer = new BattleInfoRenderLayer(_window.Info.rectTransform, _window.Canvas, ui, transform, depth - 0.5f);
            _infoRenderLayer.Refresh(_window.Visible);
        }

        internal void SetInformationVisible(bool visible) => _infoRenderLayer?.Refresh(visible);

        private void RefreshBackground(Vector2 screen)
        {
            if (_backgroundRenderer == null) return;
            var size = _battleBackground.bounds.size;
            var height = _backgroundCamera.orthographicSize * 2f;
            _backgroundCamera.aspect = screen.x / screen.y;
            // 按屏幕高宽比在两种已确认的构图之间过渡；相机高度变化时保持相同的屏幕构图。
            var ratio = screen.y / screen.x;
            var blend = Mathf.InverseLerp(16f / 9f, 18f / 9f, ratio);
            var calibratedScale = Mathf.Lerp(_battleBackgroundScale9By16, _battleBackgroundScale9By18, blend) *
                height / BACKGROUND_REFERENCE_CAMERA_HEIGHT;
            // 更宽或更长的屏幕也必须铺满，避免背景边缘露底。
            var coverScale = Mathf.Max(height * _backgroundCamera.aspect / size.x, height / size.y);
            var scale = Mathf.Max(coverScale, calibratedScale);
            _backgroundRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private Camera CreateCamera(string cameraName)
        {
            var root = new GameObject(cameraName);
            root.transform.SetParent(transform, false);
            var camera = root.AddComponent<Camera>();
            camera.CopyFrom(_sceneCamera);
            camera.enabled = false;
            return camera;
        }

        private void RegisterStatic(Transform root, PlacementBoardView board, SpriteRenderer[] renderers, BattleSide side)
        {
            if (root != null) _renderRegistry.RegisterRoot(root.gameObject, side);
            _renderRegistry.RegisterRoot(board.gameObject, side);
            foreach (var renderer in renderers) _renderRegistry.RegisterRoot(renderer.gameObject, side);
        }

        private void ApplyCamera(Camera camera, Rect viewport, Vector2 screen, Vector2 center, float size)
        {
            camera.rect = new Rect(viewport.x / screen.x, viewport.y / screen.y, viewport.width / screen.x, viewport.height / screen.y);
            camera.orthographicSize = size;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, _originalPosition.z), Quaternion.identity);
        }

        private static void DestroyCamera(ref Camera camera)
        {
            if (camera == null) return;
            camera.enabled = false;
            if (Application.isPlaying) Destroy(camera.gameObject);
            else DestroyImmediate(camera.gameObject);
            camera = null;
        }

        private readonly struct CameraState
        {
            public readonly Color Background;
            public readonly float Depth;
            private readonly CameraClearFlags _clear;
            private readonly int _mask;

            public CameraState(Camera camera)
            {
                Background = camera.backgroundColor;
                Depth = camera.depth;
                _clear = camera.clearFlags;
                _mask = camera.cullingMask;
            }
            public void Restore(Camera camera)
            {
                camera.backgroundColor = Background;
                camera.depth = Depth;
                camera.clearFlags = _clear;
                camera.cullingMask = _mask;
            }
        }
    }
}
