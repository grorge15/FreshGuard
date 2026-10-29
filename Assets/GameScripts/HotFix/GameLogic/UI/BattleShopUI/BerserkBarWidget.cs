using UnityEngine;

namespace GameLogic
{
    /// <summary>只展示真实剩余比例，不自行计时；布局与图片保存在商店 Prefab。</summary>
    public partial class BerserkBarWidget
    {
        public void Render(float remaining, float duration)
        {
            var progress = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            m_img_Fill.fillAmount = progress;
            // 从填充区域的左端走到右端，随 Prefab 尺寸调整，不写死像素宽度。
            var fill = m_img_Fill.rectTransform;
            var edge = new Vector3(Mathf.Lerp(fill.rect.xMin, fill.rect.xMax, progress), fill.rect.center.y, 0f);
            m_rect_EnergyIcon.localPosition = m_rect_EnergyIcon.parent.InverseTransformPoint(fill.TransformPoint(edge));
            var visible = progress > 0f;
            if (Visible != visible) Visible = visible;
        }
    }
}
