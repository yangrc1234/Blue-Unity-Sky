using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    // Implement aerial perspective on opaque as URP render feature.
    public class AerialPerspectiveOnOpaqueRenderFeature : ScriptableRendererFeature
    {
        public Shader applyShader;
        public ComputeShader lutShader;

        public override void Create()
        {
            if (applyShader == null || lutShader == null)
            {
                Debug.LogError("AerialPerspectiveRenderFeature: Material is null.");
            }
        }

        private AerialPerspectiveRenderPass pass;

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null)
                pass = new AerialPerspectiveRenderPass(applyShader, lutShader);
            if (renderingData.cameraData.cameraType == CameraType.SceneView || renderingData.cameraData.cameraType == CameraType.Game)
            {
                pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
                renderer.EnqueuePass(pass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            pass.Dispose();
        }
    }
}