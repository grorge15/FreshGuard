using System.Reflection;
using NUnit.Framework;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Tests
{
    public sealed class BattleInfoViewTests
    {
        private const string PREFAB_PATH = "Assets/AssetRaw/UI/BattleInfo/BattleInfoUI.prefab";
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

        [TestCase(-0.5f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(0.75f, 0.75f)]
        [TestCase(1f, 1f)]
        [TestCase(1.5f, 1f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(float.PositiveInfinity, 1f)]
        [TestCase(float.NegativeInfinity, 0f)]
        public void LayoutUsesCenterReferenceSizeAndClampedScaleWithLegacyPanelFallback(float scale, float expected)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
            try
            {
                var view = root.GetComponent<BattleInfoView>();
                var panel = root.transform.Find("m_rect_BattleInfo").GetComponent<RectTransform>();
                var rootRect = root.GetComponent<RectTransform>();
                var rootSize = rootRect.sizeDelta;
                // 模拟旧 Prefab：没有序列化 _panel，仍是顶部拉伸布局。
                typeof(BattleInfoView).GetField("_panel", PRIVATE_INSTANCE).SetValue(view, null);
                panel.anchorMin = new Vector2(0, 1);
                panel.anchorMax = Vector2.one;
                panel.pivot = Vector2.one;
                view.SetLayoutPosition(new Vector2(35, -280), scale);
                Assert.That(panel.anchorMin, Is.EqualTo(Vector2.one * 0.5f));
                Assert.That(panel.anchorMax, Is.EqualTo(Vector2.one * 0.5f));
                Assert.That(panel.pivot, Is.EqualTo(Vector2.one * 0.5f));
                Assert.That(panel.sizeDelta, Is.EqualTo(new Vector2(1040, 156)));
                Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(35, -280)));
                Assert.That(panel.localScale, Is.EqualTo(Vector3.one * expected));
                Assert.That(rootRect.sizeDelta, Is.EqualTo(rootSize), "只移动和缩放信息栏，不改全屏窗口。");
                view.SetLayoutPosition(new Vector2(-12, 80), 0.5f);
                Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(-12, 80)));
                Assert.That(panel.localScale, Is.EqualTo(Vector3.one * 0.5f), "重复设置不能累计缩放。");
                Assert.That(root.GetComponent<CanvasGroup>().interactable, Is.False);
                Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                    Assert.That(graphic.raycastTarget, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void UpdatedPrefabHasCenteredBoundPanelAndZeroElapsedPreview()
        {
            // 主执行者先在 Editor 调用 UpdateExistingLayout，再运行此资源验收。
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null);
            var panel = prefab.transform.Find("m_rect_BattleInfo").GetComponent<RectTransform>();
            Assert.That(panel.anchorMin, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(panel.anchorMax, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(panel.pivot, Is.EqualTo(Vector2.one * 0.5f));
            Assert.That(panel.sizeDelta, Is.EqualTo(new Vector2(1040, 156)));
            Assert.That(panel.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(panel.localScale, Is.EqualTo(Vector3.one));
            var refs = new SerializedObject(prefab.GetComponent<BattleInfoView>());
            Assert.That(refs.FindProperty("_panel").objectReferenceValue, Is.EqualTo(panel));
            Assert.That(Label(prefab, "m_rect_Battle/m_text_ElapsedTime"), Is.EqualTo("00:00"));
            Assert.That(prefab.GetComponent<CanvasGroup>().interactable, Is.False);
            Assert.That(prefab.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            foreach (var graphic in prefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False);
        }

        [Test]
        public void WindowsOnlyConsumeTheirBoundBattleEventsAndCanRebind()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            var firstRoot = Object.Instantiate(prefab);
            var secondRoot = Object.Instantiate(prefab);
            var first = CreateWindow(firstRoot);
            var second = CreateWindow(secondRoot);
            try
            {
                Refresh(first, new BattleProgressSnapshot(101, 2, 90.9d));
                Refresh(second, new BattleProgressSnapshot(202, 4, 120d));
                Assert.That(first.BattleId, Is.EqualTo(101));
                AssertProgress(firstRoot, 2, "01:30");
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(101, 3, 95.9d));
                AssertProgress(firstRoot, 3, "01:35");
                AssertProgress(secondRoot, 4, "02:00");
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(202, 5, 125d));
                AssertProgress(firstRoot, 3, "01:35");
                AssertProgress(secondRoot, 5, "02:05");

                Refresh(first, new BattleProgressSnapshot(303, 1, 0d));
                Assert.That(first.BattleId, Is.EqualTo(303));
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(101, 9, 999d));
                AssertProgress(firstRoot, 1, "00:00");
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(303, 2, 10d));
                AssertProgress(firstRoot, 2, "00:10");
            }
            finally
            {
                DisposeWindow(first);
                DisposeWindow(second);
                Object.DestroyImmediate(firstRoot);
                Object.DestroyImmediate(secondRoot);
            }
        }

        [Test]
        public void MissingUserDataAndDestroyClearOwnershipAndIgnoreStaleEvents()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
            var window = CreateWindow(root);
            try
            {
                Refresh(window, new BattleProgressSnapshot(101, 2, 90d));
                Refresh(window, null);
                Assert.That(window.BattleId, Is.Zero);
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(101, 8, 500d));
                GameEvent.Send(BattleProgressEvents.Changed, new BattleProgressSnapshot(0, 9, 600d));
                AssertProgress(root, 1, "00:00");
                Refresh(window, new BattleProgressSnapshot(202, 3, 100d));
                InvokeWindow(window, "OnDestroy");
                Assert.That(window.BattleId, Is.Zero);
                Assert.That(window.UserData, Is.Null);
                Assert.That(window.View, Is.Null);
                // 直接测试销毁回调，仍存活的监听也不应再操作已关闭窗口。
                Assert.DoesNotThrow(() => GameEvent.Send(BattleProgressEvents.Changed,
                    new BattleProgressSnapshot(202, 10, 999d)));
                AssertProgress(root, 3, "01:40");
            }
            finally
            {
                DisposeWindow(window);
                Object.DestroyImmediate(root);
            }
        }

        private static BattleInfoUI CreateWindow(GameObject root)
        {
            var window = new BattleInfoUI();
            typeof(UIWidget).GetProperty("gameObject").SetValue(window, root);
            typeof(UIWidget).GetProperty("transform").SetValue(window, root.transform);
            typeof(UIWidget).GetProperty("rectTransform").SetValue(window, root.GetComponent<RectTransform>());
            InvokeWindow(window, "ScriptGenerator");
            InvokeWindow(window, "RegisterEvent");
            return window;
        }
        private static void Refresh(BattleInfoUI window, object snapshot)
        {
            typeof(UIBase).GetField("_userDatas", PRIVATE_INSTANCE).SetValue(window,
                snapshot == null ? null : new[] { snapshot });
            InvokeWindow(window, "OnRefresh");
        }
        private static void InvokeWindow(BattleInfoUI window, string method)
        { typeof(BattleInfoUI).GetMethod(method, PRIVATE_INSTANCE).Invoke(window, null); }
        private static void DisposeWindow(BattleInfoUI window)
        {
            // 与框架关闭顺序一致，只清理本窗口的监听，不清空全局事件。
            typeof(UIBase).GetMethod("RemoveAllUIEvent", PRIVATE_INSTANCE).Invoke(window, null);
            InvokeWindow(window, "OnDestroy");
        }
        private static void AssertProgress(GameObject root, int wave, string elapsed)
        {
            Assert.That(Label(root, "m_rect_Battle/m_text_Wave"), Is.EqualTo("波次 " + wave));
            Assert.That(Label(root, "m_rect_Battle/m_text_ElapsedTime"), Is.EqualTo(elapsed));
        }

        [Test]
        public void PrefabRendersBothSidesAndKeepsWholeBattleTimeAcrossWaves()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/BattleInfo/BattleInfoUI.prefab");
            Assert.That(prefab, Is.Not.Null);
            var root = Object.Instantiate(prefab);
            var texture = new Texture2D(4, 4);
            var avatar = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            try
            {
                var view = root.GetComponent<BattleInfoView>();
                view.SetPlayerInfo("玩家测试", 1234, avatar);
                view.SetEnemyInfo("敌方测试", 5678);
                view.SetBattleProgress(1, 90f);
                Assert.That(Label(root, "m_rect_Player/m_img_InfoBackground/m_text_Name"), Is.EqualTo("玩家测试"));
                Assert.That(Label(root, "m_rect_Player/m_img_InfoBackground/m_text_Trophies"), Is.EqualTo("1234"));
                Assert.That(Label(root, "m_rect_Enemy/m_img_InfoBackground/m_text_Name"), Is.EqualTo("敌方测试"));
                Assert.That(Label(root, "m_rect_Enemy/m_img_InfoBackground/m_text_Trophies"), Is.EqualTo("5678"));
                Assert.That(Label(root, "m_rect_Battle/m_text_ElapsedTime"), Is.EqualTo("01:30"));
                view.SetBattleProgress(2, 95.9f);
                Assert.That(Label(root, "m_rect_Battle/m_text_Wave"), Is.EqualTo("波次 2"));
                Assert.That(Label(root, "m_rect_Battle/m_text_ElapsedTime"), Is.EqualTo("01:35"));
                var playerAvatar = root.transform.Find("m_rect_BattleInfo/m_rect_Player/m_rect_Avatar/m_rect_AvatarMask/m_img_Avatar")
                    .GetComponent<Image>();
                Assert.That(playerAvatar.sprite, Is.SameAs(avatar));
                Assert.That(playerAvatar.enabled, Is.True);
                view.SetPlayerInfo("新玩家", 0);
                Assert.That(playerAvatar.sprite, Is.Null);
                Assert.That(playerAvatar.enabled, Is.False);
                Assert.That(root.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.That(graphic.raycastTarget, Is.False);
                    Assert.That(graphic.GetComponent<CanvasRenderer>(), Is.Not.Null);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(texture);
            }
        }
        private static string Label(GameObject root, string path)
        { return root.transform.Find("m_rect_BattleInfo/" + path).GetComponent<Text>().text; }
    }
}
