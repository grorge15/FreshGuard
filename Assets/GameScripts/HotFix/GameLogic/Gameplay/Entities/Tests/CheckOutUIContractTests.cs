using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YooAsset.Editor;

namespace GameLogic.Tests
{
    public sealed class CheckOutUIContractTests
    {
        [Test]
        public void WindowPrefabHasBothThemesAndButtonsDisabledUntilBound()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AssetRaw/UI/CheckOutUI.prefab");
            Assert.IsNotNull(prefab, "结算窗口必须有可加载的 Prefab");
            Assert.IsNotNull(prefab.GetComponent<Canvas>());
            Assert.IsNotNull(prefab.GetComponent<GraphicRaycaster>());
            foreach (var path in new[] { "m_go_Victory", "m_go_Defeat", "m_rect_Content/m_rect_Player", "m_rect_Content/m_rect_Opponent", "m_rect_Content/m_rect_Rewards" })
                Assert.IsNotNull(prefab.transform.Find(path), path);
            foreach (var name in new[] { "m_btn_DoubleReward", "m_btn_ReturnMain" })
            {
                var button = prefab.transform.Find("m_rect_Content/" + name).GetComponent<Button>();
                Assert.IsFalse(button.interactable, "窗口绑定本局奖励前，按钮默认不可操作");
                Assert.AreEqual(0, button.onClick.GetPersistentEventCount());
            }
            Assert.IsTrue(prefab.transform.Find("m_img_InputBlocker").GetComponent<Image>().raycastTarget);
            var settings = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Editor/AssetBundleCollector/AssetBundleCollectorSetting.asset");
            Assert.AreEqual("CheckOutUI", settings.BeginCollect("DefaultPackage", true, false).CollectAssets
                .Single(asset => asset.AssetInfo.AssetPath == AssetDatabase.GetAssetPath(prefab)).Address);
        }
    }
}
