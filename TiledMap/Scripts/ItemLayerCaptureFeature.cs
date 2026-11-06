using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Demo.Water
{
    /// <summary>
    /// Draws each visible renderer on the tracked layers into a color RenderTexture,
    /// vertically mirrored around its own world position, then exposes the result
    /// as a global texture. The water shader (GroundWaterPuddleURP) samples it as
    /// _ItemLayerTexture at its own screen UV.
    /// </summary>
    public class ItemLayerCaptureFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Tooltip(
                "Layers rendered into the capture texture. Exclude the water layer or the water will appear in its own reflection.")]
            public LayerMask captureLayers = ~0;

            [Tooltip(
                "Optional Sorting Layer filter, e.g. \"Item\". Leave empty to accept every sorting layer that matches the mask above.")]
            public string sortingLayerName = "";

            [Tooltip("Global texture name the water shader samples.")]
            public string globalTextureName = "_ItemLayerTexture";

            [Tooltip(
                "Clear color of the capture texture. Transparent black lets the shader use alpha to detect where items were drawn.")]
            public Color clearColor = new Color(0f, 0f, 0f, 0f);

            [Tooltip("How often (in frames) the tracked renderer list is rescanned.")]
            [Min(1)]
            public int rescanInterval = 60;
        }

        private class CapturePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId[] PassTagPriority =
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            private readonly Settings settings;
            private readonly List<Renderer> trackedRenderers = new();
            private readonly List<Renderer> drawList = new();
            private readonly Dictionary<Shader, int> passIndexCache = new();
            private RTHandle legacyTexture;
            private int lastRescanFrame = int.MinValue;

            public CapturePass(Settings settings)
            {
                this.settings = settings;
                profilingSampler = new ProfilingSampler("Item Layer Capture");
            }

            private static Matrix4x4 BuildFlipMatrix(float pivotY)
            {
                var matrix = Matrix4x4.identity;
                matrix.m11 = -1f;
                matrix.m13 = 2f * pivotY;
                return matrix;
            }

            private static RenderTextureDescriptor CreateCaptureDescriptor(
                RenderTextureDescriptor source, bool hdrEnabled)
            {
                var descriptor = source;
                descriptor.colorFormat = hdrEnabled ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                descriptor.depthBufferBits = 0;
                descriptor.depthStencilFormat = GraphicsFormat.None;
                descriptor.msaaSamples = 1;
                return descriptor;
            }

            private void RescanRenderers()
            {
                trackedRenderers.Clear();
                trackedRenderers.AddRange(UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                trackedRenderers.RemoveAll(r => r is not (SpriteRenderer or MeshRenderer or SkinnedMeshRenderer));
                lastRescanFrame = Time.frameCount;
            }

            private bool PassesFilter(Renderer renderer)
            {
                if (((1 << renderer.gameObject.layer) & settings.captureLayers) == 0)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(settings.sortingLayerName))
                {
                    int wantedId = SortingLayer.NameToID(settings.sortingLayerName);
                    if (renderer.sortingLayerID != wantedId)
                    {
                        return false;
                    }
                }

                return true;
            }

            private List<Renderer> CollectDrawList()
            {
                bool rescan = lastRescanFrame < 0 || Time.frameCount - lastRescanFrame >= settings.rescanInterval;
                if (rescan)
                {
                    RescanRenderers();
                }

                drawList.Clear();
                foreach (Renderer renderer in trackedRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.isVisible || !PassesFilter(renderer))
                    {
                        continue;
                    }

                    drawList.Add(renderer);
                }

                if (rescan && lastRescanFrame > 0 && drawList.Count == 0)
                {
                    if (trackedRenderers.Count == 0)
                    {
                        Debug.LogWarning(
                            "[ItemLayerCapture] No SpriteRenderer/MeshRenderer/SkinnedMeshRenderer found in the scene.");
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[ItemLayerCapture] Renderers were found but none passed the filter. " +
                            "Check Capture Layers (currently 0x" + settings.captureLayers.value.ToString("X8") + ")" +
                            (string.IsNullOrEmpty(settings.sortingLayerName)
                                ? "."
                                : " and Sorting Layer \"" + settings.sortingLayerName + "\"."));
                    }
                }

                // Draw back-to-front so overlapping transparent items keep their order.
                drawList.Sort((a, b) =>
                {
                    int layer = SortingLayer.GetLayerValueFromID(a.sortingLayerID)
                        .CompareTo(SortingLayer.GetLayerValueFromID(b.sortingLayerID));
                    return layer != 0 ? layer : a.sortingOrder.CompareTo(b.sortingOrder);
                });

                return drawList;
            }

            private int GetPassIndex(Material material)
            {
                Shader shader = material.shader;
                if (passIndexCache.TryGetValue(shader, out int cached))
                {
                    return cached;
                }

                int index = 0;
                foreach (ShaderTagId tag in PassTagPriority)
                {
                    for (int i = 0; i < shader.passCount; ++i)
                    {
                        if (shader.FindPassTagValue(i, tag) == tag)
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index != 0 || shader.FindPassTagValue(0, tag) == tag)
                    {
                        break;
                    }
                }

                passIndexCache[shader] = index;
                return index;
            }

            private static int GetSubMeshCount(Renderer renderer)
            {
                Mesh mesh = renderer switch
                {
                    MeshRenderer => renderer.GetComponent<MeshFilter>()?.sharedMesh,
                    SkinnedMeshRenderer skinned => skinned.sharedMesh,
                    _ => null,
                };
                return mesh == null ? 1 : mesh.subMeshCount;
            }

            private static float GetFlipAnchorY(Renderer renderer)
            {
                if (renderer is SpriteRenderer spriteRenderer && spriteRenderer.sprite != null)
                {
                    // The sprite mesh is generated so that its pivot (sprite.pivot
                    // in pixel space) sits exactly on the renderer's transform
                    // position, which is therefore the pivot's world anchor.
                    var offset = renderer.bounds.size.y * .05f;
                    return spriteRenderer.transform.position.y + offset;
                }

                return renderer.bounds.center.y;
            }

            private static void DrawMirroredItems(
                Camera camera,
                List<Renderer> renderers,
                CapturePass pass,
                CommandBuffer cmd)
            {
                DrawMirroredItemsCore(camera, renderers, pass,
                    cmd.SetViewProjectionMatrices, cmd.SetInvertCulling,
                    (renderer, material, subMesh, passIndex) => cmd.DrawRenderer(renderer, material, subMesh, passIndex));
            }

            private static void DrawMirroredItems(
                Camera camera,
                List<Renderer> renderers,
                CapturePass pass,
                RasterCommandBuffer cmd)
            {
                DrawMirroredItemsCore(camera, renderers, pass,
                    cmd.SetViewProjectionMatrices, cmd.SetInvertCulling,
                    (renderer, material, subMesh, passIndex) => cmd.DrawRenderer(renderer, material, subMesh, passIndex));
            }

            private static void DrawMirroredItemsCore(
                Camera camera,
                List<Renderer> renderers,
                CapturePass pass,
                Action<Matrix4x4, Matrix4x4> setMatrices,
                Action<bool> setInvertCulling,
                Action<Renderer, Material, int, int> drawRenderer)
            {
                var projection = camera.projectionMatrix;
                setInvertCulling(true);

                foreach (Renderer renderer in renderers)
                {
                    var flip = BuildFlipMatrix(GetFlipAnchorY(renderer));
                    setMatrices(camera.worldToCameraMatrix * flip, projection);

                    Material[] materials = renderer.sharedMaterials;
                    int subMeshCount = GetSubMeshCount(renderer);
                    for (int subMesh = 0; subMesh < subMeshCount; ++subMesh)
                    {
                        Material material = materials[Mathf.Min(subMesh, materials.Length - 1)];
                        if (material == null)
                        {
                            continue;
                        }

                        drawRenderer(renderer, material, subMesh, pass.GetPassIndex(material));
                    }
                }

                setInvertCulling(false);
                setMatrices(camera.worldToCameraMatrix, projection);
            }

            // ------------------------------------------------------------------
            // Render Graph path (URP 17). Active when Render Graph is enabled.
            // ------------------------------------------------------------------
            private class PassData
            {
                internal Color clearColor;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                var descriptor = CreateCaptureDescriptor(cameraData.cameraTargetDescriptor, cameraData.isHdrEnabled);
                var textureDesc = new TextureDesc(descriptor.width, descriptor.height)
                {
                    name = "ItemLayerCaptureRT",
                    colorFormat = descriptor.graphicsFormat,
                    depthBufferBits = DepthBits.None,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    msaaSamples = MSAASamples.None,
                };
                TextureHandle captureTexture = renderGraph.CreateTexture(textureDesc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Item Layer Capture", out var passData, profilingSampler))
                {
                    passData.clearColor = settings.clearColor;
                    builder.SetRenderAttachment(captureTexture, 0, AccessFlags.Write);
                    builder.UseAllGlobalTextures(true);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetGlobalTextureAfterPass(captureTexture, Shader.PropertyToID(settings.globalTextureName));

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(false, true, data.clearColor);
                        DrawMirroredItems(cameraData.camera, CollectDrawList(), this, context.cmd);
                    });
                }
            }

            // ------------------------------------------------------------------
            // Compatibility-mode path (Render Graph disabled). These members are
            // deprecated in URP 17 but still drive rendering while the project
            // runs with "Compatibility Mode (Render Graph Disabled)".
            // ------------------------------------------------------------------
