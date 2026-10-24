using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ClubPenguin
{
	/// <summary>Town scenery and baked lighting, without the zone's gameplay services.</summary>
	public sealed class PCVRMenuEnvironment : MonoBehaviour
	{
		public Vector3 plazaOrigin;
		public float plazaYaw = 270f;
		public Material skybox;
		public Color ambientColor = Color.white;
		public LightmapsMode lightmapsMode;
		public Texture2D[] lightmapColors;
		public Texture2D[] lightmapDirections;
		public Texture2D[] shadowMasks;
		public Renderer[] bakedRenderers;
		public int[] lightmapIndices;
		public Vector4[] lightmapOffsets;
		private LightmapData[] previousLightmaps;
		private LightmapsMode previousLightmapsMode;
		private AmbientMode previousAmbientMode;
		private Color previousAmbientColor;
		private bool previousFog;
		private Camera viewCamera;
		private CameraClearFlags previousClearFlags;
		private Skybox cameraSkybox;
		private bool applied;

		public void PlaceAround(Camera camera, Vector3 trackingOrigin, Quaternion trackingRotation)
		{
			Quaternion sceneryRotation = trackingRotation * Quaternion.Euler(0f, -plazaYaw, 0f);
			transform.SetPositionAndRotation(trackingOrigin - sceneryRotation * plazaOrigin, sceneryRotation);
			viewCamera = camera;
			previousClearFlags = camera.clearFlags;
			camera.clearFlags = CameraClearFlags.Skybox;
			cameraSkybox = camera.gameObject.AddComponent<Skybox>();
			cameraSkybox.material = skybox;
			previousAmbientMode = RenderSettings.ambientMode;
			previousAmbientColor = RenderSettings.ambientLight;
			previousFog = RenderSettings.fog;
			RenderSettings.ambientMode = AmbientMode.Flat;
			RenderSettings.ambientLight = ambientColor;
			RenderSettings.fog = false;
			previousLightmaps = LightmapSettings.lightmaps;
			previousLightmapsMode = LightmapSettings.lightmapsMode;
			var combined = new LightmapData[previousLightmaps.Length + lightmapColors.Length];
			Array.Copy(previousLightmaps, combined, previousLightmaps.Length);
			for (int i = 0; i < lightmapColors.Length; i++)
			{
				combined[previousLightmaps.Length + i] = new LightmapData
				{
					lightmapColor = lightmapColors[i],
					lightmapDir = lightmapDirections[i],
					shadowMask = shadowMasks[i]
				};
			}
			LightmapSettings.lightmapsMode = lightmapsMode;
			LightmapSettings.lightmaps = combined;
			for (int i = 0; i < bakedRenderers.Length; i++)
			{
				bakedRenderers[i].lightmapIndex = previousLightmaps.Length + lightmapIndices[i];
				bakedRenderers[i].lightmapScaleOffset = lightmapOffsets[i];
			}
			applied = true;
		}

		public void RestoreLighting()
		{
			if (!applied) return;
			applied = false;
			if (viewCamera != null) viewCamera.clearFlags = previousClearFlags;
			if (cameraSkybox != null)
			{
				if (Application.isPlaying) Destroy(cameraSkybox);
				else DestroyImmediate(cameraSkybox);
			}
			// A new scene owns its own lighting; only restore when still in Home.
			if (SceneManager.GetActiveScene() == gameObject.scene)
			{
				LightmapSettings.lightmaps = previousLightmaps;
				LightmapSettings.lightmapsMode = previousLightmapsMode;
				RenderSettings.ambientMode = previousAmbientMode;
				RenderSettings.ambientLight = previousAmbientColor;
				RenderSettings.fog = previousFog;
			}
		}

		private void OnDestroy()
		{
			RestoreLighting();
		}
	}
}
