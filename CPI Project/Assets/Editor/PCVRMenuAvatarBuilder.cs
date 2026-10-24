using System;
using UnityEditor;
using UnityEngine;

internal static class PCVRMenuAvatarBuilder
{
	[MenuItem("CPI VR/Rebuild Menu Penguin")]
	public static void Rebuild()
	{
		const string folder = "Assets/Resources/PCVR";
		GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Avatar/FBX/Penguin_0LOD.fbx");
		if (model == null) throw new InvalidOperationException("Original penguin model is missing.");
		GameObject penguin = UnityEngine.Object.Instantiate(model);
		try
		{
			penguin.name = "MenuPenguin";
			foreach (Animator animator in penguin.GetComponentsInChildren<Animator>()) UnityEngine.Object.DestroyImmediate(animator);
			foreach (Renderer renderer in penguin.GetComponentsInChildren<Renderer>())
			{
				Material[] materials = renderer.sharedMaterials;
				for (int i = 0; i < materials.Length; i++)
				{
					string name = renderer.name;
					Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Avatar/Materials/" + name + ".mat");
					if (material == null) throw new InvalidOperationException("Missing penguin material: " + name);
					materials[i] = material;
				}
				renderer.sharedMaterials = materials;
			}
			PrefabUtility.SaveAsPrefabAsset(penguin, folder + "/MenuPenguin.prefab");
			AssetDatabase.SaveAssets();
		}
		finally { UnityEngine.Object.DestroyImmediate(penguin); }
	}
}
