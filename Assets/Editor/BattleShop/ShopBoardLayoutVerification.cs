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
                await host.InitializeAsync();
                await UniTask.Yield();
                RequireReady();
                var adaptedPosition = camera.transform.position;
                var adaptedZoom = camera.orthographicSize;
                CheckWorld();
                host.EndBattle();
                if (camera.rect != viewport || camera.orthographicSize != zoom || camera.transform.position != cameraPosition)
                    throw new InvalidOperationException("退场未恢复相机。");
                CheckWorld();
                await host.InitializeAsync();
                RequireReady();
                if (camera.transform.position != adaptedPosition || camera.orthographicSize != adaptedZoom)
                    throw new InvalidOperationException("相同屏幕和战场重复初始化的相机结果不一致。");
                CheckWorld();
                passed = true;
                details = "相同输入重复开局相机布局一致，棋盘世界位置和比例保持，退场恢复原始相机。";

                void RequireReady()
                {
                    if (!host.IsReady || !host.GetComponent<BattleScreenLayoutController>().IsBound)
                        throw new InvalidOperationException("屏幕适配未就绪。");
                }

                void CheckWorld()
                {
                    for (var i = 0; i < boards.Length; i++)
                        if (boards[i].transform.position != positions[i] || boards[i].transform.lossyScale != scales[i])
                            throw new InvalidOperationException("屏幕适配改变了棋盘世界布局。");
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
