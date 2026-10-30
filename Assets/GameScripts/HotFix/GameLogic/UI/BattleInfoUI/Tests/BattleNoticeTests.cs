using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameLogic.Tests
{
    public sealed class BattleNoticeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private BattleNoticeWidget _widget;
        private RectTransform _panel;
        private CanvasGroup _group;
        private Text _label;

        [SetUp]
        public void SetUp()
        {
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/AssetRaw/UI/MainCombat/MainCombatUI.prefab"));
            var layer = (RectTransform)_root.transform.Find("BattleNotices");
            layer.anchorMin = layer.anchorMax = Vector2.one * 0.5f;
            layer.sizeDelta = new Vector2(1080, 1920);
            _panel = (RectTransform)layer.Find("Notice");
            _group = _panel.GetComponent<CanvasGroup>();
            _label = _panel.GetComponentInChildren<Text>(true);
            _widget = new BattleNoticeWidget();
            typeof(UIWidget).GetProperty("gameObject").SetValue(_widget, layer.gameObject);
            typeof(UIWidget).GetProperty("transform").SetValue(_widget, layer);
            typeof(UIWidget).GetProperty("rectTransform").SetValue(_widget, layer);
            typeof(BattleNoticeWidget).GetMethod("ScriptGenerator", Private).Invoke(_widget, null);
            typeof(BattleNoticeWidget).GetMethod("OnCreate", Private).Invoke(_widget, null);
        }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(_root); }

        [Test]
        public void UsesProvidedBackgroundBoldWhiteCurrentFontAndNeverBlocksInput()
        {
            _widget.Show("能源币不足");
            Assert.AreEqual("Assets/AssetRaw/Actor/UISprites/General/U_blackBarDarker.png",
                AssetDatabase.GetAssetPath(_panel.GetComponent<Image>().sprite));
            Assert.AreEqual("Assets/AssetRaw/Fonts/MiSans-UI-Semibold.ttf", AssetDatabase.GetAssetPath(_label.font));
            Assert.AreEqual(FontStyle.Bold, _label.fontStyle);
            Assert.AreEqual(Color.white, _label.color);
            Assert.IsFalse(_label.supportRichText);
            foreach (var graphic in _widget.gameObject.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget);
            foreach (var group in _widget.gameObject.GetComponentsInChildren<CanvasGroup>(true))
                Assert.IsFalse(group.blocksRaycasts);
        }

        [Test]
        public void HoldsForExactlyHalfASecondThenRisesAndFadesLinearly()
        {
            _widget.Show("能源币不足");
            Assert.AreEqual(Vector2.zero, _panel.anchoredPosition);
            _widget.Advance(0.5f);
            Assert.AreEqual(Vector2.zero, _panel.anchoredPosition);
            Assert.AreEqual(1f, _group.alpha);
            _widget.Advance(BattleNoticeWidget.FadeDuration * 0.25f);
            Assert.AreEqual(BattleNoticeWidget.RiseDistance * 0.25f, _panel.anchoredPosition.y, 0.001f);
            Assert.AreEqual(0.75f, _group.alpha, 0.001f);
            _widget.Advance(BattleNoticeWidget.FadeDuration * 0.25f);
            Assert.AreEqual(BattleNoticeWidget.RiseDistance * 0.5f, _panel.anchoredPosition.y, 0.001f);
            Assert.AreEqual(0.5f, _group.alpha, 0.001f);
            _widget.Advance(BattleNoticeWidget.FadeDuration * 0.5f);
            Assert.IsFalse(_panel.gameObject.activeSelf);
        }

        [Test]
        public void ConsecutiveNoticesQueueWithoutOverlappingAndDuplicatesDoNotRestartHold()
        {
            _widget.Show("first");
            _widget.Advance(0.4f);
            _widget.Show("first");
            _widget.Show("second");
            _widget.Show("second");
            _widget.Advance(0.2f);
            Assert.Less(_group.alpha, 1f);
            Assert.AreEqual("first", _label.text);
            _widget.Advance(0.6f);
            Assert.AreEqual("second", _label.text);
            Assert.AreEqual(1f, _group.alpha);
            Assert.AreEqual(Vector2.zero, _panel.anchoredPosition);
            _widget.Advance(1.15f);
            Assert.IsFalse(_panel.gameObject.activeSelf);
        }

        [Test]
        public void QueueKeepsLatestThreeMessagesAndLargeFrameStepAdvancesAcrossMessages()
        {
            _widget.Show("current");
            _widget.Show("discarded");
            _widget.Show("second");
            _widget.Show("third");
            _widget.Show("fourth");
            _widget.Advance(1.2f);
            Assert.AreEqual("second", _label.text);
            _widget.Advance(3.5f);
            Assert.IsFalse(_panel.gameObject.activeSelf);
        }

        [Test]
        public void HideClearsCurrentAndPendingNotices()
        {
            _widget.Show("first"); _widget.Show("stale");
            _widget.Visible = false;
            _widget.Visible = true;
            _widget.Advance(3f);
            Assert.IsFalse(_panel.gameObject.activeSelf);
            _widget.Show("new battle");
            Assert.AreEqual("new battle", _label.text);
            Assert.AreEqual(1f, _group.alpha);
        }

        [Test]
        public void LongMessagesStayInsideScreenAndWrapWithPadding()
        {
            _widget.Show("刷新会清除店内所有机器人，已购机器人不退款。请确认当前机器人已经放置。");
            Assert.LessOrEqual(_panel.rect.width, 984f);
            Assert.GreaterOrEqual(_panel.rect.height, _label.preferredHeight + 48f - 0.01f);
            Assert.AreEqual(HorizontalWrapMode.Wrap, _label.horizontalOverflow);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void InvalidTimeIsRejected(float deltaTime)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _widget.Advance(deltaTime));
        }
    }
}
