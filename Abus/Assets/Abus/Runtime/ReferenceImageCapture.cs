using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    /// <summary>
    /// For taking screenshots, and make image comparison during profiling.
    /// 
    /// </summary>
    public class ReferenceImageCapture : MonoBehaviour
    {
        public Transform[] cameraPoses;

        [System.Serializable]
        public class CaptureSettings
        {
            public Transform SunPose;
        }

        public CaptureSettings[] ImageSettings;

        [SerializeField] int width = 1024;
        [SerializeField] int height = 512;
        [SerializeField] public string folder = "Screenshots";
        public float diffMultiplier = 10.0f;
    
        // Place at same level with Assets/Library etc.
        public string AbsoluteFolderPath =>  System.IO.Directory.GetCurrentDirectory() + "/" + folder + "/";

        // B Group is testing image, A is reference image.
        public bool CaptureToTestGroup = false;

        public void RunDifferencePass()
        {
            // Read images in A and B, for each pair of same name images, outputs a image of difference.
            string dirA = AbsoluteFolderPath + "/Reference/";
            string dirB = AbsoluteFolderPath + "/Test/";
            string dirDiff = AbsoluteFolderPath + "/Diff/";
            Directory.CreateDirectory(dirDiff);
        
            string[] filesA = Directory.GetFiles(dirA);
            string[] filesB = Directory.GetFiles(dirB);
            
        
            for (int i = 0; i < filesA.Length; i++)
            {
                string fileA = filesA[i];
                string fileB = filesB[i];
                string fileDiff = dirDiff + Path.GetFileName(fileA);
            
                Texture2D texA = new Texture2D(2, 2);
                texA.LoadImage(File.ReadAllBytes(fileA));
                Texture2D texB = new Texture2D(2, 2);
                texB.LoadImage(File.ReadAllBytes(fileB));
            
                if (texA.width != texB.width || texA.height != texB.height)
                {
                    Debug.LogError("Image size mismatch: " + fileA + " " + fileB);
                    continue;
                }

                Color[] pixelsA = texA.GetPixels();
                Color[] pixelsB = texB.GetPixels();
                Color[] pixelsDiff = new Color[pixelsA.Length];
                for (int p = 0; p < pixelsA.Length; p++)
                {
                    var colorDiff = diffMultiplier * (pixelsA[p] - pixelsB[p]);
                    // Take absolute value. 
                    colorDiff.r = Mathf.Abs(colorDiff.r);
                    colorDiff.g = Mathf.Abs(colorDiff.g);
                    colorDiff.b = Mathf.Abs(colorDiff.b);
                    colorDiff.a = 1.0f;
                    pixelsDiff[p] = colorDiff;
                }

                Texture2D texDiff = new Texture2D(texA.width, texA.height);
                texDiff.SetPixels(pixelsDiff);
                texDiff.Apply();
            
                byte[] png = texDiff.EncodeToPNG();
                File.WriteAllBytes(fileDiff, png);
            }
        }

        public void PrepareCapture()
        {
            // Find abus core.  
            AbusCore abusCore = FindObjectOfType<AbusCore>();
            if (abusCore == null)
                return;

            foreach (var imageSetting in ImageSettings)
            {
                abusCore.boundLight.transform.rotation = imageSetting.SunPose.rotation;
                break;
            }
        }

        public void Execute()
        {
            if (!CaptureToTestGroup)
            {
                // Show a confirm dialogue.  
                if (!UnityEditor.EditorUtility.DisplayDialog("Warning", "Create new reference images?", "Yes", "No"))
                    return;
            }
            
            // Create or get camera gameobject.
            GameObject camObj = GameObject.Find("ScreenshotCamera");
            Camera cam = camObj ? camObj.GetComponent<Camera>() : new GameObject("ScreenshotCamera").AddComponent<Camera>();
            cam.transform.SetParent(this.transform, true);

            int imageIndex = 0;
        
            // Find abus core.  
            AbusCore abusCore = FindObjectOfType<AbusCore>();
            if (abusCore == null)
                return;

            foreach (var imageSetting in ImageSettings)
            {
                abusCore.boundLight.transform.rotation = imageSetting.SunPose.rotation;

                abusCore.GetComponent<AbusLutUpdater>().UpdateLuts(true);
                abusCore.GetComponent<AbusSceneLighting>().UpdateLighting();
            
                foreach (var pose in cameraPoses)
                {
                    cam.transform.position = pose.position;
                    cam.transform.rotation = pose.rotation;
                    TakeScreenshot(cam, imageIndex++);
                }
            }

            PrepareCapture();   // Reset to initial state.
        }

        public void TakeScreenshot(Camera cam, int imageIndex)
        {
            string dir = AbsoluteFolderPath + (CaptureToTestGroup ? "Test/" : "Reference/");
            string filename = imageIndex.ToString() + ".png";
            string path = dir + filename;

            // Create Render Texture with width and height.
            RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat);
        
            // Assign Render Texture to camera.
            cam.clearFlags = CameraClearFlags.Skybox;
        
            // Render the camera's view to the Target Texture.
            cam.Render();
            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest()
            {
                 destination = rt
            });

            // ReadPixels reads from the active Render Texture.
            RenderTexture.active = rt;

            // Make a new texture and read the active Render Texture into it.
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.ARGB32, false);
            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);

            // Save the screnshot.
            Directory.CreateDirectory(dir);
            byte[] png = screenshot.EncodeToPNG();
            File.WriteAllBytes(path, png);

            Debug.Log("Screenshot saved to: " + path);
        }
    }
}
