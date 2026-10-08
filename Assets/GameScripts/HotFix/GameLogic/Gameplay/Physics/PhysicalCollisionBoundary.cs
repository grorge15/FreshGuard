using System;
using UnityEngine;

namespace GameLogic
{
    /// <summary>由棋盘生成完成后配置的矩形静态外框。</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalCollisionBoundary : MonoBehaviour
    {
        [SerializeField] private Vector2 size;
        [SerializeField] private float wallThickness = 0.25f;
        public Vector2 Size => size;

        public void Configure(Vector2 boardSize, float thickness)
        {
            SetWallsEnabled(false);
            if (!IsPositiveFinite(boardSize.x) || !IsPositiveFinite(boardSize.y) || !IsPositiveFinite(thickness))
                throw new ArgumentOutOfRangeException(nameof(boardSize), "边框尺寸与厚度必须为正数。");
            size = boardSize;
            wallThickness = thickness;
            CreateOrUpdateWall("Top", new Vector2(size.x + wallThickness * 2f, wallThickness), new Vector2(0f, size.y * 0.5f + wallThickness * 0.5f));
            CreateOrUpdateWall("Bottom", new Vector2(size.x + wallThickness * 2f, wallThickness), new Vector2(0f, -size.y * 0.5f - wallThickness * 0.5f));
            CreateOrUpdateWall("Left", new Vector2(wallThickness, size.y), new Vector2(-size.x * 0.5f - wallThickness * 0.5f, 0f));
            CreateOrUpdateWall("Right", new Vector2(wallThickness, size.y), new Vector2(size.x * 0.5f + wallThickness * 0.5f, 0f));
            SetWallsEnabled(true);
        }

        public void SetWallsEnabled(bool value)
        {
            foreach (var collider in GetComponentsInChildren<BoxCollider2D>(true)) collider.enabled = value;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void CreateOrUpdateWall(string wallName, Vector2 wallSize, Vector2 localPosition)
        {
            var wall = transform.Find(wallName);
            if (wall == null)
            {
                wall = new GameObject(wallName).transform;
                wall.SetParent(transform, false);
            }
            wall.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
            wall.localRotation = Quaternion.identity;
            wall.localScale = Vector3.one;
            var collider = wall.GetComponent<BoxCollider2D>();
            if (collider == null) collider = wall.gameObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.offset = Vector2.zero;
            collider.size = wallSize;
        }
    }
}
