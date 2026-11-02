using UnityEngine;

namespace GameLogic
{
    /// <summary>将固定的世界边界等比容纳到全屏正交相机中的指定像素区域。</summary>
    public static class BattleScreenLayout
    {
        public static bool TryCalculateReference(Bounds player, Bounds opponent, Vector2 shopDesignSize,
            out float pixelsPerUnit, out float infoScale)
        {
            pixelsPerUnit = infoScale = 0f;
            if (!Positive(shopDesignSize.x) || !Positive(shopDesignSize.y) || !ValidBounds(player) || !ValidBounds(opponent)) return false;
            var shopHeight = shopDesignSize.y * Mathf.Min(1f, 1056f / shopDesignSize.x);
            var combined = player;
            combined.Encapsulate(opponent);
            Vector2 center;
            float size;
            if (!TryCalculate(new Vector2(1080, 1920),
                Rect.MinMaxRect(16, 12 + shopHeight + 16, 1064, 1904), combined, out center, out size)) return false;
            pixelsPerUnit = 1920f / (2f * size);
            // 仅战场继承V1显示比例。信息栏使用Prefab原始尺寸，不继承旧世界空隙限制。
            infoScale = 1f;
            return true;
        }

        public static bool TryCalculateSplit(Vector2 screenSize, Rect safeArea, Rect shop, float canvasScale,
            Bounds player, Bounds opponent, float referencePixelsPerUnit, float referenceInfoScale,
            float opponentMaxSizeRatio, out BattleScreenLayoutResult result)
        {
            result = default;
            if (!Positive(screenSize.x) || !Positive(screenSize.y) || !Positive(canvasScale) ||
                !Positive(referencePixelsPerUnit) || !Positive(referenceInfoScale) ||
                !Positive(opponentMaxSizeRatio) || opponentMaxSizeRatio > 1f ||
                !ValidBounds(player) || !ValidBounds(opponent) || !ValidRect(safeArea) || !ValidRect(shop)) return false;
            var safe = Intersect(new Rect(Vector2.zero, screenSize), safeArea);
            var margin = 16f * canvasScale;
            var width = safe.width - 2f * margin;
            if (!Positive(width)) return false;
            var infoScale = Mathf.Min(referenceInfoScale, width / (1040f * canvasScale));
            var infoHeight = 156f * infoScale * canvasScale;
            var bottom = Mathf.Max(safe.yMin, shop.yMax) + margin;
            var boardsHeight = safe.yMax - margin - bottom - infoHeight - 2f * margin;
            if (!Positive(boardsHeight)) return false;
            var playerPpu = Mathf.Min(referencePixelsPerUnit * canvasScale, width / player.size.x);
            // 信息栏恢复原始高度后，由敌方让出所需空间；仅比16:9更矮的可用区域缩小我方。
            var referencePpu = referencePixelsPerUnit * canvasScale;
            var referenceBottom = 12f * canvasScale + shop.height + margin;
            var referenceBoardsHeight = 1920f * canvasScale - margin - referenceBottom - infoHeight - 2f * margin;
            var referenceEnemyHeight = Mathf.Min(opponent.size.y * referencePpu,
                referenceBoardsHeight - player.size.y * referencePpu);
            var combinedHeightPerUnit = player.size.y + (referenceEnemyHeight > 0f
                ? referenceEnemyHeight / referencePpu : opponent.size.y);
            playerPpu = Mathf.Min(playerPpu, boardsHeight / combinedHeightPerUnit);
            if (!Positive(playerPpu)) return false;
            var playerTarget = CenteredBottom(safe.center.x, bottom, (Vector2)player.size * playerPpu);
            var infoTarget = CenteredBottom(safe.center.x, playerTarget.yMax + margin,
                new Vector2(1040, 156) * (infoScale * canvasScale));
            var enemyBottom = infoTarget.yMax + margin;
            var enemyHeight = safe.yMax - margin - enemyBottom;
            var opponentPpu = Mathf.Min(Mathf.Min(width, playerTarget.width * opponentMaxSizeRatio) / opponent.size.x,
                Mathf.Min(enemyHeight, playerTarget.height * opponentMaxSizeRatio) / opponent.size.y);
            if (!Positive(opponentPpu)) return false;
            var opponentTarget = CenteredBottom(safe.center.x, enemyBottom, (Vector2)opponent.size * opponentPpu);
            // Unity 的相机像素高度为整数，分界对齐像素可避免亚像素视口改变实际投影比例。
            var split = Mathf.Round(infoTarget.center.y);
            result = new BattleScreenLayoutResult(playerTarget, opponentTarget, infoTarget,
                new Rect(safe.xMin + margin, bottom, width, playerTarget.height),
                new Rect(safe.xMin + margin, enemyBottom, width, enemyHeight),
                new Rect(0, 0, screenSize.x, split),
                new Rect(0, split, screenSize.x, screenSize.y - split), playerPpu, opponentPpu);
            return true;
        }

