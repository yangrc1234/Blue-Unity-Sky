using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rcying.Atmosphere
{
    public static class CommonUtils
    {
        static Matrix4x4 XyzToSRGB = new Matrix4x4( 
            new Vector4(3.2406f, -1.5372f, -0.4986f, 0.0f),
            new Vector4(-0.9689f, 1.8758f, 0.0415f, 0.0f),
            new Vector4(0.0557f, -0.2040f, 1.0570f, 0.0f),
            Vector4.zero
        );

        static float xFit_1931( float wave )
        {
            float t1 = (wave-442.0f)*((wave<442.0f)?0.0624f:0.0374f);
            float t2 = (wave-599.8f)*((wave<599.8f)?0.0264f:0.0323f);
            float t3 = (wave-501.1f)*((wave<501.1f)?0.0490f:0.0382f);
            return 0.362f*Mathf.Exp(-0.5f*t1*t1) + 1.056f*Mathf.Exp(-0.5f*t2*t2)
                   - 0.065f*Mathf.Exp(-0.5f*t3*t3);
        }
        
        static float yFit_1931( float wave )
        {
            float t1 = (wave-568.8f)*((wave<568.8f)?0.0213f:0.0247f);
            float t2 = (wave-530.9f)*((wave<530.9f)?0.0613f:0.0322f);
            return 0.821f*Mathf.Exp(-0.5f*t1*t1) + 0.286f*Mathf.Exp(-0.5f*t2*t2);
        }
        
        static float zFit_1931( float wave )
        {
            float t1 = (wave-437.0f)*((wave<437.0f)?0.0845f:0.0278f);
            float t2 = (wave-459.0f)*((wave<459.0f)?0.0385f:0.0725f);
            return 1.217f*Mathf.Exp(-0.5f*t1*t1) + 0.681f*Mathf.Exp(-0.5f*t2*t2);
        }

        /// <summary>
        /// Give a wave length, map it to CIE 1931 XYZ
        /// The mapping function is from [Wyman13]Simple Analytic Approximations to the CIE XYZ Color Matching Functions, which shows pretty little error from the data in paper.
        /// </summary>
        /// <param name="waveLength"></param>
        /// <returns></returns>
        public static Vector3 MapWaveLengthToXYZ(float waveLength)
        {
            return new Vector3(xFit_1931(waveLength), yFit_1931(waveLength), zFit_1931(waveLength));
        }

        public static Color ConvertXyzToSRGB(Vector3 XYZ)
        {
            var transformed = XyzToSRGB.transpose * XYZ;
            return new Color(transformed.x, transformed.y, transformed.z, 1.0f);
        }

        public static Vector3Int GetDispatchGroup(Vector3Int size, Vector3Int groupSize)
        {
            return new Vector3Int(
                Mathf.CeilToInt(size.x / (float)groupSize.x),
                Mathf.CeilToInt(size.y / (float)groupSize.y),
                Mathf.CeilToInt(size.z / (float)groupSize.z)
            );
        }

        public static Vector3Int GetDispatchGroup(Vector2Int size, Vector2Int groupSize)
        {
            return new Vector3Int(
                Mathf.CeilToInt(size.x / (float)groupSize.x),
                Mathf.CeilToInt(size.y / (float)groupSize.y),
                1
            );
        }

        public static void Dispatch(this ComputeShader shader, int kernelIndex, Vector3Int groupCount)
        {
            shader.Dispatch(kernelIndex, groupCount.x, groupCount.y, groupCount.z);
        }

        public static void CreateLUT(ref RenderTexture result, string name, int width, int height, int zsize, RenderTextureFormat format, TextureWrapMode wrapModeU = TextureWrapMode.Clamp, TextureWrapMode wrapModeV = TextureWrapMode.Clamp) {
            if (result != null) {
                if (result.name == name 
                    && result.width == width
                    && result.height == height 
                    && result.volumeDepth == zsize 
                    && result.format == format
                    && result.enableRandomWrite == true
                    && result.wrapModeU == wrapModeU
                    && result.wrapModeV == wrapModeV
                   )
                    return;
                result.Release();
            }
            result = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            result.name = name;
            result.enableRandomWrite = true;
            if (zsize > 1) {
                result.dimension = TextureDimension.Tex3D;
                result.volumeDepth = zsize;
            } else {
                result.dimension = TextureDimension.Tex2D;
            }
            result.filterMode = FilterMode.Bilinear;
            result.wrapModeU = wrapModeU;
            result.wrapModeV = wrapModeV;
            result.Create();
        }

        static float ErfInv(float x)
        {
            float tt1, tt2, lnx, sgn;
            sgn = (x < 0) ? -1.0f : 1.0f;

            x = (1 - x)*(1 + x);        // x = 1 - x*x;
            lnx = Mathf.Log(x);

            tt1 = 2.0f / (Mathf.PI * 0.147f) + 0.5f * lnx;
            tt2 = 1.0f/(0.147f) * lnx;

            return(sgn * Mathf.Sqrt(-tt1 + Mathf.Sqrt(tt1*tt1 - tt2)));
        }
        
        public static void GetLogNormalBinning(float geometricMean, float geometricDeviation, float minRadius, float maxRadius, int binCount, List<float> outBinMean, List<float> outBinSize)
        {
            outBinMean.Clear();
            outBinSize.Clear();
            
            outBinMean.Add(geometricMean);
            outBinSize.Add(1.0f);
            return;

            var mean = Mathf.Log(geometricMean);
            var dev = Mathf.Log(geometricDeviation);

            // Setup bins between mean - 1sigma, mean + 1sigma.
            float totalWeight = 0.0f;
            for (int i = 0; i < binCount; i++)
            {
                var r = minRadius + (maxRadius - minRadius) * (i + 0.5f) / binCount;
                outBinMean.Add(r);
                
                var pdf = Mathf.Exp(-0.5f * (Mathf.Log(r) - mean) * (Mathf.Log(r) - mean) / (dev * dev)) / (Mathf.Sqrt(2.0f * Mathf.PI) * dev * r);
                totalWeight += pdf;
                outBinSize.Add(pdf);
            }
            
            // Normalize weight.    
            for (int i = 0; i < binCount; i++)
            {
                outBinSize[i] /= totalWeight;
            }
        }
        
        public static float LogNormalPDFFromGeometric(float GeometricMean, float GeometricDeviation, float radiusUm)
        {
            var Deviation = LogNormalDeviationFromGeometric(GeometricDeviation);
            var Mean = LogNormalMeanFromGeometric(GeometricMean);
            
            return (1.0f / (radiusUm * Deviation * Mathf.Sqrt(2.0f * Mathf.PI))) * 
                   Mathf.Exp(-0.5f * Mathf.Pow((Mathf.Log(radiusUm) - Mean) / Deviation, 2.0f));
        }
        
        public static float LogNormalMeanFromGeometric(float geometricMean)
        {
            return Mathf.Log(geometricMean);
        }
        
        public static float LogNormalDeviationFromGeometric(float geometryDeviation)
        { 
            return Mathf.Log(geometryDeviation);
        }

    }
}