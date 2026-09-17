using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    public class FullScreenPassWrapper
    {
        private FullScreenPassRendererFeature.FullScreenRenderPass _fullScreenRenderPass;

        public FullScreenPassWrapper(string passName, Material material, int passIndex, bool fetchActiveColor,
            bool bindDepthStencilAttachment,RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing)
        {
            _fullScreenRenderPass = new FullScreenPassRendererFeature.FullScreenRenderPass(passName);
            _fullScreenRenderPass.SetupMembers(material, passIndex, fetchActiveColor, bindDepthStencilAttachment);
            _fullScreenRenderPass.renderPassEvent = renderPassEvent;
        }

        public void EnqueuePass(Camera camera)
        {
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_fullScreenRenderPass);
        }
    }
}
