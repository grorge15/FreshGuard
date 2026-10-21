using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.Tests
{
    public sealed class BuffSelectPrefabTests
    {
        private const string FOLDER = "Assets/AssetRaw/UI/BuffSelect/";
        private sealed class WidgetOwner : UIBase
        {
            public WidgetOwner(GameObject root)
            { gameObject = root; transform = root.transform; rectTransform = root.GetComponent<RectTransform>(); }
        }

        [Test]
        public void WindowBindsCandidatesKeepsSessionWhenCoveredAndResetsOnRefresh()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance;
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "BuffSelectUI.prefab"));
            var window = new BuffSelectUI();
            typeof(UIWindow).GetField("_panel", flags).SetValue(window, instance);
            typeof(UIWindow).GetField("_canvas", flags).SetValue(window, instance.GetComponent<Canvas>());
            var options = new[] {
                new BuffSelectOption(101, 7, BuffQuality.Blue, "测试一", "描述一"),
                new BuffSelectOption(102, 1, BuffQuality.Gold, "测试二", "描述二"),
                new BuffSelectOption(103, 2, BuffQuality.Purple, "测试三", "描述三")
            };
            try
            {
                typeof(BuffSelectUI).GetMethod("ScriptGenerator", flags).Invoke(window, null);
                typeof(BuffSelectUI).GetMethod("OnCreate", flags).Invoke(window, null);
                typeof(UIBase).GetField("_userDatas", flags).SetValue(window,
                    new object[] { new BuffSelectRequest(options, null) });
                typeof(BuffSelectUI).GetMethod("OnRefresh", flags).Invoke(window, null);
                var card = instance.transform.Find("m_rect_Content/m_rect_Cards/m_item_Card0");
                Assert.That(card.Find("m_text_Level").GetComponent<Text>().text, Is.EqualTo("等级7"));
                Assert.That(card.GetComponent<Image>().sprite.name, Is.EqualTo("Rogue_blueBG"));
                var old = (BuffSelectSession)typeof(BuffSelectUI).GetField("_session", flags).GetValue(window);
                old.Advance(1f, out _);
                typeof(BuffSelectUI).GetMethod("OnSetVisible", flags).Invoke(window, new object[] { false });
                typeof(BuffSelectUI).GetMethod("OnSetVisible", flags).Invoke(window, new object[] { true });
                Assert.That(typeof(BuffSelectUI).GetField("_session", flags).GetValue(window), Is.SameAs(old));
                Assert.That(old.RemainingSeconds, Is.EqualTo(4f));
                Assert.That(old.IsActive, Is.True);
                typeof(BuffSelectUI).GetMethod("OnRefresh", flags).Invoke(window, null);
                Assert.That(old.IsActive, Is.False);
                var session = (BuffSelectSession)typeof(BuffSelectUI).GetField("_session", flags).GetValue(window);
                Assert.That(session.RemainingSeconds, Is.EqualTo(5f));
                Assert.That(card.GetComponent<Button>().interactable, Is.True);
                typeof(BuffSelectUI).GetMethod("OnDestroy", flags).Invoke(window, null);
                Assert.That(session.IsActive, Is.False);
            }
            finally
            {
                var items = (BuffSelectItemWidget[])typeof(BuffSelectUI).GetField("_items", flags).GetValue(window);
                if (items != null) foreach (var item in items)
                    typeof(BuffSelectItemWidget).GetMethod("OnDestroy", flags).Invoke(item, null);
                typeof(BuffSelectUI).GetMethod("OnDestroy", flags).Invoke(window, null);
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WindowHasThreeNestedCardsAndAllQualityBackgrounds()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "BuffSelectUI.prefab");
            Assert.That(prefab, Is.Not.Null);
            var cards = prefab.transform.Find("m_rect_Content/m_rect_Cards");
            Assert.That(cards.childCount, Is.EqualTo(3));
            for (var i = 0; i < 3; i++)
            {
                var card = cards.Find("m_item_Card" + i).gameObject;
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(card),
                    Is.EqualTo(FOLDER + "BuffSelectItem.prefab"));
                Assert.That(card.GetComponent<Button>(), Is.Not.Null);
                var style = card.GetComponent<BuffSelectItemStyle>();
                Assert.That(style.BackgroundFor(BuffQuality.Blue).name, Is.EqualTo("Rogue_blueBG"));
                Assert.That(style.BackgroundFor(BuffQuality.Purple).name, Is.EqualTo("Rogue_purpleBG"));
                Assert.That(style.BackgroundFor(BuffQuality.Gold).name, Is.EqualTo("Rogue_goldBG"));
            }
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<Image>().raycastTarget, Is.True);
            foreach (var text in prefab.GetComponentsInChildren<Text>(true))
            {
                Assert.That(text.font, Is.Not.Null);
                Assert.That(text.font.material, Is.Not.Null, "字体重载后仍须有可渲染材质。");
                Assert.That(text.font.HasCharacter('强'), Is.True);
            }
        }

        [Test]
        public void RealCardShowsIndependentLevelAndQualityAndCanBeRebound()
        {
            var parent = new GameObject("BuffSelectTestOwner", typeof(RectTransform));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FOLDER + "BuffSelectItem.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab, parent.transform);
            var widget = new BuffSelectItemWidget();
            var texture = new Texture2D(8, 8);
            var icon = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * 0.5f);
            try
            {
                Assert.That(widget.Create(new WidgetOwner(parent), instance), Is.True);
                var selected = -1;
                var count = 0;
                widget.Render(new BuffSelectOption(101, 7, BuffQuality.Blue, "测试名称", "测试介绍", icon),
                    2, index => { selected = index; count++; });
                Assert.That(instance.GetComponent<Image>().sprite.name, Is.EqualTo("Rogue_blueBG"));
                Assert.That(instance.transform.Find("m_text_Level").GetComponent<Text>().text, Is.EqualTo("等级7"));
                Assert.That(instance.transform.Find("m_text_Name").GetComponent<Text>().text, Is.EqualTo("测试名称"));
                Assert.That(instance.transform.Find("m_text_Description").GetComponent<Text>().text, Is.EqualTo("测试介绍"));
                Assert.That(instance.transform.Find("m_img_Icon").GetComponent<Image>().sprite, Is.SameAs(icon));
                Assert.That(instance.transform.Find("m_img_Icon").GetComponent<Image>().enabled, Is.True);
                Assert.That(instance.transform.Find("m_text_IconPlaceholder").GetComponent<Text>().enabled, Is.False);
                instance.GetComponent<Button>().onClick.Invoke();
                Assert.That(selected, Is.EqualTo(2));
                widget.StopSelection();
                instance.GetComponent<Button>().onClick.Invoke();
                Assert.That(count, Is.EqualTo(1));
                widget.Render(new BuffSelectOption(102, 1, BuffQuality.Gold, "新候选", ""),
                    0, index => selected = index);
                instance.GetComponent<Button>().onClick.Invoke();
                Assert.That(selected, Is.Zero);
                Assert.That(instance.GetComponent<Image>().sprite.name, Is.EqualTo("Rogue_goldBG"));
                Assert.That(instance.transform.Find("m_img_Icon").GetComponent<Image>().sprite, Is.Null);
                Assert.That(instance.transform.Find("m_img_Icon").GetComponent<Image>().enabled, Is.False);
                Assert.That(instance.transform.Find("m_text_IconPlaceholder").GetComponent<Text>().enabled, Is.True);
            }
            finally
            {
                typeof(BuffSelectItemWidget).GetMethod("OnDestroy", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).Invoke(widget, null);
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(icon);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
