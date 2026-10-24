using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

/// <summary>Builds a Windows player when a request marker is written under Library.</summary>
[InitializeOnLoad]
internal static class CPIWindowsBuildAutomation
{
	private static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
	private static readonly string RequestPath = Path.Combine(ProjectRoot, "Library", "CPI_WINDOWS_BUILD_REQUEST.txt");
	private static readonly string ResultPath = Path.Combine(ProjectRoot, "Library", "CPI_WINDOWS_BUILD_RESULT.txt");
	private static readonly string EditorPlayRequestPath = Path.Combine(ProjectRoot, "Library", "CPI_WINDOWS_EDITOR_PLAY_REQUEST.txt");
	private const string BootScenePath = "Assets/Game/Core/Scenes/Boot.unity";
	private const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";
	private static bool buildInProgress;

	static CPIWindowsBuildAutomation()
	{
		EditorApplication.update -= CheckForBuildRequest;
		EditorApplication.update += CheckForBuildRequest;
		if (File.Exists(EditorPlayRequestPath))
		{
			EditorApplication.delayCall += ResumeEditorPcvrPlayMode;
		}
	}

	[MenuItem("CPI VR/Play PCVR in Editor %#P", false, 1)]
	private static void StartEditorPcvrPlayMode()
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
		{
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(EditorPlayRequestPath));
		File.WriteAllText(EditorPlayRequestPath, "pcvr");
		ResumeEditorPcvrPlayMode();
	}

	private static void ResumeEditorPcvrPlayMode()
	{
		if (!File.Exists(EditorPlayRequestPath) || EditorApplication.isPlayingOrWillChangePlaymode)
		{
			return;
		}

		if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
		{
			if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
			{
				File.Delete(EditorPlayRequestPath);
				Debug.LogError("CPI VR: Unity could not switch to Windows 64-bit for OpenXR Editor play mode.");
			}
			return;
		}

		try
		{
			ConfigureWindowsOpenXR();
			if (EditorSceneManager.GetActiveScene().path != BootScenePath)
			{
				if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
				{
					File.Delete(EditorPlayRequestPath);
					return;
				}
				EditorSceneManager.OpenScene(BootScenePath, OpenSceneMode.Single);
			}

			File.Delete(EditorPlayRequestPath);
			EditorApplication.isPlaying = true;
			Debug.Log("CPI VR: entering Boot scene in PCVR Editor play mode. Script changes can be tested without rebuilding the Windows player.");
		}
		catch (Exception exception)
		{
			File.Delete(EditorPlayRequestPath);
			Debug.LogException(exception);
		}
	}

	[MenuItem("CPI VR/Enable Windows OpenXR")]
	private static void EnableWindowsOpenXR()
	{
		ConfigureWindowsOpenXR();
		Debug.Log("CPI VR: Windows OpenXR loader and common controller profiles are enabled.");
	}

	public static void BuildWindowsPcvrFromCommandLine()
	{
		if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
		{
			throw new InvalidOperationException("Run Unity with -buildTarget Win64 before building the PCVR player.");
		}

		BuildWindowsPlayer("pcvr");
	}

	private static void CheckForBuildRequest()
	{
		if (buildInProgress || !File.Exists(RequestPath))
		{
			return;
		}

		string mode = File.ReadAllText(RequestPath).Trim();
		File.Delete(RequestPath);
		buildInProgress = true;
		EditorApplication.delayCall += () => BuildWindowsPlayer(mode);
	}

	private static void BuildWindowsPlayer(string mode)
	{
		try
		{
			File.WriteAllText(ResultPath, $"Mode: {mode}\nResult: Running\nStage: Starting\n");
			if (string.Equals(mode, "pcvr", StringComparison.OrdinalIgnoreCase))
			{
				File.WriteAllText(ResultPath, $"Mode: {mode}\nResult: Running\nStage: Configuring OpenXR\n");
				ConfigureWindowsOpenXR();
			}

			if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
			{
				// Target changes can reload the editor domain, so leave the request in place
				// for InitializeOnLoad to resume the build after Unity finishes switching.
				File.WriteAllText(RequestPath, mode);
				if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
				{
					File.Delete(RequestPath);
					throw new InvalidOperationException("Unity could not switch to the Windows 64-bit build target.");
				}
				return;
			}

			File.WriteAllText(ResultPath, $"Mode: {mode}\nResult: Running\nStage: Building Windows player\n");
			string[] scenes = EditorBuildSettings.scenes
				.Where(scene => scene.enabled && File.Exists(scene.path))
				.Select(scene => scene.path)
				.ToArray();
			if (scenes.Length == 0)
			{
				throw new InvalidOperationException("No enabled build scenes were found.");
			}

			string output = Path.Combine(ProjectRoot, "Builds", mode == "pcvr" ? "WindowsPCVR" : "WindowsNormal", "CP Island.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(output));
			BuildPlayerOptions options = new BuildPlayerOptions
			{
				scenes = scenes,
				locationPathName = output,
				target = BuildTarget.StandaloneWindows64,
				options = BuildOptions.None
			};

			BuildReport report = BuildPipeline.BuildPlayer(options);
			BuildSummary summary = report.summary;
			File.WriteAllText(ResultPath,
				$"Mode: {mode}\nResult: {summary.result}\nOutput: {summary.outputPath}\nSize: {summary.totalSize}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\nDuration: {summary.totalTime}\n");
			Debug.Log($"CPI Windows build finished: {summary.result}. {summary.totalErrors} errors, {summary.totalWarnings} warnings. Output: {summary.outputPath}");
		}
		catch (Exception exception)
		{
			File.WriteAllText(ResultPath, $"Mode: {mode}\nResult: Failed\n{exception}");
			Debug.LogException(exception);
		}
		finally
		{
			buildInProgress = false;
		}
	}

	private static void ConfigureWindowsOpenXR()
	{
		const BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
		XRGeneralSettingsPerBuildTarget buildTargetSettings = FindOrCreateBuildTargetSettings();
		if (!buildTargetSettings.HasSettingsForBuildTarget(targetGroup))
		{
			buildTargetSettings.CreateDefaultSettingsForBuildTarget(targetGroup);
		}
		if (!buildTargetSettings.HasManagerSettingsForBuildTarget(targetGroup))
		{
			buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(targetGroup);
		}

		XRGeneralSettings generalSettings = buildTargetSettings.SettingsForBuildTarget(targetGroup);
		generalSettings.InitManagerOnStart = true;
		if (!XRPackageMetadataStore.AssignLoader(generalSettings.Manager, OpenXRLoaderTypeName, targetGroup))
		{
			throw new InvalidOperationException("Unity could not assign the OpenXR loader for Windows.");
		}

		OpenXRSettings openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
		if (openXRSettings == null)
		{
			throw new InvalidOperationException("OpenXR settings are missing for the Windows build target.");
		}

		string[] controllerProfiles = { "KHRSimpleControllerProfile", "OculusTouchControllerProfile", "ValveIndexControllerProfile", "HTCViveControllerProfile" };
		foreach (OpenXRFeature feature in openXRSettings.GetFeatures())
		{
			if (controllerProfiles.Contains(feature.GetType().Name))
			{
				feature.enabled = true;
				EditorUtility.SetDirty(feature);
			}
		}

		EditorUtility.SetDirty(generalSettings);
		EditorUtility.SetDirty(generalSettings.Manager);
		EditorUtility.SetDirty(buildTargetSettings);
		EditorUtility.SetDirty(openXRSettings);
		AssetDatabase.SaveAssets();
	}

	private static XRGeneralSettingsPerBuildTarget FindOrCreateBuildTargetSettings()
	{
		string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
		if (guids.Length > 0)
		{
			string existingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
			XRGeneralSettingsPerBuildTarget existing = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(existingPath);
			EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, existing, true);
			return existing;
		}

		const string settingsFolder = "Assets/XR/Settings";
		if (!AssetDatabase.IsValidFolder(settingsFolder))
		{
			if (!AssetDatabase.IsValidFolder("Assets/XR"))
			{
				AssetDatabase.CreateFolder("Assets", "XR");
			}
			AssetDatabase.CreateFolder("Assets/XR", "Settings");
		}

		XRGeneralSettingsPerBuildTarget created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
		AssetDatabase.CreateAsset(created, settingsFolder + "/XRGeneralSettingsPerBuildTarget.asset");
		EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
		return created;
	}
}
