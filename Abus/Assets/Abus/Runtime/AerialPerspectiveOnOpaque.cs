using System;
using Rcying.Atmosphere;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Abus.Runtime
{
    public class AerialPerspectiveRenderPass : ScriptableRenderPass, IDisposable
    {
        private Material material;
        private RTHandle textureHandle;
        private RenderTextureDescriptor textureDescriptor;
        private ComputeShader lutShader;
        
        public AerialPerspectiveRenderPass(Shader shader, ComputeShader lutShader)
        {
            this.material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            this.lutShader = lutShader;
            
            base.profilingSampler = new ProfilingSampler("AerialPerspectiveOnOpaque");
        }

        public void Dispose()
        {
            
        #if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                Object.Destroy(material);
            }
            else
            {
                Object.DestroyImmediate(material);
            }
        #else
                Object.Destroy(material);
        #endif

            if (textureHandle != null) textureHandle.Release();
        }

        private bool scenePrepared = false;
        private AbusLutUpdater updater;
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            updater = AbusCore.Instance ? AbusCore.Instance.GetComponent<AbusLutUpdater>() : null;
            if (updater)
            {
                scenePrepared = true;
            
                //Set the red tint texture size to be the same as the camera target size.
                textureDescriptor = cameraTextureDescriptor;
                textureDescriptor.depthStencilFormat = GraphicsFormat.None;

                //Check if the descriptor has changed, and reallocate the RTHandle if necessary.
                RenderingUtils.ReAllocateIfNeeded(ref textureHandle, textureDescriptor);
            }
            else
            {
                scenePrepared = false;
            }
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RTHandle cameraTargetHandle =
                renderingData.cameraData.renderer.cameraColorTargetHandle;
            
            if (!scenePrepared || !material || cameraTargetHandle == null)
                return;
            
            //Get a CommandBuffer from pool.
            CommandBuffer cmd = CommandBufferPool.Get();

            // Generate LUT.
            // RTHandle lutHandle;
            // {
            //     RenderTextureDescriptor lutDescriptor = new RenderTextureDescriptor(updater.aerialPerspectiveTextureSize.x, updater.aerialPerspectiveTextureSize.y, RenderTextureFormat.ARGBHalf);
            //     lutDescriptor.volumeDepth = updater.aerialPerspectiveTextureSize.z;
            //     textureDescriptor.depthStencilFormat = GraphicsFormat.None;
            //     var scatteringLut = RTHandles.Alloc(lutDescriptor);
            //     var extinctionLut = RTHandles.Alloc(lutDescriptor);
            //     cmd.SetGlobalTexture("OutAerialPerspectiveSRGBLut", scatteringLut);
            //     cmd.SetGlobalTexture("OutAerialPerspectiveExtinctionSRGBLut", extinctionLut);
            //     var dispatchSize =
            //         CommonUtils.GetDispatchGroup(updater.aerialPerspectiveTextureSize, new Vector3Int(4, 4, 4));
            //     cmd.DispatchCompute(lutShader, 0, dispatchSize.x, dispatchSize.y, dispatchSize.z);
            // }
            
            cmd.SetGlobalTexture("AerialPerspectiveSRGBLut", updater.SRGBAerialPerspectiveLut);

            // Blit from the camera target to the temporary render texture,
            // using the first shader pass.
            Blit(cmd, cameraTargetHandle, cameraTargetHandle, material);

            //Execute the command buffer and release it back to the pool.
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}