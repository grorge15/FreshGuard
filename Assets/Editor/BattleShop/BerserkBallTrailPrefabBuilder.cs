using System;
using GameLogic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FreshGuard.Editor
{
    /// <summary>为共用能源球预制体装配 Unity 原生粒子拖尾。</summary>
    public static class BerserkBallTrailPrefabBuilder
    {
        private const string BallPath = "Assets/AssetRaw/Physics/Prefabs/PhysicalCollisionBall.prefab";
        private const string MaterialsFolder = "Assets/AssetRaw/Physics/Materials";
        private const string MaterialPath = MaterialsFolder + "/BerserkBallTrail.mat";

        [MenuItem("FreshGuard/Effects/Build Berserk Ball Trail")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请退出运行并等待编译完成后装配暴走拖尾。");
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder("Assets/AssetRaw/Physics", "Materials");
            var root = PrefabUtility.LoadPrefabContents(BallPath);
            try
            {
                var node = root.transform.Find("BerserkTrail");
                if (node == null)
                {
                    node = new GameObject("BerserkTrail", typeof(ParticleSystem)).transform;
                    node.SetParent(root.transform, false);
                }
                node.gameObject.layer = root.layer;
                node.localPosition = Vector3.zero;
                node.localRotation = Quaternion.identity;
                node.localScale = Vector3.one;
                var particles = node.GetComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.duration = 1f;
                main.loop = true;
                main.playOnAwake = false;
                main.prewarm = false;
                main.startLifetime = 100000f;
                main.startSpeed = 0f;
                main.startSize = 0.26f;
                main.startColor = Color.white;
                main.gravityModifier = 0f;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = 1;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.stopAction = ParticleSystemStopAction.None;
                var emission = particles.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var shape = particles.shape;
                shape.enabled = false;
                var size = particles.sizeOverLifetime;
                size.enabled = false;
                var gradient = new Gradient();
                gradient.SetKeys(new[]
                {
                    new GradientColorKey(new Color(1f, 0.94f, 0.38f), 0f),
                    new GradientColorKey(new Color(1f, 0.48f, 0.04f), 0.35f),
                    new GradientColorKey(new Color(1f, 0.12f, 0.02f), 1f)
                }, new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
                var color = particles.colorOverLifetime;
                color.enabled = false;
                // 一个粒子固定在球心，用世界空间 Trail 记录运动轨迹，生成连续光带。
                var trails = particles.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.PerParticle;
                trails.ratio = 1f;
                // Trails 的寿命是粒子总寿命的倍率，换算为约 0.2 秒，避免长寿命球心留下整局轨迹。
                trails.lifetime = 0.2f / main.startLifetime.constant;
                trails.attachRibbonsToTransform = false;
                trails.splitSubEmitterRibbons = false;
                trails.worldSpace = true;
                trails.dieWithParticles = true;
                trails.minVertexDistance = 0.01f;
                trails.sizeAffectsWidth = true;
                trails.sizeAffectsLifetime = false;
                trails.inheritParticleColor = false;
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                trails.colorOverLifetime = Color.white;
                trails.colorOverTrail = new ParticleSystem.MinMaxGradient(gradient);
                trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (shader == null)
                    throw new InvalidOperationException("缺少 Unity 内置透明混合粒子着色器。");
                if (material == null)
                {
                    var defaultMaterial = renderer.sharedMaterial != null ? renderer.sharedMaterial :
                        AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
                    if (defaultMaterial == null)
                        throw new InvalidOperationException("缺少 Unity 内置粒子材质。");
                    material = new Material(defaultMaterial) { name = "BerserkBallTrail", shader = shader };
                    AssetDatabase.CreateAsset(material, MaterialPath);
                }
                material.shader = shader;
                // 透明混合避免多颗球的拖尾叠加后在浅色棋盘上过曝发白。
                material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
                // 固定采样内置粒子贴图的中间列，仅保留横截面的柔边，纵向不会形成亮点间隔。
                material.mainTextureScale = new Vector2(0f, 1f);
                material.mainTextureOffset = new Vector2(0.5f, 0f);
                EditorUtility.SetDirty(material);
                renderer.sharedMaterial = material;
                renderer.trailMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.None;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var ballRenderer = root.GetComponent<SpriteRenderer>();
                renderer.sortingLayerID = ballRenderer.sortingLayerID;
                renderer.sortingOrder = ballRenderer.sortingOrder - 1;
                var serialized = new SerializedObject(root.GetComponent<PhysicalCollisionBall>());
                serialized.FindProperty("_berserkTrail").objectReferenceValue = particles;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BallPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
