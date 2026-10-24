using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClubPenguin;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Regenerate the scenery when Town art changes; no zone scripts enter the prefab.</summary>
[InitializeOnLoad]
internal static class PCVRMenuEnvironmentBuilder
{
	private const string OutputPath = "Assets/Resources/PCVR/TownMenuEnvironment.prefab";
	private const string RequestPath = "Library/CPI_MENU_ENVIRONMENT_REQUEST.txt";
	private const string ResultPath = "Library/CPI_MENU_ENVIRONMENT_RESULT.txt";

	static PCVRMenuEnvironmentBuilder()
	{
		EditorApplication.update += CheckRequest;
	}

	private static void CheckRequest()
	{
		if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
		string request = File.ReadAllText(RequestPath).Trim();
		File.Delete(RequestPath);
		try { if (request == "preview") Preview(); else Rebuild(); }
		catch (Exception exception) { File.WriteAllText(ResultPath, exception.ToString()); Debug.LogException(exception); }
	}

	[MenuItem("CPI VR/Rebuild Town Menu Scenery")]
	public static void Rebuild()
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before rebuilding menu scenery.");
		Scene previousScene = SceneManager.GetActiveScene();
		Scene town = SceneManager.GetSceneByPath("Assets/Game/World/Scenes/Town.unity");
		bool openedTown = !town.isLoaded;
		GameObject root = null;
		try
		{
			if (openedTown) town = EditorSceneManager.OpenScene("Assets/Game/World/Scenes/Town.unity", OpenSceneMode.Additive);
			SceneManager.SetActiveScene(town);
			GameObject[] sourceRoots = town.GetRootGameObjects();
			var sourceMeshes = sourceRoots.SelectMany(item => item.GetComponentsInChildren<MeshRenderer>()).Where(item => item.enabled && item.gameObject.activeInHierarchy).ToArray();
			root = new GameObject("Town Menu Environment");
			var environment = root.AddComponent<PCVRMenuEnvironment>();
			// Open paving beside the central fountain, clear of the menu panel.
			environment.plazaOrigin = new Vector3(-0.5f, 0.9f, -22f);
			environment.skybox = RenderSettings.skybox;
			environment.ambientColor = RenderSettings.ambientLight;
			environment.lightmapsMode = LightmapSettings.lightmapsMode;
			LightmapData[] sceneMaps = LightmapSettings.lightmaps;
			// Additively opened scenes share an index space. Export only Town's maps.
			int[] usedMapIndices = sourceMeshes.Select(item => item.lightmapIndex)
				.Where(index => index >= 0 && index < sceneMaps.Length).Distinct().OrderBy(index => index).ToArray();
			LightmapData[] maps = usedMapIndices.Select(index => sceneMaps[index]).ToArray();
			environment.lightmapColors = maps.Select(map => map.lightmapColor).ToArray();
			environment.lightmapDirections = maps.Select(map => map.lightmapDir).ToArray();
			environment.shadowMasks = maps.Select(map => map.shadowMask).ToArray();
			var renderers = new List<Renderer>();
			var indices = new List<int>();
			var offsets = new List<Vector4>();
			foreach (MeshRenderer source in sourceMeshes)
			{
				MeshFilter filter = source.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null) continue;
				var child = new GameObject(source.name);
				// Home's overlay camera renders Default/UI, but excludes this world layer.
				child.layer = LayerMask.NameToLayer("TerrainBarrier");
				child.transform.SetParent(root.transform, false);
				child.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
				child.transform.localScale = source.transform.lossyScale;
				child.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
				var renderer = child.AddComponent<MeshRenderer>();
				renderer.sharedMaterials = source.sharedMaterials;
				renderer.shadowCastingMode = source.shadowCastingMode;
				renderer.receiveShadows = source.receiveShadows;
				int bakedIndex = Array.IndexOf(usedMapIndices, source.lightmapIndex);
				if (bakedIndex >= 0)
				{
					renderers.Add(renderer);
					indices.Add(bakedIndex);
					offsets.Add(source.lightmapScaleOffset);
				}
			}
			foreach (Light source in sourceRoots.SelectMany(item => item.GetComponentsInChildren<Light>()).Where(item => item.enabled && item.gameObject.activeInHierarchy && item.lightmapBakeType != LightmapBakeType.Baked))
			{
				var child = new GameObject(source.name);
				child.transform.SetParent(root.transform, false);
				child.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
				EditorUtility.CopySerialized(source, child.AddComponent<Light>());
			}
			environment.bakedRenderers = renderers.ToArray();
			environment.lightmapIndices = indices.ToArray();
			environment.lightmapOffsets = offsets.ToArray();
			Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
			PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
			File.WriteAllText(ResultPath, $"Succeeded: {root.transform.childCount} scenery objects; {maps.Length} lightmaps; {renderers.Count} baked renderers.\n");
			Debug.Log("CPI PCVR: rebuilt Town menu scenery.");
		}
		finally
		{
			if (root != null) UnityEngine.Object.DestroyImmediate(root);
			SceneManager.SetActiveScene(previousScene);
			if (openedTown && town.isLoaded) EditorSceneManager.CloseScene(town, true);
		}
	}

	[MenuItem("CPI VR/Validate Town Menu Scenery")]
	public static void Preview()
	{
		Scene previous = SceneManager.GetActiveScene();
		Scene preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
		SceneManager.SetActiveScene(preview);
		GameObject instance = null;
		var texture = new RenderTexture(960, 720, 24);
		var pixels = new Texture2D(960, 720, TextureFormat.RGB24, false);
		RenderTexture previousTarget = RenderTexture.active;
		try
		{
			instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OutputPath), preview);
			var environment = instance.GetComponent<PCVRMenuEnvironment>();
			if (instance.GetComponentsInChildren<MonoBehaviour>().Length != 1 || instance.GetComponentsInChildren<Collider>().Length != 0 || instance.GetComponentsInChildren<Camera>().Length != 0)
				throw new InvalidOperationException("Scenery must not include gameplay scripts, colliders or cameras.");
			var camera = new GameObject("Preview Camera").AddComponent<Camera>();
			camera.enabled = false;
			camera.fieldOfView = 85f;
			camera.nearClipPlane = 0.005f;
			camera.farClipPlane = 500f;
			camera.targetTexture = texture;
			LightmapData[] originalMaps = LightmapSettings.lightmaps;
			var originalAmbientMode = RenderSettings.ambientMode;
			Color originalAmbient = RenderSettings.ambientLight;
			bool originalFog = RenderSettings.fog;
			CameraClearFlags originalClearFlags = camera.clearFlags;
			environment.PlaceAround(camera, Vector3.zero, Quaternion.identity);
			if (Vector3.Distance(environment.transform.TransformPoint(environment.plazaOrigin), Vector3.zero) > 0.001f)
				throw new InvalidOperationException("Town plaza does not align with the menu tracking origin.");
			if (instance.GetComponentsInChildren<MeshRenderer>().Any(renderer => (289 & (1 << renderer.gameObject.layer)) != 0))
				throw new InvalidOperationException("Home's UI camera must not render the town scenery.");
			camera.transform.position = new Vector3(0f, 1.5f, 0f);
			for (int direction = 0; direction < 4; direction++)
			{
				camera.transform.rotation = Quaternion.Euler(0f, direction * 90f, 0f);
				camera.Render();
				RenderTexture.active = texture;
				pixels.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
				pixels.Apply();
				File.WriteAllBytes($"Library/CPI_MENU_PREVIEW_{direction}.png", pixels.EncodeToPNG());
			}
			environment.RestoreLighting();
			if (LightmapSettings.lightmaps.Length != originalMaps.Length || RenderSettings.ambientMode != originalAmbientMode || RenderSettings.ambientLight != originalAmbient || RenderSettings.fog != originalFog || camera.clearFlags != originalClearFlags)
				throw new InvalidOperationException("Menu scenery did not restore camera and lighting settings.");
			UnityEngine.Object.DestroyImmediate(instance);
			instance = null;
			File.WriteAllText(ResultPath, "Validated: scenery-only prefab; plaza alignment; UI-camera isolation; four rendered plaza views; camera and lighting restored on teardown.\n");
		}
		finally
		{
			if (instance != null)
			{
				instance.GetComponent<PCVRMenuEnvironment>().RestoreLighting();
				UnityEngine.Object.DestroyImmediate(instance);
			}
			RenderTexture.active = previousTarget;
			UnityEngine.Object.DestroyImmediate(texture);
			UnityEngine.Object.DestroyImmediate(pixels);
			SceneManager.SetActiveScene(previous);
			EditorSceneManager.CloseScene(preview, true);
		}
	}
}
