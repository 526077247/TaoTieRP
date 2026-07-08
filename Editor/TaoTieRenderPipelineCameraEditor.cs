using UnityEditor;

namespace TaoTie.RenderPipelines.Editor
{
    [CustomEditor(typeof(TaoTieRenderPipelineCamera))]
    public class TaoTieRenderPipelineCameraEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Settings are shown inline in the Camera inspector (TaoTieCameraEditor).
            // This component has no user-editable properties of its own.
        }
    }
}
