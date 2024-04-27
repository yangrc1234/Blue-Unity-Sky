using System.Collections.Generic;
using System.IO;
using Rcying.Atmosphere;
using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    /// <summary>
    /// Contains array of (wavelength, float).
    /// </summary>
    [CreateAssetMenu(menuName = "Abus/Wavelength Data")]
    public class WavelengthFloatData : ScriptableObject
    {
        #if UNITY_EDITOR
        [TextArea]
        public string dataComment;
        public TextAsset sourceCsv;
        public float wavelengthImportScale = 1.0f;
        public float valueImportScale = 1.0f;
        
        void PostProcessData(List<Entry> data)
        {
            // Process data scale.
            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                item.wavelength *= wavelengthImportScale;
                item.value *= valueImportScale;
                data[i] = item;
            }
            
            // Sort by wavelength, start at high frequency.
            data.Sort((a, b) => a.wavelength.CompareTo(b.wavelength));
            
            // Calculate width of each item.
            for (int i = 0; i < data.Count; i++)
            {
                if (i == 0)
                {
                    var item = data[i];
                    item.width = (data[1].wavelength - data[0].wavelength) * 0.5f;
                    data[i] = item;
                }
                else if (i == data.Count - 1)
                {
                    var item = data[i];
                    item.width = (data[i].wavelength - data[i - 1].wavelength) * 0.5f;
                    data[i] = item;
                }
                else
                {
                    var item = data[i];
                    item.width = (data[i + 1].wavelength - data[i - 1].wavelength) * 0.5f;
                    data[i] = item;
                }
            }
            
            // Record min/max/average value.
            minValue = float.MaxValue;
            maxValue = float.MinValue;
            averageValue = 0.0f;
            foreach (var item in data)
            {
                minValue = Mathf.Min(minValue, item.value);
                maxValue = Mathf.Max(maxValue, item.value);
                averageValue += item.value;
            }
            
            averageValue /= data.Count;
        }

        public void ImportFromCSV(TextAsset asset)
        {
            data = new();
            using (MemoryStream stream = new MemoryStream ())
            {
                var writer = new StreamWriter(stream);
                writer.Write(asset.text);
                writer.Flush();
                stream.Position = 0;
                var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        break;
                    var splited = line.Split(',');
                    var wavelength = float.Parse(splited[0]);
                    var irradiance = float.Parse(splited[1]);

                    data.Add(new Entry()
                    {
                        wavelength = wavelength, value = irradiance
                    });
                }
            }

            PostProcessData(data);
        }
        #endif
        
        [System.Serializable]
        public struct Entry
        {
            // Wavelength of the data.
            public float wavelength;
            // Associated of the data.
            public float value;
            // Width of the data.
            public float width;
        }

        /// <summary>
        /// Ordered data, starts at small wavelength.
        /// </summary>
        [SerializeField]
        private List<Entry> data;

        [SerializeField] private float minValue;
        public float MinValue => minValue;
        [SerializeField] private float maxValue;
        public float MaxValue => maxValue;
        [SerializeField] private float averageValue;
        public float AverageValue => averageValue;

        public IReadOnlyList<Entry> Data => data;

        int FindWavelengthItemLowerBound(int first, int last, float wavelength)
        {
            int it;
            int count, step;
            count = last - first;
            while ( count > 0 )
            {
                it = first; step=count/2;
                it += step;
                if (data[it].wavelength < wavelength) {
                    first=++it;
                    count-=step+1;
                }
                else count=step;
            }
            return first;
        }

        int FindWavelengthItemUpperBound(int first, int last, float wavelength)
        {
            int it;
            int count, step;
            count = last - first;
            while ( count > 0 )
            {
                it = first; step=count/2;
                it += step;
                if (data[it].wavelength <= wavelength) {
                    first=++it;
                    count-=step+1;
                }
                else count=step;
            }
            return first;
        }
        
        /// <summary>
        /// Calculate interpolated value at given wavelength.
        /// </summary>
        /// <param name="wavelength"></param>
        /// <returns></returns>
        public float CalculateValueAtWavelength(float wavelength)
        {
            // Find the item that is less than or equal to the wavelength.
            var lower = FindWavelengthItemLowerBound(0, data.Count, wavelength);
            
            // For out-of-bound value, return the nearest value.
            if (lower == data.Count)
                return data[data.Count - 1].value;
            if (lower == 0)
                return data[0].value;
            
            var upper = lower;
            lower--;
            var lowerItem = data[lower];
            var upperItem = data[upper];
            var t = (wavelength - lowerItem.wavelength) / (upperItem.wavelength - lowerItem.wavelength);
            return Mathf.Lerp(lowerItem.value, upperItem.value, t);
        }
        
        /// <summary>
        /// Give a center wavelength and dw, calculate integral of [wavelength - 0.5 * dw, wavelength + 0.5 * dw]
        /// </summary>
        /// <param name="center"></param>
        /// <param name="dw"></param>
        /// <returns></returns>
        public float CalculateValueAtWavelength(float center, float dw)
        {
            // This is a iterative process, starting at x = center - 0.5f * dw, end at x = center + 0.5f * dw
            // For each iteration, step onto next element(Could be in the list, or the end), calculate integral between [x, x+1].
            
            // Evaluate value at start.
            var iterateWavelength = center - dw * 0.5f;
            var end = center + dw * 0.5f;
            var prevValue = CalculateValueAtWavelength(iterateWavelength);
            int iterateIndex = FindWavelengthItemLowerBound(0, data.Count, iterateWavelength) + 1;
            var endValue = CalculateValueAtWavelength(end);

            float Result = 0.0f;

            while (true)
            {
                if (iterateIndex >= data.Count || data[iterateIndex].wavelength > end)
                {
                    // Calculate between end.
                    var t = end - iterateWavelength;
                    Result += (prevValue + endValue) * t * 0.5f;
                    break;
                }
                else
                {
                    // Calculate between nextIndex.
                    var nextValue = data[iterateIndex].value;
                    var nextWavelength = data[iterateIndex].wavelength;
                    var t = nextWavelength - iterateWavelength;
                    var value = (prevValue + nextValue) * t * 0.5f;
                    Result += value;
                    
                    prevValue = nextValue;
                    iterateWavelength = nextWavelength;
                    iterateIndex++;
                }
            }

            return Result / dw;
        }
    }
}