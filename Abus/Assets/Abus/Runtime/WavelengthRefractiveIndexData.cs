using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    [CreateAssetMenu(menuName = "Abus/Wavelength Refractive Index Data")]
    public class WavelengthRefractiveIndexData : ScriptableObject
    {
        #if UNITY_EDITOR
        [TextArea]
        public string dataComment;
        public TextAsset sourceCsv;
        public float wavelengthImportScale = 1.0f;
        
        void PostProcessData(List<Entry> data)
        {
            // Process data scale.
            for (int i = 0; i < data.Count; i++)
            {
                var item = data[i];
                item.wavelength *= wavelengthImportScale;
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
                    var real = float.Parse(splited[1]);
                    var imag = float.Parse(splited[2]);

                    data.Add(new Entry()
                    {
                        wavelength = wavelength, real = real, imag = imag
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
            public float real;
            public float imag;
            // Width of the data.
            public float width;
        }

        /// <summary>
        /// Ordered data, starts at small wavelength.
        /// </summary>
        [SerializeField]
        private List<Entry> data;

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
        public RefractiveIndex CalculateValueAtWavelength(float wavelength)
        {
            // Find the item that is less than or equal to the wavelength.
            var lower = FindWavelengthItemLowerBound(0, data.Count, wavelength);
            
            // For out-of-bound value, return the nearest value.
            if (lower == data.Count)
                return new RefractiveIndex(){ real = data[data.Count - 1].real, imag =  data[data.Count - 1].imag};
            if (lower == 0)
                return  new RefractiveIndex(){ real = data[0].real, imag =  data[0].imag};
            
            var upper = lower;
            lower--;
            var lowerItem = data[lower];
            var upperItem = data[upper];
            var t = (wavelength - lowerItem.wavelength) / (upperItem.wavelength - lowerItem.wavelength);
            return new RefractiveIndex()
            {
                real = Mathf.Lerp(lowerItem.real, upperItem.real, t),
                imag = Mathf.Lerp(lowerItem.imag, upperItem.imag, t)
            };
        }

        RefractiveIndex Lerp(RefractiveIndex a, RefractiveIndex b, float t)
        {
            return new RefractiveIndex()
            {
                real = Mathf.Lerp(a.real, b.real, t),
                imag = Mathf.Lerp(a.imag, b.imag, t)
            };
        }
        
        /// <summary>
        /// Give a center wavelength and dw, calculate integral of [wavelength - 0.5 * dw, wavelength + 0.5 * dw]
        /// </summary>
        /// <param name="center"></param>
        /// <param name="dw"></param>
        /// <returns></returns>
        public RefractiveIndex CalculateValueAtWavelength(float center, float dw)
        {
            // This is a iterative process, starting at x = center - 0.5f * dw, end at x = center + 0.5f * dw
            // For each iteration, step onto next element(Could be in the list, or the end), calculate integral between [x, x+1].
            
            // Evaluate value at start.
            var iterateWavelength = center - dw * 0.5f;
            var end = center + dw * 0.5f;
            var prevValue = CalculateValueAtWavelength(iterateWavelength);
            int iterateIndex = FindWavelengthItemLowerBound(0, data.Count, iterateWavelength) + 1;
            var endValue = CalculateValueAtWavelength(end);

            float ResultReal = 0.0f;
            float ResultImag = 0.0f;

            while (true)
            {
                if (iterateIndex >= data.Count || data[iterateIndex].wavelength > end)
                {
                    // Calculate between end.
                    var t = end - iterateWavelength;
                    ResultReal += (prevValue.real + endValue.real) * 0.5f * t;
                    ResultImag += (prevValue.imag + endValue.imag) * 0.5f * t;
                    break;
                }
                else
                {
                    // Calculate between nextIndex.
                    var nextValue = data[iterateIndex];
                    var nextWavelength = data[iterateIndex].wavelength;
                    var t = nextWavelength - iterateWavelength;
                    ResultReal += (prevValue.real + nextValue.real) * 0.5f * t;
                    ResultImag += (prevValue.imag + nextValue.imag) * 0.5f * t;
                    
                    prevValue.real = nextValue.real;
                    prevValue.imag = nextValue.imag;
                    iterateWavelength = nextWavelength;
                    iterateIndex++;
                }
            }

            return new RefractiveIndex()
            {
                real = ResultReal / dw,
                imag = ResultImag / dw,
            };
        }
    }
}