#pragma warning disable CS0618, CS0672
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = CreateCaptureDescriptor(
                    renderingData.cameraData.cameraTargetDescriptor, renderingData.cameraData.isHdrEnabled);

                RenderingUtils.ReAllocateHandleIfNeeded(
                    ref legacyTexture, descriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "ItemLayerCaptureRT");

                ConfigureTarget(legacyTexture);
                ConfigureClear(ClearFlag.Color, settings.clearColor);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                List<Renderer> renderers = CollectDrawList();

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, profilingSampler))
                {
                    cmd.ClearRenderTarget(false, true, settings.clearColor);
                    DrawMirroredItems(camera, renderers, this, cmd);
                    cmd.SetGlobalTexture(Shader.PropertyToID(settings.globalTextureName), legacyTexture);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
#pragma warning restore CS0618, CS0672

            public void Release()
            {
                legacyTexture?.Release();
                legacyTexture = null;
            }
        }

        [SerializeField] private Settings settings = new Settings();

        private CapturePass capturePass;

        public Settings CurrentSettings => settings;

        public override void Create()
        {
            capturePass = new CapturePass(settings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(capturePass);
        }

        protected override void Dispose(bool disposing)
        {
            capturePass?.Release();
            base.Dispose(disposing);
        }
    }
}
