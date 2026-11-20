using UnityEngine;

namespace GameLogic
{
    /// <summary>槽体常显；闪电先冲至满槽，再跟随暴走余时。</summary>
    public partial class BerserkBarWidget
    {
        private const float IgnitionDuration = 0.16f;
        private bool _active;
        private float _initialRemaining;
        private BerserkStripeGraphic _stripes;
        private static readonly Color FullColor = new Color32(255, 219, 46, 255);
        private static readonly Color LowColor = new Color32(244, 48, 37, 255);

        public void Render(float remaining, float duration)
        {
            var progress = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            if (_stripes == null) _stripes = m_img_Fill.GetComponentInChildren<BerserkStripeGraphic>(true);
            bool active = progress > 0f;
            if (active && !_active) _initialRemaining = remaining;
            _active = active;
            m_rect_EnergyIcon.gameObject.SetActive(active);
            if (!active)
            {
                m_img_Fill.fillAmount = 0f;
                _stripes.Present(0f, 0f, 0f, 0f);
                return;
            }

            // 使用战斗余时驱动起步，重复刷新和暂停不会额外推进动画。
            float elapsed = Mathf.Max(0f, _initialRemaining - remaining);
            float ignition = Mathf.Clamp01(elapsed / IgnitionDuration);
            m_img_Fill.fillAmount = ignition < 1f ? 1f : progress;
            m_img_Fill.color = Color.Lerp(LowColor, FullColor, m_img_Fill.fillAmount);
            float bolt = ignition < 1f ? ignition : progress;
            _stripes.Present(m_img_Fill.fillAmount, bolt, progress, elapsed);
            var fill = m_img_Fill.rectTransform;
            var edge = new Vector3(Mathf.Lerp(fill.rect.xMin, fill.rect.xMax, bolt), fill.rect.center.y, 0f);
            m_rect_EnergyIcon.localPosition = m_rect_EnergyIcon.parent.InverseTransformPoint(fill.TransformPoint(edge));
        }
    }
}
