using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public class TaoTieRenderPipelineCamera : MonoBehaviour {

        [SerializeField]
        CameraSettings settings = default;

        ProfilingSampler sampler;

        public ProfilingSampler Sampler => sampler ??= new(GetComponent<Camera>().name);
        
        public CameraSettings Settings => settings ?? (settings = new CameraSettings());
        
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnEnable() => sampler = null;
#endif
    }
}