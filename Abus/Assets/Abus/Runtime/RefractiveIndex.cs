using Rcying.Atmosphere;
using UnityEngine;

namespace Abus.Runtime
{
    [System.Serializable]
    public struct RefractiveIndex
    {
        [Range(1.0f, 2.0f)]
        public float real;

        [Range(-0.01f, 0.0f)]
        public float imagine;
        
        // Operator to convert to complex.
        public static implicit operator Complex(RefractiveIndex index)
        {
            return new Complex()
            {
                real = index.real,
                imagine = index.imagine
            };
        }
    }
}