using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>玻璃与击杀奖励的纯展示飞币，不参与货币结算。数值以720宽画面为基准。</summary>
    public sealed class GlassCurrencyFlightWidget : UIWidget
    {
        public const int IconsPerGlass = 4;
        public const int MaxActiveIcons = 128;
        public const float ScatterDuration = 0.22f;
        public const float FlightDelay = 0.32f;
        public const float FlightDuration = 0.56f;
        private const float PulseDuration = 0.3f;
        private static readonly float[] Angles = { -2.52f, -1.85f, -1.06f, -0.35f };

        private sealed class Coin
        {
            public Image Image;
            public Vector2 Origin;
            public Vector2 Burst;
            public float Age;
            public float Launch;
            public float Scale;
        }

        private readonly List<Coin> _active = new List<Coin>(MaxActiveIcons);
        private readonly Stack<Coin> _pool = new Stack<Coin>();
        private Image _template;
        private Canvas _canvas;
        private Camera _sceneCamera;
        private RectTransform _target;
        private Vector3 _targetScale;
        private BattleShopContext _context;
        private float _pulseAge = PulseDuration;
        public int ActiveCount => _active.Count;

        protected override void ScriptGenerator()
        {
            _template = transform.Find("CoinTemplate").GetComponent<Image>();
            _canvas = gameObject.GetComponentInParent<Canvas>();
        }

        protected override void OnCreate() { _template.gameObject.SetActive(false); }

        public void Bind(BattleShopContext context, Camera sceneCamera, RectTransform target)
        {
            if (ReferenceEquals(context, _context) && _target == target && _sceneCamera == sceneCamera) return;
            Clear();
            _context = context;
            _sceneCamera = sceneCamera;
            _target = target;
            _targetScale = target != null ? target.localScale : Vector3.one;
        }

        public void Show(Vector3 worldOrigin, int iconCount = IconsPerGlass)
        {
            if (_sceneCamera == null || _target == null || !_target.gameObject.activeInHierarchy ||
                !gameObject.activeInHierarchy || iconCount <= 0 || iconCount > MaxActiveIcons - _active.Count) return;
            var screen = _sceneCamera.WorldToScreenPoint(worldOrigin);
            if (screen.z <= 0f) return;
            var rootCanvas = _canvas != null ? _canvas.rootCanvas : null;
            var uiCamera = rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : (rootCanvas.worldCamera != null ? rootCanvas.worldCamera : GameModule.UI.UICamera);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, uiCamera, out var origin)) return;
            var scale = rectTransform.rect.width / 720f;
            for (var i = 0; i < iconCount; i++)
            {
                var coin = Rent();
                coin.Origin = origin;
                var radius = 44f * (0.82f + 0.16f * (i % 3)) * scale;
                var angle = Angles[i % Angles.Length];
                // 预览坐标Y朝下，Unity局部坐标Y朝上。
                coin.Burst = origin + new Vector2(Mathf.Cos(angle) * radius, -Mathf.Sin(angle) * radius + 3f * scale);
                coin.Age = -i * 0.018f;
                coin.Launch = FlightDelay + i * 0.028f;
                coin.Scale = scale;
                _active.Add(coin);
                Render(coin);
            }
        }

        private Coin Rent()
        {
            if (_pool.Count > 0) return _pool.Pop();
            var image = UnityEngine.Object.Instantiate(_template, rectTransform, false);
            image.name = "FlyingEnergyCoin";
            image.raycastTarget = false;
            return new Coin { Image = image };
        }

        private void Release(Coin coin)
        {
            if (coin.Image != null) coin.Image.gameObject.SetActive(false);
            _pool.Push(coin);
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (_target == null || !_target.gameObject.activeInHierarchy ||
                (_context != null && (_context.IsEnded || _context.IsEndRequested)))
            { Clear(); return; }
            _pulseAge = Mathf.Min(PulseDuration, _pulseAge + deltaTime);
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var coin = _active[i];
                coin.Age += deltaTime;
                if (coin.Age >= coin.Launch + FlightDuration)
                {
                    Release(coin);
                    _active.RemoveAt(i);
                    _pulseAge = 0f;
                }
                else Render(coin);
            }
            _target.localScale = _targetScale * (1f + 0.16f * Mathf.Sin(Mathf.PI * _pulseAge / PulseDuration));
        }

        private void Render(Coin coin)
        {
            coin.Image.gameObject.SetActive(coin.Age >= 0f);
            if (coin.Age < 0f) return;
            Vector2 position;
            float size;
            float alpha = 1f;
            if (coin.Age < ScatterDuration)
            {
                var p = Mathf.Clamp01(coin.Age / ScatterDuration);
                var eased = 1f - Mathf.Pow(1f - p, 3f);
                position = Vector2.LerpUnclamped(coin.Origin, coin.Burst, eased) + Vector2.up * (Mathf.Sin(p * Mathf.PI) * 8f * coin.Scale);
                size = Mathf.Lerp(11f, 29f, eased);
            }
            else
            {
                var raw = Mathf.Clamp01((coin.Age - coin.Launch) / FlightDuration);
                // 直线插值搭配三次方加速：起步慢，接近商店时快速吸入。
                var target = (Vector2)rectTransform.InverseTransformPoint(_target.TransformPoint(_target.rect.center));
                position = Vector2.LerpUnclamped(coin.Burst, target, raw * raw * raw);
                size = Mathf.Lerp(29f, 13f, Mathf.Clamp01((raw - 0.68f) / 0.32f));
                alpha = 1f - Mathf.Clamp01((raw - 0.94f) / 0.06f);
            }
            coin.Image.rectTransform.anchoredPosition = position;
            coin.Image.rectTransform.sizeDelta = Vector2.one * (size * coin.Scale);
            coin.Image.color = new Color(1f, 1f, 1f, alpha);
        }

        public void Clear()
        {
            foreach (var coin in _active) Release(coin);
            _active.Clear();
            _pulseAge = PulseDuration;
            if (_target != null) _target.localScale = _targetScale;
        }

        protected override void OnUpdate() { Advance(Time.unscaledDeltaTime); }
        protected override void OnSetVisible(bool visible) { if (!visible) Clear(); }
        protected override void OnDestroy()
        {
            Clear();
            _pool.Clear();
            _context = null;
            _sceneCamera = null;
            _target = null;
        }
    }
}
