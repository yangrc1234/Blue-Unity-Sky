using Rcying.Atmosphere;
using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    [System.Serializable]
    public struct RefractiveIndex
    {
        [Range(1.001f, 1.7f)]
        public float real;

        [FormerlySerializedAs("imagine")] [Range(-0.5f, 0.0f)]
        public float imag;
        
        // Operator to convert to complex.
        public static implicit operator Complex(RefractiveIndex index)
        {
            return new Complex()
            {
                real = index.real,
                imagine = index.imag
            };
        }
    }
}