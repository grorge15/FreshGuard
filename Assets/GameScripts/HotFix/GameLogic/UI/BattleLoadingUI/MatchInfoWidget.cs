using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
namespace GameLogic
{
    public sealed class MatchInfoWidget : UIWidget
    {
        private Image _avatar;
        private Text _name;
        private Text _trophies;
        private Transform _buffRoot;
        private GameObject _template;
        private readonly List<MatchBuffWidget> _buffs=new List<MatchBuffWidget>();
        protected override void ScriptGenerator()
        {
            _avatar=FindChildComponent<Image>("m_img_Avatar");
            _name=FindChildComponent<Text>("m_text_Name");
            _trophies=FindChildComponent<Text>("m_text_Trophies");
            _buffRoot=transform.Find("m_rect_BuffSlots");
            _template=_buffRoot.Find("m_item_BuffTemplate").gameObject;
        }
        public void Render(string name,string avatar,int trophies,IReadOnlyList<MatchBuff> buffs)
        {
            _name.text=name; _trophies.text=trophies.ToString();
            _avatar.SetSprite(avatar,cancellationToken:gameObject.GetCancellationTokenOnDestroy());
            AdjustIconNum(_buffs,buffs.Count,_buffRoot,_template);
            _template.SetActive(false);
            for(var i=0;i<buffs.Count;i++) { _buffs[i].Visible=true; _buffs[i].Render(buffs[i]); }
        }
    }
    public sealed class MatchBuffWidget : UIWidget
    {
        private Image _icon;
        private Text _layers;
        protected override void ScriptGenerator()
        {
            _icon=FindChildComponent<Image>("m_img_Icon");
            _layers=FindChildComponent<Text>("m_text_Layers");
        }
        public void Render(MatchBuff buff)
        {
            _layers.text="×"+buff.Layers;
            _icon.SetSprite(buff.IconLocation,cancellationToken:gameObject.GetCancellationTokenOnDestroy());
        }
    }
}
