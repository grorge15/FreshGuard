using System;
using System.IO;
using Cysharp.Threading.Tasks;
using GameLogic;
using UnityEditor;
using UnityEngine;

namespace FreshGuard.Editor
{
    public static class ShopBoardLayoutVerification
    {
        [MenuItem("FreshGuard/Shop/Verify Board Layout Unchanged")]
        public static void Verify() { VerifyAsync().Forget(); }

        private static async UniTaskVoid VerifyAsync()
        {
            var passed = false;
            string details;
            try
            {
                if (!Application.isPlaying) throw new InvalidOperationException("需要 PlayMode。");
                var host = UnityEngine.Object.FindObjectOfType<BattleShopSceneController>();
                if (host == null || !host.IsReady) throw new InvalidOperationException("商店未就绪。");
                host.EndBattle();
                var camera = host.Placement.SceneCamera;
                var viewport = camera.rect;
                var zoom = camera.orthographicSize;
                var cameraPosition = camera.transform.position;
                var boards = UnityEngine.Object.FindObjectsOfType<PlacementBoardView>();
                var positions = Array.ConvertAll(boards, board => board.transform.position);
                var scales = Array.ConvertAll(boards, board => board.transform.lossyScale);
                var cellPositions = Array.ConvertAll(boards,
                    board => camera.WorldToScreenPoint(board.GetCellWorldPosition(new BoardCoordinate(0, 0))));
                await host.InitializeAsync();
                await UniTask.Yield();
                Check();
                host.EndBattle();
                Check();
                await host.InitializeAsync();
                Check();
                passed = true;
                details = "商店打开、关闭、重新打开均不改变相机视口、缩放、位置或棋盘位置、比例、屏幕格子坐标。";

                void Check()
                {
                    if (camera.rect != viewport || camera.orthographicSize != zoom || camera.transform.position != cameraPosition)
                        throw new InvalidOperationException("商店改变了战斗相机布局。");
                    for (var i = 0; i < boards.Length; i++)
                        if (boards[i].transform.position != positions[i] || boards[i].transform.lossyScale != scales[i] ||
                            Vector3.Distance(camera.WorldToScreenPoint(boards[i].GetCellWorldPosition(new BoardCoordinate(0, 0))), cellPositions[i]) > 0.01f)
                            throw new InvalidOperationException("商店改变了棋盘布局或屏幕大小。");
                }
            }
            catch (Exception e) { details = e.Message; Debug.LogError("[Shop Board Layout] " + e); }
            File.WriteAllText(Path.Combine(Application.dataPath, "../Library/ShopBoardLayoutVerification.json"),
                JsonUtility.ToJson(new Report { passed = passed, details = details }, true));
            if (passed) Debug.Log("[Shop Board Layout] PASS " + details);
        }

        [Serializable]
        private sealed class Report { public bool passed; public string details; }
    }
}
