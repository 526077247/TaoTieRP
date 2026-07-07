using System;
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

        /// <summary>如果此效果使用独立 shader，返回 shader 名称；使用共享 PostFXStack shader 的效果返回 null。</summary>
        public virtual string ShaderName => null;

        /// <summary>由 PostFXSettings 调用，确保序列化的 shader 引用已填充。</summary>
        public virtual void EnsureShaderReference() { }

        /// <summary>
        /// 在 RenderGraph 中执行效果。
        /// 返回处理后的纹理（如果效果禁用或无操作，返回原 source）。
        /// </summary>
        public abstract TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures);

        /// <summary>子类实现：释放自身的静态 material 等资源。默认无操作。</summary>
        protected virtual void DisposeInternal() { }

        static bool registered;
        static readonly List<Action> disposeActions = new();

        /// <summary>
        /// 注册一个效果的释放回调。在 AppDomain 卸载或 PlayMode 退出时自动触发。
        /// 效果类在静态构造函数或 EnsureMaterial 中调用此方法即可，无需外部 Dispose。
        /// </summary>
        protected static void RegisterDispose(Action dispose)
        {
            lock (disposeActions)
            {
                if (!registered)
                {
                    registered = true;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
                    UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnDomainReload;
#endif
                }
                if (!disposeActions.Contains(dispose))
                    disposeActions.Add(dispose);
            }
        }

        static void DisposeAll()
        {
            lock (disposeActions)
            {
                foreach (var action in disposeActions)
                    action?.Invoke();
            }
        }

#if UNITY_EDITOR
        static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
                state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                DisposeAll();
            }
        }

        static void OnDomainReload()
        {
            DisposeAll();
        }
#endif
    }
}
