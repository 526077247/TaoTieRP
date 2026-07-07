using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie.RenderPipelines
{
    /// <summary>
    /// 后期效果基类。每个子类封装一个独立效果（Bloom、ColorGrading 等）。
    /// 效果实例序列化在 PostFXSettings.effects 列表中，可重新排序和单独启用/禁用。
    /// 类似 URP ScriptableRendererFeature 模式。
    /// </summary>
    [System.Serializable]
    public abstract class PostFXEffect
    {
        [SerializeField] bool enabled = true;
        public bool IsEnabled => enabled;

        /// <summary>Inspector 中显示的名称</summary>
        public abstract string DisplayName { get; }

        /// <summary>此效果依赖的 shader pass 名称列表（用于验证 shader 完整性）</summary>
        public virtual IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        /// <summary>
        /// 在 RenderGraph 中执行效果。
        /// 返回处理后的纹理（如果效果禁用或无操作，返回原 source）。
        /// </summary>
        public abstract TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures);
    }
}
