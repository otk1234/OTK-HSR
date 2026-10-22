using System.Collections.Generic;
using UnityEngine;

namespace NarrativeGraybox.CameraSystem
{
    public sealed class OccludingWall : MonoBehaviour
    {
        [SerializeField] private Renderer wallRenderer;
        [SerializeField] private Renderer topRenderer;
        [SerializeField, Range(0f, 1f)] private float occludedAlpha = 0.08f;
        [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.28f;
        [SerializeField, Min(0.01f)] private float fadeInDuration = 0.38f;

        private readonly List<Material> fadeMaterials = new List<Material>();
        private readonly List<float> originalAlphas = new List<float>();
        private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
        private float currentVisibility = 1f;
        private float targetVisibility = 1f;
        private bool materialsPrepared;

        public void Configure(Renderer face, Renderer top)
        {
            wallRenderer = face;
            topRenderer = top;
        }

        private void Update()
        {
            if (Mathf.Approximately(currentVisibility, targetVisibility)) return;

            float duration = targetVisibility < currentVisibility ? fadeOutDuration : fadeInDuration;
            currentVisibility = Mathf.MoveTowards(currentVisibility, targetVisibility, Time.deltaTime / duration);
            ApplyAlpha(Mathf.Lerp(occludedAlpha, 1f, currentVisibility));
        }

        public void SetVisible(bool visible) => SetOccluded(!visible);

        public void SetOccluded(bool occluded, bool immediate = false)
        {
            PrepareMaterials();
            targetVisibility = occluded ? 0f : 1f;

            if (!immediate) return;
            currentVisibility = targetVisibility;
            ApplyAlpha(Mathf.Lerp(occludedAlpha, 1f, currentVisibility));
        }

        private void PrepareMaterials()
        {
            if (materialsPrepared) return;
            materialsPrepared = true;
            PrepareRenderer(wallRenderer);
            PrepareRenderer(topRenderer);
        }

        private void PrepareRenderer(Renderer targetRenderer)
        {
            if (targetRenderer == null || originalMaterials.ContainsKey(targetRenderer)) return;

            Material[] originals = targetRenderer.sharedMaterials;
            originalMaterials[targetRenderer] = originals;
            Material[] instances = new Material[originals.Length];

            for (int i = 0; i < originals.Length; i++)
            {
                Material instance = new Material(originals[i]);
                instance.name = originals[i].name + " (Wall Fade)";
                MakeTransparent(instance);
                instances[i] = instance;
                fadeMaterials.Add(instance);
                originalAlphas.Add(instance.HasProperty("_BaseColor")
                    ? instance.GetColor("_BaseColor").a
                    : instance.HasProperty("_Color") ? instance.color.a : 1f);
            }

            targetRenderer.sharedMaterials = instances;
        }

        private static void MakeTransparent(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        private void ApplyAlpha(float multiplier)
        {
            for (int i = 0; i < fadeMaterials.Count; i++)
            {
                Material material = fadeMaterials[i];
                string colorProperty = material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                if (!material.HasProperty(colorProperty)) continue;
                Color color = material.GetColor(colorProperty);
                color.a = originalAlphas[i] * multiplier;
                material.SetColor(colorProperty, color);
            }
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in originalMaterials)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;

            foreach (Material material in fadeMaterials)
                if (material != null) Destroy(material);
        }
    }
}
