using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public static class DepthDebugger
    {
        const string panelName = "Depth Debug";

        static readonly int
            modeID = Shader.PropertyToID("_DepthDebuggerMode"),
            splitID = Shader.PropertyToID("_DepthDebuggerSplit"),
            opacityID = Shader.PropertyToID("_DepthDebuggerOpacity"),
            depthTexID = Shader.PropertyToID("_CameraDepthTexture");

        static Material material;

        static bool showDepth;
        static int mode;
        static bool splitScreen;
        static float opacity = 1f;
        static bool useDepthCopy = true;

        public static bool IsActive => showDepth && opacity > 0f && material != null;

        public static bool UseDepthCopy => useDepthCopy;

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Initialize(Shader shader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            DebugManager.instance.GetPanel(panelName, true).children.Add(
                new DebugUI.BoolField
                {
                    displayName = "Show Depth",
                    tooltip = "Toggle the depth debug overlay.",
                    getter = static () => showDepth,
                    setter = static value => showDepth = value
                },
                new DebugUI.IntField
                {
                    displayName = "Mode",
                    tooltip = "0 = Linear Eye, 1 = Linear 01, 2 = Raw",
                    min = static () => 0,
                    max = static () => 2,
                    getter = static () => mode,
                    setter = static value => mode = value
                },
                new DebugUI.BoolField
                {
                    displayName = "Split Screen",
                    tooltip = "Show depth only on the right half.",
                    getter = static () => splitScreen,
                    setter = static value => splitScreen = value
                },
                new DebugUI.FloatField
                {
                    displayName = "Opacity",
                    tooltip = "Overlay opacity.",
                    min = static () => 0f,
                    max = static () => 1f,
                    getter = static () => opacity,
                    setter = static value => opacity = value
                },
                new DebugUI.BoolField
                {
                    displayName = "Use Depth Copy",
                    tooltip = "ON: read _CameraDepthTexture (the copy). OFF: read raw depth attachment.",
                    getter = static () => useDepthCopy,
                    setter = static value => useDepthCopy = value
                }
            );
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Cleanup()
        {
            CoreUtils.Destroy(material);
            DebugManager.instance.RemovePanel(panelName);
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Render(RenderGraphContext context, TextureHandle depthTexture)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetGlobalTexture(depthTexID, depthTexture);
            buffer.SetGlobalFloat(modeID, (float)mode);
            buffer.SetGlobalFloat(splitID, splitScreen ? 1f : 0f);
            buffer.SetGlobalFloat(opacityID, opacity);

            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

            buffer.DrawProcedural(
                Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);

            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }
    }
}
