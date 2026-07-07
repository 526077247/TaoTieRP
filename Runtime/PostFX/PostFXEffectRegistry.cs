using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TaoTie.RenderPipelines
{
    /// <summary>
    /// 运行时通过反射发现所有 PostFXEffect 子类。
    /// 用于 Editor 中的 "Add Effect" 下拉菜单和效果类型枚举。
    /// </summary>
    public static class PostFXEffectRegistry
    {
        static List<Type> effectTypes;
        static Dictionary<Type, string> displayNames;

        /// <summary>获取所有可用的 PostFXEffect 子类类型（非抽象）</summary>
        public static IReadOnlyList<Type> GetEffectTypes()
        {
            if (effectTypes == null)
            {
                effectTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => GetTypesSafely(a))
                    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(PostFXEffect).IsAssignableFrom(t))
                    .OrderBy(t => GetDisplayName(t))
                    .ToList();
            }
            return effectTypes;
        }

        /// <summary>获取类型的显示名称</summary>
        public static string GetDisplayName(Type type)
        {
            if (displayNames != null && displayNames.TryGetValue(type, out string name))
                return name;

            var instance = Activator.CreateInstance(type) as PostFXEffect;
            string displayName = instance?.DisplayName ?? type.Name;
            instance = null;
            return displayName;
        }

        static IEnumerable<Type> GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                return Array.Empty<Type>();
            }
        }
    }
}
