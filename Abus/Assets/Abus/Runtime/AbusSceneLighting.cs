using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Abus.Runtime
{
    [ExecuteAlways]
    public class AbusSceneLighting : MonoBehaviour
    {
        private AbusCore core;
        private AbusLutUpdater lutUpdater;

        public bool bUpdateBoundLight;
        public bool bUpdateAmbient;
        public bool bUpdateSkybox;
        public Shader skyboxShader;

        Vector3 GetNormalizedAndIntensity(Vector3 srgbColor, out float intensity)
        {
            var Max = Mathf.Max(srgbColor.x, srgbColor.y, srgbColor.z);
            intensity = Max;
            return srgbColor / Max;
        }
        
        private void Update()
        {
            core = GetComponent<AbusCore>();
            lutUpdater = GetComponent<AbusLutUpdater>();
            if (core == null || lutUpdater == null)
                return;

            if (bUpdateBoundLight && core.boundLight)
            {
                var color = lutUpdater.SunDiscIrradiance;
                color = GetNormalizedAndIntensity(color, out var intensity);
                core.boundLight.intensity = intensity;
                core.boundLight.color = new Color(color.x, color.y, color.z);
            }

            if (bUpdateAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = lutUpdater.SkyIrradiance;
                // var test = new SphericalHarmonicsL2();
                // test.AddDirectionalLight(core.boundLight.transform.up, Color.white, 10000.0f);
                // RenderSettings.ambientProbe = test;
                // RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            }

            if (bUpdateSkybox && skyboxShader)
            {
                SetSkybox();
            }
        }
        

        private Material skyboxMaterial;
        public void SetSkybox()
        {
            if (lutUpdater.SrgbSkyViewLut == null)
                return;
            
            if (skyboxMaterial == null || skyboxMaterial.shader != skyboxShader)
            {
                skyboxMaterial = new Material(skyboxShader);
            }
            skyboxMaterial.SetTexture("SrgbSkyViewTexture", lutUpdater.SrgbSkyViewLut);
            skyboxMaterial.SetTexture("SRGBTransmittanceTexture", lutUpdater.SrgbTransmittanceLut);
            
            RenderSettings.skybox = skyboxMaterial;
        }
    }
}