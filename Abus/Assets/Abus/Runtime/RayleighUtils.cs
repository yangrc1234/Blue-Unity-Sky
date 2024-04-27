using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Many data/calculation is from [Bucholtz95] Rayleigh-scattering calculations for the terrestrial atmosphere
/// Rayleigh scattering is not easy to calculate actually.
/// Its cross-section depends on refractive index, which has only tabulated measurements.
/// Best we can do, is calculate rayleigh for sea level standard air(we have refractive index and density data),
/// Then scale the coefficient with altitude.
/// </summary>
public static class RayleighUtils
{
    // Per unit meter cube
    const double StandardAirDensity = 2.54743e25;

    public static double GetStandardAirRefractiveIndex(double waveLengthNM)
    {
        var waveLengthUM = waveLengthNM * 1e-3;
        return 1.0 + (0.05792105 / (238.1085 - Math.Pow(waveLengthUM, -2.0)) + 0.00167917 / (57.362 - Math.Pow(waveLengthUM, -2.0)));
    }
    
    public static double CalculateRayleighScatteringCoefficientM(double waveLengthNM, double depolarizationFactor, double airRefractiveIndex)
    {
        var waveLengthM = waveLengthNM * 1e-9;
        // All done in meter.
        var numerator = 24 * Math.Pow(Math.PI, 3.0) * Math.Pow((airRefractiveIndex * airRefractiveIndex - 1), 2.0);
        var denominator = Math.Pow(waveLengthM, 4) * StandardAirDensity * Math.Pow((airRefractiveIndex * airRefractiveIndex + 2), 2.0);
        var KingFactor = (6.0 + depolarizationFactor * 3.0) / (6.0 - depolarizationFactor * 7);
        
        return numerator / denominator * KingFactor;
    }

    public static double RayleighPhaseFunction(double mu, double depolarizationFactor)
    {
        var gamma = depolarizationFactor / (2.0 - depolarizationFactor);
        return 3.0 / (16.0 * Math.PI * (1 + 2 * gamma)) * ((1 + 3.0 * gamma) + (1 - gamma) * mu * mu);
    }

    public static double RayleighPhaseFunctionSimple(double mu)
    {
        return 3.0 / (16.0 * Math.PI) * (1.0 + mu * mu);
    }
}
