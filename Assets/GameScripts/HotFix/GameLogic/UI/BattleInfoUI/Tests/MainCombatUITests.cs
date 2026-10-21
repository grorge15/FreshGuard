using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Tests
{
    public sealed class MainCombatUITests
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string PATH = "Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab";

        [Test]
        public void MainWindowKeepsEditedChildLayoutAcrossCreateRefreshProgressAndUpdate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PATH);
            Assert.IsNotNull(prefab);
            var savedRoot = prefab.GetComponent<RectTransform>();
            Assert.AreEqual(Vector2.zero, savedRoot.anchorMin);
            Assert.AreEqual(Vector2.one, savedRoot.anchorMax);
            Assert.AreEqual(Vector2.one * 0.5f, savedRoot.pivot);
            Assert.AreEqual(Vector2.zero, savedRoot.sizeDelta);
            Assert.AreEqual(Vector3.one, savedRoot.localScale);
            var root = Object.Instantiate(prefab);
            var window = new MainCombatUI();
            try
            {
                Assert.AreEqual(1, root.GetComponentsInChildren<Canvas>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<GraphicRaycaster>(true).Length);
                Assert.IsNull(root.GetComponent<Graphic>(), "全屏根不能用透明 Image 拦截盘面点击。");
                typeof(UIWindow).GetField("_panel", PRIVATE).SetValue(window, root);
                typeof(UIWindow).GetField("_canvas", PRIVATE).SetValue(window, root.GetComponent<Canvas>());
                var info = (RectTransform)root.transform.Find("BattleInfoUI");
                var shop = (RectTransform)root.transform.Find("BattleShopUI");
                info.anchoredPosition = new Vector2(27, 240);
                info.sizeDelta = new Vector2(900, 130);
                info.localScale = Vector3.one * 0.85f;
                shop.anchoredPosition = new Vector2(-19, 95);
                shop.sizeDelta = new Vector2(960, 230);
                shop.localScale = Vector3.one * 0.9f;
                typeof(MainCombatUI).GetMethod("OnCreate", PRIVATE).Invoke(window, null);
                typeof(UIBase).GetField("_userDatas", PRIVATE).SetValue(window,
                    new object[] { null, null, new BattleProgressSnapshot(17, 4, 125d) });
                typeof(MainCombatUI).GetMethod("OnRefresh", PRIVATE).Invoke(window, null);
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(17, 5, 130d));
                typeof(BattleShopUI).GetMethod("OnUpdate", PRIVATE).Invoke(window.Shop, null);
                Assert.AreSame(window, window.Info.OwnerWindow);
                Assert.AreSame(window, window.Shop.OwnerWindow);
                Assert.AreEqual("波次 5", info.Find("m_rect_BattleInfo/m_rect_Battle/m_text_Wave").GetComponent<Text>().text);
                Assert.AreEqual(new Vector2(27, 240), info.anchoredPosition);
                Assert.AreEqual(new Vector2(900, 130), info.sizeDelta);
                Assert.AreEqual(Vector3.one * 0.85f, info.localScale);
                Assert.AreEqual(new Vector2(-19, 95), shop.anchoredPosition);
                Assert.AreEqual(new Vector2(960, 230), shop.sizeDelta);
                Assert.AreEqual(Vector3.one * 0.9f, shop.localScale);
                Assert.IsFalse(info.GetComponent<CanvasGroup>().blocksRaycasts);
                Assert.IsTrue(shop.Find("m_rect_ShopPanel/m_btn_Refresh").GetComponent<Button>().interactable);
            }
            finally
            {
                DisposeChildren(window);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DragGhostFollowsScreenPointWithMovedResizedAndScaledShop()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PATH));
            try
            {
                var shop = (RectTransform)root.transform.Find("BattleShopUI");
                shop.anchoredPosition = new Vector2(65, 150);
                shop.sizeDelta = new Vector2(900, 260);
                shop.localScale = Vector3.one * 0.8f;
                Canvas.ForceUpdateCanvases();
                var ghostRect = (RectTransform)shop.Find("m_item_DragGhost");
                var ghost = new RobotDragGhostWidget();
                typeof(UIWidget).GetProperty("transform").SetValue(ghost, ghostRect);
                typeof(UIWidget).GetProperty("rectTransform").SetValue(ghost, ghostRect);
                var pointer = new Vector2(Screen.width * 0.4f, Screen.height * 0.7f);
                ghost.Move(pointer, null);
                Assert.That(Vector2.Distance(pointer, RectTransformUtility.WorldToScreenPoint(null, ghostRect.position)), Is.LessThan(0.1f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void DisposeChildren(UIBase parent)
        {
            var children = (IList)typeof(UIBase).GetField("ListChild", PRIVATE).GetValue(parent);
            foreach (UIWidget child in children)
            {
                DisposeChildren(child);
                typeof(UIBase).GetMethod("RemoveAllUIEvent", PRIVATE).Invoke(child, null);
                // EditMode 不允许 Object.Destroy；即时回收商店在 OnCreate 分配的运行时字体。
                if (child is BattleShopUI)
                {
                    var font = typeof(BattleShopUI).GetField("_font", PRIVATE);
                    Object.DestroyImmediate(font.GetValue(child) as Font);
                    font.SetValue(child, null);
                }
                child.GetType().GetMethod("OnDestroy", PRIVATE).Invoke(child, null);
            }
        }
    }
}
