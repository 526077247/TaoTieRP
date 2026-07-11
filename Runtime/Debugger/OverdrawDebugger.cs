using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public static class OverdrawDebugger
    {
        const string panelName = "Overdraw";

        static bool showOverdraw;
        static float opacity = 0.8f;

        public static bool IsActive => showOverdraw && opacity > 0f;

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Initialize()
        {
            DebugManager.instance.GetPanel(panelName, true).children.Add(
                new DebugUI.BoolField
                {
                    displayName = "Show Overdraw",
                    tooltip = "Visualize pixel overdraw with an additive heat map overlay.",
                    getter = static () => showOverdraw,
                    setter = static value => showOverdraw = value
                },
                new DebugUI.FloatField
                {
                    displayName = "Opacity",
                    tooltip = "Opacity of the overdraw overlay.",
                    min = static () => 0f,
                    max = static () => 1f,
                    getter = static () => opacity,
                    setter = static value => opacity = value
                }
            );
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Cleanup()
        {
            DebugManager.instance.RemovePanel(panelName);
        }
    }
}
