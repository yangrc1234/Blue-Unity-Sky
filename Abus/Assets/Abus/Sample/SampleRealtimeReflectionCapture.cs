using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class SampleRealtimeReflectionCapture : MonoBehaviour
{
    private void Update()
    {
        var probe = GetComponent<ReflectionProbe>();
        if (probe)
            probe.RenderProbe();
        
    }
}
