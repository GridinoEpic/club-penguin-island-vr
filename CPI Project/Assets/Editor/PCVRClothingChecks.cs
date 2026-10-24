using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClubPenguin;
using ClubPenguin.Avatar;
using UnityEditor;
using UnityEngine;

// Opt-in checks against the real equipment meshes and the production VR copier.
[InitializeOnLoad]
internal static class PCVRClothingChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static PCVRClothingChecks()
    {
        EditorApplication.update += () =>
        {
            const string request = "Library/PCVR_CLOTHING_CHECK_REQUEST.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
            File.Delete(request);
            try { Run(); }
            catch (Exception ex)
            {
                File.WriteAllText("Library/PCVR_CLOTHING_CHECK_RESULT.txt", ex.ToString());
                Debug.LogException(ex);
            }
        };
    }

    [MenuItem("CPI VR/Validate First Person Clothing")]
    public static void Run()
    {
        var report = new StringBuilder();
        GameObject player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Avatar/FBX/Penguin_0LOD.fbx"));
        GameObject cameraObject = new GameObject("Clothing check camera", typeof(Camera));
        PCVRAvatarRenderer avatar = null;
        Mesh unreadable = null;
        Material material = new Material(Shader.Find("Unlit/Color"));
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = player.transform.position + Vector3.up * 1.5f;
            var originalBones = player.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            GameObject outfit = new GameObject("Equipped clothing");
            outfit.transform.SetParent(player.transform, false);
            var source = outfit.AddComponent<SkinnedMeshRenderer>();
            source.sharedMaterial = material;
            source.enabled = false;
            avatar = new PCVRAvatarRenderer(player);
            avatar.BeginPose(camera);
            Transform rig = player.transform.Find("PCVR Live Penguin Avatar");
            MethodInfo scan = typeof(PCVRAvatarRenderer).GetMethod("ScanRenderers", Private);
            MethodInfo before = typeof(PCVRAvatarRenderer).GetMethod("BeforeCamera", Private);
            MethodInfo after = typeof(PCVRAvatarRenderer).GetMethod("AfterCamera", Private);
            SkinnedMeshRenderer view = null;
            int checkedParts = 0;
            foreach (string path in Directory.GetFiles("Assets/Game/ItemCustomizer/Resources/Definitions/Equipment", "*_0LOD.asset"))
            {
                EquipmentViewDefinition definition = AssetDatabase.LoadAssetAtPath<EquipmentViewDefinition>(path);
                if (definition == null || definition.SkinnedMesh?.Mesh == null) continue;
                var meshDef = definition.SkinnedMesh;
                // Torso, hips, flippers and shoes must survive the headset head filter.
                if (!path.Contains("_rep_torso_") && !path.Contains("_rep_hips_") && !path.Contains("Hand_") && !path.Contains("Foot_")) continue;
                source.bones = meshDef.BoneNames.Select(n => originalBones[n]).ToArray();
                source.rootBone = originalBones[meshDef.RootBoneName];
                source.sharedMesh = meshDef.Mesh;
                source.enabled = true;
                scan.Invoke(avatar, null);
                avatar.BeginPose(camera);
                view = rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == outfit.name + " VR mesh");
                before.Invoke(avatar, new object[] { camera });
                Require(view.enabled && !view.forceRenderingOff && view.sharedMesh != null && view.sharedMesh.triangles.Length > 0,
                    "Equipment disappeared: " + path);
                Require(view.bones.Select(t => t.name).SequenceEqual(source.bones.Select(t => t.name)), "Equipment bone order: " + path);
                after.Invoke(avatar, new object[] { camera });
                Require(view.sharedMesh == source.sharedMesh, "Full mesh restored: " + path);
                checkedParts++;
            }
            Require(checkedParts > 100, "Actual equipment assets were checked");
            report.AppendLine("PASS: " + checkedParts + " actual torso/hips/hand/foot equipment parts remain visible in first person and restore for other cameras.");
            unreadable = UnityEngine.Object.Instantiate(source.sharedMesh);
            unreadable.UploadMeshData(true);
            source.sharedMesh = unreadable;
            avatar.BeginPose(camera);
            before.Invoke(avatar, new object[] { camera });
            Require(view.sharedMesh == unreadable, "Unreadable equipment retains its mesh");
            after.Invoke(avatar, new object[] { camera });
            report.AppendLine("PASS: unreadable equipment retains geometry instead of becoming invisible.");
            // Equipment GPU renderers can use their own ordered bone subset.
            GameObject gpuObject = new GameObject("GPU clothing", typeof(MeshFilter), typeof(MeshRenderer), typeof(GpuSkinnedRenderer));
            gpuObject.transform.SetParent(player.transform, false);
            var hoodie = AssetDatabase.LoadAssetAtPath<EquipmentViewDefinition>("Assets/Game/ItemCustomizer/Resources/Definitions/Equipment/hoodie_rep_torso_0LOD.asset").SkinnedMesh;
            gpuObject.GetComponent<MeshFilter>().sharedMesh = UnityEngine.Object.Instantiate(hoodie.Mesh);
            var gpuMaterial = new Material(Shader.Find("CpRemix/GPU Combined Avatar"));
            gpuObject.GetComponent<MeshRenderer>().sharedMaterial = gpuMaterial;
            gpuObject.GetComponent<GpuSkinnedRenderer>().init(hoodie.BoneNames.Select(n => originalBones[n]).ToArray());
            scan.Invoke(avatar, null);
            avatar.BeginPose(camera);
            var gpuView = rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(r => r.name == gpuObject.name + " VR mesh");
            before.Invoke(avatar, new object[] { camera });
            Require(gpuView.enabled && gpuView.sharedMesh != null && gpuView.sharedMesh.triangles.Length > 0, "GPU equipment copied without a Rig on the player root");
            Require(gpuView.bones.Select(t => t.name).SequenceEqual(hoodie.BoneNames), "GPU equipment uses its actual bone order");
            Require(gpuView.sharedMaterial.shader.name == "CpRemix/Combined Avatar", "GPU equipment uses a CPU skinning material in the copy");
            after.Invoke(avatar, new object[] { camera });
            UnityEngine.Object.DestroyImmediate(gpuObject.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(gpuObject);
            UnityEngine.Object.DestroyImmediate(gpuMaterial);
            report.AppendLine("PASS: GPU clothing binds to its own bones and converts its material for the VR copy.");
            avatar.Dispose();
            avatar = null;
            Require(!source.forceRenderingOff, "Source outfit visibility restored");
            File.WriteAllText("Library/PCVR_CLOTHING_CHECK_RESULT.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        finally
        {
            avatar?.Dispose();
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(material);
            if (unreadable != null) UnityEngine.Object.DestroyImmediate(unreadable);
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("PCVR clothing: " + message);
    }
}