        public static bool TryCalculateCamera(Rect viewportPixels, Rect target, Bounds bounds, float pixelsPerUnit,
            out Vector2 center, out float size)
        {
            center = default;
            size = 0;
            if (!ValidRect(viewportPixels) || !ValidRect(target) || !ValidBounds(bounds) || !Positive(pixelsPerUnit)) return false;
            center = (Vector2)bounds.center - (target.center - viewportPixels.center) / pixelsPerUnit;
            size = viewportPixels.height / (2f * pixelsPerUnit);
            return Finite(center.x) && Finite(center.y) && Positive(size);
        }

        private static Rect CenteredBottom(float x, float bottom, Vector2 size) => new Rect(x - size.x * 0.5f, bottom, size.x, size.y);
        private static bool ValidBounds(Bounds bounds) => Positive(bounds.size.x) && Positive(bounds.size.y) &&
            Finite(bounds.center.x) && Finite(bounds.center.y);
        private static bool ValidRect(Rect rect) => Finite(rect.x) && Finite(rect.y) && Positive(rect.width) && Positive(rect.height);

        public static bool TryCalculate(Vector2 screenSize, Rect available, Bounds worldBounds,
            out Vector2 cameraCenter, out float orthographicSize)
        {
            cameraCenter = default;
            orthographicSize = 0f;
            if (!Positive(screenSize.x) || !Positive(screenSize.y) ||
                !Positive(available.width) || !Positive(available.height) ||
                !Finite(available.x) || !Finite(available.y) ||
                available.xMin < 0 || available.yMin < 0 ||
                available.xMax > screenSize.x || available.yMax > screenSize.y ||
                !Positive(worldBounds.size.x) || !Positive(worldBounds.size.y) ||
                !Finite(worldBounds.center.x) || !Finite(worldBounds.center.y)) return false;
            var pixelsPerUnit = Mathf.Min(available.width / worldBounds.size.x, available.height / worldBounds.size.y);
            if (!Positive(pixelsPerUnit)) return false;
            cameraCenter = (Vector2)worldBounds.center - (available.center - screenSize * 0.5f) / pixelsPerUnit;
            orthographicSize = screenSize.y / (2f * pixelsPerUnit);
            return Finite(cameraCenter.x) && Finite(cameraCenter.y) && Positive(orthographicSize);
        }

        public static Rect Intersect(Rect a, Rect b) => Rect.MinMaxRect(
            Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin),
            Mathf.Max(Mathf.Max(a.xMin, b.xMin), Mathf.Min(a.xMax, b.xMax)),
            Mathf.Max(Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.yMax, b.yMax)));

        private static bool Positive(float value) => Finite(value) && value > 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct BattleScreenLayoutResult
    {
        public Rect PlayerTarget { get; }
        public Rect OpponentTarget { get; }
        public Rect InfoTarget { get; }
        public Rect PlayerArea { get; }
        public Rect OpponentArea { get; }
        public Rect PlayerViewport { get; }
        public Rect OpponentViewport { get; }
        public float PlayerPixelsPerUnit { get; }
        public float OpponentPixelsPerUnit { get; }

        public BattleScreenLayoutResult(Rect player, Rect opponent, Rect info, Rect playerArea, Rect opponentArea,
            Rect playerViewport, Rect opponentViewport, float playerPpu, float opponentPpu)
        {
            PlayerTarget = player;
            OpponentTarget = opponent;
            InfoTarget = info;
            PlayerArea = playerArea;
            OpponentArea = opponentArea;
            PlayerViewport = playerViewport;
            OpponentViewport = opponentViewport;
            PlayerPixelsPerUnit = playerPpu;
            OpponentPixelsPerUnit = opponentPpu;
        }
    }
}
