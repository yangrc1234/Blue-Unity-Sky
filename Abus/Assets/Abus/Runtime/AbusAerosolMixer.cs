﻿using System;
using UnityEngine;

namespace Abus.Runtime
{
    /// <summary>
    /// Helps setup aerosol properties.
    /// Our core system accepts per-particle input,
    /// It's flexible, but not easy to use.
    ///
    /// This script helps to setup aerosol properties using pre-configured profiles.
    /// </summary>
    public class AbusAerosolMixer : MonoBehaviour
    {
        public AbusAerosolTypeProfile MainProfile;

        private AbusCore core;

        private void OnValidate()
        {
            Apply();
        }

        AbusCore GetCore()
        {
            if (core == null)
            {
                core = GetComponent<AbusCore>();
            }

            return core;
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            var core = GetCore();
            if (this.core == null)
                return;
            
            // Clear all aerosol components.
            foreach (var aerosol in core.AerosolComponents)
            {
                aerosol.geometryCrossSection = 0.0f;
            }
            
            // Add aerosol components.
            int Index = 0;
            if (MainProfile != null)
            {
                foreach (var entry in MainProfile.entries)
                {
                    if (entry.component)
                    {
                        if (Index >= core.AerosolComponents.Count)
                        {
                            core.AerosolComponents.Add(new ());
                        }
                        core.AerosolComponents[Index].heightType = EAerosolHeightType.PlanetBoundaryLayer;
                        core.AerosolComponents[Index].scaleHeightKM = MainProfile.scaleHeightKM;
                        var meanRadius = entry.component.GetSurfaceAreaDistributionGeometricMean(); // Scattering coefficient is directly related to cross-section area, so we use the mean of surface area distribution.
                        core.AerosolComponents[Index].geometryCrossSection = entry.NumberPerCM3 * Mathf.PI * meanRadius * meanRadius * 1e-3f;
                        core.AerosolComponents[Index].radiusUm = meanRadius;
                        core.AerosolComponents[Index].radiusGeometricDeviation = entry.component.geometricDeviation;
                        core.AerosolComponents[Index].RefractiveIndex = entry.component.RefractiveIndexData.CalculateValueAtWavelength(550.0f);
                        Index++;
                    }
                }
            }
            
            core.MarkSettingsDirty();
        }

        public static void NotifyProfileChanged(object changedData)
        {
            foreach (var mixer in FindObjectsOfType<AbusAerosolMixer>())
            {
                mixer.Apply();
            }
        }
    }
}