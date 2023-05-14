using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class VideoMonitor : MonoBehaviour
{
	[SerializeField] ARCameraManager arCameraManager;
	[SerializeField] AROcclusionManager arOcclusionManager;

	[SerializeField] public RawImage camImage;
    [SerializeField] public RawImage originalDepthView;
    [SerializeField] public RawImage grayDepthView;
    [SerializeField] public RawImage confidenceView;

    [SerializeField] float near;
    [SerializeField] float far;

    Texture2D cameraTexture;
    Texture2D depthTextureFloat;
    Texture2D depthTextureBGRA;
    Texture2D depthConfidenceR8;
    Texture2D depthConfidenceRGBA;

	private void OnEnable()
	{
		if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnCameraFrameReceived;
        }
	}

	private void OnDisable()
	{
		if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
        }
	}

    void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        UpdateCameraImage();
        UpdateEnvironmentDepthImage();
        UpdateEnvironmentConfidenceImage();
    }

    void UpdateCameraImage()
    {
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            Debug.Log("Could not get the latest image from the CPU");
            return;
        }

        using (image)
        {
            // Choose an RGBA format
            // See XRCpuImage.Format for a complete list
            TextureFormat format = TextureFormat.RGBA32;
            
            if (cameraTexture == null || cameraTexture.width != image.width || cameraTexture.height != image.height)
            {
                cameraTexture = new Texture2D(image.width, image.height, format, false);
            }

            UpdateRawImage(cameraTexture, image, format);

            // Set the RawImage's texture so we can visualize it
            camImage.texture = cameraTexture;
        }
    }

    void UpdateEnvironmentDepthImage()
    {
        if (!arOcclusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage image))
        {
            Debug.Log("Could not get an environment depth image from the cpu");
            return;
        }

        using (image)
        {
            if (depthTextureFloat == null || depthTextureFloat.width != image.width || depthTextureFloat.height != image.height)
            {
                depthTextureFloat = new Texture2D(image.width, image.height, image.format.AsTextureFormat(), false);
            }
            if (depthTextureBGRA == null || depthTextureBGRA.width != image.width || depthTextureBGRA.height != image.height)
            {
                depthTextureBGRA = new Texture2D(image.width, image.height, TextureFormat.BGRA32, false);
            }

            // Acquire Depth Image (RFloat format). Depth pixels are store with meter units.
            UpdateRawImage(depthTextureFloat, image, image.format.AsTextureFormat());

            // Visualize 0~1m depth.
            originalDepthView.texture = depthTextureFloat;

            //Convert RFloat into grayscale image between near and far clip area
            ConvertFloatToGrayScale(depthTextureFloat, depthTextureBGRA);

            // Visualize near-far depth
            grayDepthView.texture = depthTextureBGRA;
        }
    }

    void UpdateEnvironmentConfidenceImage()
    {
        if (!arOcclusionManager.TryAcquireEnvironmentDepthConfidenceCpuImage(out XRCpuImage image))
        {
            Debug.Log("Could not get an environment depth confidence image from the cpu");
            return;
        }

        using (image)
        {
            if (depthConfidenceR8 == null || depthConfidenceR8.width != image.width || depthConfidenceR8.height != image.height)
            {
                depthConfidenceR8 = new Texture2D(image.width, image.height, image.format.AsTextureFormat(), false);
            }
            if (depthConfidenceRGBA == null || depthConfidenceRGBA.width != image.width || depthConfidenceRGBA.height != image.height)
            {
                depthConfidenceRGBA = new Texture2D(image.width, image.height, TextureFormat.BGRA32, false);
            }

            UpdateRawImage(depthConfidenceR8, image, image.format.AsTextureFormat());
            ConvertR8ToConfidenceMap(depthConfidenceR8, depthConfidenceRGBA);

            confidenceView.texture = depthConfidenceRGBA;
        }
    }

    unsafe void UpdateRawImage(Texture2D texture, XRCpuImage cpuImage, TextureFormat format)
    {
        XRCpuImage.ConversionParams conversionParams = new XRCpuImage.ConversionParams(cpuImage, format, XRCpuImage.Transformation.MirrorY);
        NativeArray<byte> rawTextureData = texture.GetRawTextureData<byte>();

        Debug.Assert(rawTextureData.Length == cpuImage.GetConvertedDataSize(conversionParams.outputDimensions, conversionParams.outputFormat),
            "The Texture2D is not the same size as the converted data.");

        // Perform the conversion
        cpuImage.Convert(conversionParams, rawTextureData);
        texture.Apply();
    }

    void ConvertFloatToGrayScale(Texture2D txFloat, Texture2D txGray)
    {
        int length = txGray.width * txGray.height;
        Color[] depthPixels = txFloat.GetPixels();
        Color[] colorPixels = txGray.GetPixels();

        for (int i=0; i<length; i++)
        {
            float value = (depthPixels[i].r - near) / (far - near);

            colorPixels[i].r = value;
			colorPixels[i].g = value;
			colorPixels[i].b = value;
			colorPixels[i].a = 1;
		}
        txGray.SetPixels(colorPixels);
        txGray.Apply();
    }

    void ConvertR8ToConfidenceMap(Texture2D txR8, Texture2D txRGBA)
    {
        Color32[] r8 =  txR8.GetPixels32();
        Color32[] rgba = txRGBA.GetPixels32();

        for (int i=0; i<r8.Length; i++)
        {
            switch (r8[i].r)
            {
                case 0:
                    rgba[i].r = 255;
                    rgba[i].g = 0;
					rgba[i].b = 0;
					rgba[i].a = 255;
                    break;
				case 1:
					rgba[i].r = 0;
					rgba[i].g = 255;
					rgba[i].b = 0;
					rgba[i].a = 255;
					break;
				case 2:
					rgba[i].r = 0;
					rgba[i].g = 0;
					rgba[i].b = 255;
					rgba[i].a = 255;
					break;
			}
        }
        txRGBA.SetPixels32(rgba);
        txRGBA.Apply();
    }
}
