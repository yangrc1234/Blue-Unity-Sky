using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;

namespace Rcying.Atmosphere
{
    
[System.Serializable]
[StructLayout(LayoutKind.Sequential)]
public struct Complex
{
    public double real;
    public double imagine;

    public double ModulusSqr()
    {
        return real * real + imagine * imagine;
    }
}

public class MieUtils
{
    [DllImport("PrahlMieLib.dll")]
    private static extern void CalculateMie(
        double inSizeParameter,
        Complex inRefractionIndex,
        int inNumAngles,
        double[] inAngleCosines,
        Complex[] outS1,
        Complex[] outS2,
        ref double outQExt,
        ref double outQsca,
        ref double outQback,
        ref double outQG
    );

    private static double MapAngleInRange(double startAngle, double endAngle, int i, int count)
    {
        return (endAngle - startAngle) * ((double)i / (count - 1)) + startAngle;
    }

    public static void FillPhaseAngle(double[] outAngleConsinesArray, double startAngle, double endAngle)
    {
        for (int i = 0; i < outAngleConsinesArray.Length; i++)
        {
            outAngleConsinesArray[i] = Math.Cos(MapAngleInRange(startAngle,endAngle,i, outAngleConsinesArray.Length));
        }
    }

    /// <summary>
    /// Convert calculated S1, S2 to normalized un-polarized phase function
    /// </summary>
    /// <param name="s1"></param>
    /// <param name="s2"></param>
    /// <param name="outNormalizedPhase"></param>
    /// <returns></returns>
    public static void ConvertToNormalizedPhase(Complex[] s1, Complex[] s2, double startAngle, double endAngle, double dphi, double[] outNormalizedPhase)
    {
        double integeral = 0.0f;
        for (int i = 0; i < s1.Length; i++)
        {
            outNormalizedPhase[i] = ConvertToUnPolarizedPhase(s1[i], s2[i]);
            var phi = MapAngleInRange(startAngle, endAngle, i, s1.Length);
            integeral += outNormalizedPhase[i] * Math.Sin(phi) * dphi;
        }

        for (int i = 0; i < s1.Length; i++)
        {
            outNormalizedPhase[i] /= (integeral * Math.PI * 2.0);
        }
    }

    public static double ConvertToUnPolarizedPhase(Complex s1, Complex s2)
    {
        return 0.5f * (s1.ModulusSqr() + s2.ModulusSqr());
    }

    public static double CalculateSizeParameter(double relativeWaveLength, double relativeParticleSize)
    {
        return 2.0 * Math.PI * relativeParticleSize / relativeWaveLength;
    }

    public static void CalculateMie(double inSizeParameter, Complex inRefractionIndex, double[] inPhaseAngleConsines, out double outqext, out double outqscat, out double outqback, out double outqg, Complex[] outs1, Complex[] outs2)
    {
        if (inPhaseAngleConsines != null)
        {
            Assert.AreEqual(inPhaseAngleConsines.Length, outs1.Length);
            Assert.AreEqual(inPhaseAngleConsines.Length, outs2.Length);
        }

        outqext = 0.0;
        outqscat = 0.0;
        outqback = 0.0;
        outqg = 0.0;
        CalculateMie(inSizeParameter, inRefractionIndex, inPhaseAngleConsines?.Length??0, inPhaseAngleConsines, outs1, outs2, ref outqext, ref outqscat, ref outqback, ref outqg);
    }
    
    // Parametrization by droplet size.
    // Input: droplet size, in micrometres.
    // For d < 1.5f, should fallback to HG. 
    public static Vector4 JEPhaseParams(float d)
    {
        if (d >= 5.0f)
        {
            return new Vector4(
                Mathf.Exp(-0.0990567f / (d - 1.67154f)), // gHG
                Mathf.Exp(-2.20679f / (d + 3.91029f) - 0.428934f), // gD
                Mathf.Exp(3.62489f - 8.29288f / (d + 5.52825f)), // alpha
                Mathf.Exp(-0.599085f / (d - 0.641583f) - 0.665888f) // wD
            );
        }
        else
        {
            return new Vector4(
                0.0604931f * Mathf.Log(Mathf.Log(d)) + 0.940256f,
                0.500411f - (0.081287f / (-2.0f * Mathf.Log(d) + Mathf.Tan(Mathf.Log(d)) + 1.27551f)),
                7.30354f * Mathf.Log(d) + 6.31675f,
                0.026914f * (Mathf.Log(d) - Mathf.Cos(5.68947f * (Mathf.Log(Mathf.Log(d)) - 0.0292149f))) + 0.376475f
            );
        }
    }
}
}

