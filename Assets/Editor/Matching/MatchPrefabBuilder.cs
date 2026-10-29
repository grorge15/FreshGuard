using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.EditorTools
{
    public static class MatchPrefabBuilder
    {
        public const string PrefabPath="Assets/AssetRaw/UI/BattleLoadingUI.prefab";
        public const string ProfilePath="Assets/AssetRaw/Configs/DemoBattleProfile.asset";
        private const string Art="Assets/AssetRaw/Actor/UISprites/";
        private static Font _font;
        [MenuItem("FreshGuard/Matching/Build Matching Assets")]
        public static void Build()
        {
            if(EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("请先停止运行并等编译完成。");
            _font=FreshGuard.Editor.ModernUIFontTheme.LoadFont(false);
            if(_font==null) throw new InvalidOperationException("缺少中文字体。");
            if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)==null) throw new InvalidOperationException("缺少原BattleLoadingUI。");
            var root=PrefabUtility.LoadPrefabContents(PrefabPath);
            var staging=new GameObject("MatchingPrefabStaging",typeof(RectTransform),typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(staging,root.scene);
            root.transform.SetParent(staging.transform,false);
            try
            {
                // 已转换的资源保留编辑器手工调整；重复执行只验证，不重置布局。
                if(root.transform.Find("m_rect_Matching")==null)
                {
                    while(root.transform.childCount>0) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
                    var scaler=root.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution=new Vector2(1080,1920); scaler.matchWidthOrHeight=.5f;
                    var bg=Image(root.transform,"m_img_Background","MatchUI/Match_BG",Vector2.zero,new Vector2(1080,1920));
                    Fill(bg.rectTransform); bg.raycastTarget=true;
                    Image(root.transform,"m_img_Logo","Logo/游戏logo",new Vector2(0,640),new Vector2(820,547));
                    var matching=Rect(root.transform,"m_rect_Matching",Vector2.zero,new Vector2(1080,1920));
                    Text(matching,"m_text_Title","正在匹配对手...",new Vector2(0,365),new Vector2(950,100),64,Color.white,true);
                    Text(matching,"m_text_Timer","00:00",new Vector2(0,245),new Vector2(700,160),94,new Color(.87f,.98f,1),true);
                    Image(matching,"m_img_Robot","MatchUI/Match_robot",new Vector2(0,-175),new Vector2(900,725));
                    var bar=Image(matching,"m_img_TipBar","MatchUI/Match_tipBarBG",new Vector2(0,-630),new Vector2(740,147));
                    var tip=Text(bar.transform,"m_text_Tip","合理布置机器人，触发能源连锁反应，\n可以释放强力料理技能！",new Vector2(45,0),new Vector2(580,110),30,new Color(.19f,.22f,.30f));
                    tip.alignment=TextAnchor.MiddleLeft;
                    var cancel=Image(matching,"m_btn_Cancel","General/U_yellowButton",new Vector2(0,-815),new Vector2(470,140));
                    cancel.raycastTarget=true;
                    var button=cancel.gameObject.AddComponent<Button>(); button.targetGraphic=cancel;
                    var colors=button.colors; colors.pressedColor=new Color(.82f,.72f,.52f); colors.highlightedColor=Color.white; button.colors=colors;
                    Text(cancel.transform,"m_text_Label","取消匹配",Vector2.zero,new Vector2(420,110),46,new Color(.33f,.18f,.03f));
                    var success=Rect(root.transform,"m_rect_Success",Vector2.zero,new Vector2(1080,1920));
                    var banner=Image(success,"m_rect_Banner","MatchUI/Match_matchFoundBG",new Vector2(0,335),new Vector2(940,183));
                    Text(banner.transform,"m_text_Title","匹配成功！",new Vector2(0,30),new Vector2(700,70),48,Color.white,true);
                    Text(banner.transform,"m_text_Entering","即将进入战斗",new Vector2(-28,-36),new Vector2(350,55),34,Color.white);
                    for(var i=0;i<3;i++) Text(banner.transform,"m_text_Dot"+i,".",new Vector2(161+i*17,-36),new Vector2(20,55),34,Color.white);
                    Card(success,"m_item_Player",false,-340);
                    Card(success,"m_item_Opponent",true,340);
                    Image(success,"m_img_VS","General/U_winVS",new Vector2(0,-180),new Vector2(300,270));
                    var tipIcon=Image(success,"m_img_TipIcon","MatchUI/Match_tip",new Vector2(-410,-685),new Vector2(40,71));
                    var successTip=Text(success,"m_text_Tip","小贴士：合理布置机器人，触发<color=#FFE06B>能源连锁反应</color>，\n可以释放强力料理技能！",new Vector2(30,-685),new Vector2(790,150),30,new Color(.84f,.88f,1));
                    successTip.alignment=TextAnchor.MiddleLeft;
                    success.gameObject.SetActive(false);
                }
                // 字体行高高于字号，已有Prefab也同步修复裁切。
                var timer=root.transform.Find("m_rect_Matching/m_text_Timer").GetComponent<Text>();
                timer.rectTransform.sizeDelta=new Vector2(700,160);
                var robotRect=(RectTransform)root.transform.Find("m_rect_Matching/m_img_Robot");
                robotRect.anchoredPosition=new Vector2(0,-175);robotRect.sizeDelta=new Vector2(900,725);
                foreach(var label in root.GetComponentsInChildren<Text>(true))
                    if(label.name=="m_text_Layers")label.rectTransform.sizeDelta=new Vector2(48,40);
                // 独立预览Canvas会驱动根Rect为零，须作为临时Canvas的子节点保存。
                var rootRect=(RectTransform)root.transform;
                rootRect.pivot=Vector2.one*.5f;
                Fill(rootRect); rootRect.localScale=Vector3.one; rootRect.localRotation=Quaternion.identity;
                rootRect.anchoredPosition3D=Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            }
            finally { root.transform.SetParent(null,false); PrefabUtility.UnloadPrefabContents(root); }
            var profile=AssetDatabase.LoadAssetAtPath<DemoBattleProfile>(ProfilePath);
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<DemoBattleProfile>();
                foreach(var id in new[]{501,502,503,504,505,601,602,603}) profile.Robots.Add(new DemoRobotReference{RobotId=id,Unlocked=true,ReferenceLevel=5});
                AssetDatabase.CreateAsset(profile,ProfilePath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MatchingAssets] 已通过Editor保存匹配Prefab及Demo配置。");
        }
        private static void Card(Transform parent,string name,bool enemy,float x)
        {
            var card=Image(parent,name,enemy?"MatchUI/Match_enemyBG":"MatchUI/Match_playerBG",new Vector2(x,-170),new Vector2(320,612));
            Image(card.transform,"m_img_Avatar","MainMenu/Main_playerAvatar 1",new Vector2(0,160),new Vector2(234,234));
            Image(card.transform,"m_img_AvatarFrame","General/U_avatarFrame",new Vector2(0,160),new Vector2(250,250));
            var nameText=Text(card.transform,"m_text_Name",enemy?"美味料理家":"料理守护者",new Vector2(0,-15),new Vector2(268,70),33,Color.white,true);
            nameText.resizeTextForBestFit=true; nameText.resizeTextMinSize=20; nameText.resizeTextMaxSize=33;
            Image(card.transform,"m_img_Cup","General/U_cup",new Vector2(-78,-130),new Vector2(48,58));
            Text(card.transform,"m_text_Trophies","2500",new Vector2(35,-130),new Vector2(168,64),40,new Color(1,.88f,.31f),true);
            var slots=Rect(card.transform,"m_rect_BuffSlots",new Vector2(0,-225),new Vector2(266,94));
            var grid=slots.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize=new Vector2(64,72); grid.spacing=new Vector2(4,4); grid.childAlignment=TextAnchor.UpperCenter;
            var template=Rect(slots,"m_item_BuffTemplate",Vector2.zero,new Vector2(64,72));
            Image(template,"m_img_Icon",null,new Vector2(0,7),new Vector2(52,52));
            Text(template,"m_text_Layers","×1",new Vector2(11,-22),new Vector2(48,40),22,Color.white,true);
            template.gameObject.SetActive(false);
        }
        private static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform)); go.layer=5; go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
        }
        private static Image Image(Transform parent,string name,string art,Vector2 position,Vector2 size)
        {
            var rect=Rect(parent,name,position,size); var image=rect.gameObject.AddComponent<Image>();
            if(art!=null)
            {
                image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+art+".png");
                if(image.sprite==null)throw new InvalidOperationException("缺少Sprite："+art);
                image.preserveAspect=true;
            }
            image.raycastTarget=false; return image;
        }
        private static Text Text(Transform parent,string name,string text,Vector2 position,Vector2 size,int fontSize,Color color,bool outline=false)
        {
            var rect=Rect(parent,name,position,size); var label=rect.gameObject.AddComponent<Text>();
            label.font=_font; label.fontSize=fontSize; label.text=text; label.color=color; label.alignment=TextAnchor.MiddleCenter;
            label.raycastTarget=false; label.supportRichText=true; label.horizontalOverflow=HorizontalWrapMode.Wrap;
            label.verticalOverflow=VerticalWrapMode.Truncate;
            if(outline){var effect=rect.gameObject.AddComponent<Outline>();effect.effectColor=new Color(.1f,.3f,.58f,.8f);effect.effectDistance=new Vector2(.75f,-.75f);}
            FreshGuard.Editor.ModernUIFontTheme.Apply(label);
            return label;
        }
        private static void Fill(RectTransform rect) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    }
}
