using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClubPenguin;
using UnityEditor;
using UnityEngine;

/// <summary>Headset-independent checks against the original penguin mesh and skeleton.</summary>
public static class PCVRAvatarValidation
{
	[InitializeOnLoadMethod]
	private static void RegisterFishingChecks()
	{
		EditorApplication.update += () =>
		{
			const string jackhammerRequest = "Library/PCVR_JACKHAMMER_CHECK_REQUEST.txt";
			if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists(jackhammerRequest))
			{
				File.Delete(jackhammerRequest);
				try
				{
					RunJackhammerChecks();
					File.WriteAllText("Library/PCVR_JACKHAMMER_CHECK_RESULT.txt", "PASS: both grips, rigid scale, two-hand movement, gameplay rig unchanged.\n");
				}
				catch (Exception exception)
				{
					File.WriteAllText("Library/PCVR_JACKHAMMER_CHECK_RESULT.txt", exception.ToString());
					Debug.LogException(exception);
				}
			}
			const string request = "Library/PCVR_FISHING_CHECK_REQUEST.txt";
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
			File.Delete(request);
			try
			{
				RunFishingChecks();
				File.WriteAllText("Library/PCVR_FISHING_CHECK_RESULT.txt", "PASS: cast bobber world position, line retrieval, left-flipper prize attachment, fish controller rotation without flipper twisting.\n");
			}
			catch (Exception exception)
			{
				File.WriteAllText("Library/PCVR_FISHING_CHECK_RESULT.txt", exception.ToString());
				Debug.LogException(exception);
			}
		};
	}

	[MenuItem("CPI VR/Validate Fishing Attachments")]
	public static void RunFishingChecks()
	{
		// These render-only checks also work during Play mode without opening a scene.
		GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Avatar/FBX/Penguin_0LOD.fbx");
		GameObject player = UnityEngine.Object.Instantiate(model);
		GameObject cameraObject = new GameObject("Fishing validation camera", typeof(Camera));
		PCVRAvatarRenderer avatar = null;
		try
		{
			Camera camera = cameraObject.GetComponent<Camera>();
			camera.enabled = false;
			camera.transform.position = player.transform.position + Vector3.up * 1.5f;
			avatar = new PCVRAvatarRenderer(player);
			avatar.BeginPose(camera);
			Require(avatar.Ready, "Fishing test avatar binds to the original model");
			Transform rig = player.transform.Find("PCVR Live Penguin Avatar");
			Transform wrist = player.GetComponentsInChildren<Transform>().First(t =>
				t.name == "r_wrist_jnt" && !t.IsChildOf(rig));
			ValidateFishingAttachments(player, rig, camera, avatar, wrist);
		}
		finally
		{
			avatar?.Dispose();
			UnityEngine.Object.DestroyImmediate(player);
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}
	}

	[MenuItem("CPI VR/Validate Jackhammer Attachments")]
	public static void RunJackhammerChecks()
	{
		GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Avatar/FBX/Penguin_0LOD.fbx");
		GameObject player = UnityEngine.Object.Instantiate(model);
		GameObject cameraObject = new GameObject("Jackhammer validation camera", typeof(Camera));
		PCVRAvatarRenderer avatar = null;
		try
		{
			Camera camera = cameraObject.GetComponent<Camera>();
			camera.enabled = false;
			camera.transform.position = Vector3.up * 1.5f;
			avatar = new PCVRAvatarRenderer(player);
			avatar.BeginPose(camera);
			Require(avatar.Ready, "Jackhammer test avatar is ready");
			Transform rig = player.transform.Find("PCVR Live Penguin Avatar");
			Transform left = rig.GetComponentsInChildren<Transform>().First(t => t.name == "l_wrist_jnt");
			Transform right = rig.GetComponentsInChildren<Transform>().First(t => t.name == "r_wrist_jnt");
			Transform tool = new GameObject("Test rigid jackhammer").transform;
			tool.SetParent(right, false);
			Vector3 leftStart = left.position, rightStart = right.position;
			Vector3 leftHandle = tool.InverseTransformPoint(leftStart), rightHandle = tool.InverseTransformPoint(rightStart);
			Vector3 scale = tool.lossyScale;
			Vector3[] gameplay = player.GetComponentsInChildren<Transform>().Where(t => !t.IsChildOf(rig)).Select(t => t.position).ToArray();
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			foreach (var entry in new[] {
				new { Name = "twoHandedProp", Value = (object)tool },
				new { Name = "propPosition", Value = (object)tool.position },
				new { Name = "propRotation", Value = (object)tool.rotation },
				new { Name = "leftGrip", Value = (object)leftStart },
				new { Name = "rightGrip", Value = (object)rightStart } })
				typeof(PCVRAvatarRenderer).GetField(entry.Name, flags).SetValue(avatar, entry.Value);
			avatar.PoseHand(true, leftStart + Vector3.up * 0.015f, Quaternion.identity, 0f);
			avatar.PoseHand(false, rightStart + Vector3.up * 0.01f, Quaternion.identity, 0f);
			Vector3 center = (left.position + right.position) * 0.5f;
			avatar.PoseTwoHandedProp();
			Require(Vector3.Distance(tool.TransformPoint(leftHandle), left.position) < 0.002f, "Left flipper stays on its handle");
			Require(Vector3.Distance(tool.TransformPoint(rightHandle), right.position) < 0.002f, "Right flipper stays on its handle");
			Require(Vector3.Distance((tool.TransformPoint(leftHandle) + tool.TransformPoint(rightHandle)) * 0.5f, center) < 0.0001f,
				"Both hand positions determine the handle center");
			Require(Vector3.Distance(tool.lossyScale, scale) < 0.0001f, "Jackhammer remains rigid");
			Require(gameplay.SequenceEqual(player.GetComponentsInChildren<Transform>().Where(t => !t.IsChildOf(rig)).Select(t => t.position)),
				"Two-hand pose leaves gameplay skeleton untouched");
		}
		finally
		{
			avatar?.Dispose();
			UnityEngine.Object.DestroyImmediate(player);
			UnityEngine.Object.DestroyImmediate(cameraObject);
		}
	}

	[MenuItem("CPI VR/Validate Articulated Avatar")]
	public static void Run()
	{
		GameObject player = null, headObject = null, mirrorObject = null, lightObject = null;
		PCVRAvatarRenderer avatar = null;
		try
		{
			ValidateSolver();
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Avatar/FBX/Penguin_0LOD.fbx");
			Require(model != null, "Original penguin model is available");
			player = UnityEngine.Object.Instantiate(model);
			player.name = "VR validation penguin";
			player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
			SkinnedMeshRenderer[] originalRenderers = player.GetComponentsInChildren<SkinnedMeshRenderer>();
			Require(originalRenderers.Length > 0, "Original model has skinned meshes");
			// Neutral inspection materials let a standalone validation project render the FBX too.
			foreach (SkinnedMeshRenderer renderer in originalRenderers)
			{
				Material[] materials = renderer.sharedMaterials;
				for (int i = 0; i < materials.Length; i++)
				{
					string name = materials[i] != null ? materials[i].name.ToLowerInvariant() : "";
					Material material = new Material(Shader.Find("Standard"));
					material.color = name.Contains("beak") || name.Contains("feet") ? new Color(1f, 0.65f, 0.1f)
						: name.Contains("pupil") ? Color.black : name.Contains("eye") ? Color.white : new Color(0.08f, 0.65f, 0.85f);
					material.SetFloat("_Glossiness", 0.15f);
					materials[i] = material;
				}
				renderer.sharedMaterials = materials;
			}
			headObject = new GameObject("Validation headset");
			Camera head = headObject.AddComponent<Camera>();
			head.enabled = false;
			head.nearClipPlane = 0.005f;
			head.transform.position = new Vector3(0f, 1.5f, 0f);
			mirrorObject = new GameObject("Validation mirror view");
			Camera mirror = mirrorObject.AddComponent<Camera>();
			mirror.enabled = false;
			mirror.transform.position = new Vector3(0f, 1.25f, 3f);
			mirror.transform.LookAt(new Vector3(0f, 0.85f, 0f));
			mirror.clearFlags = CameraClearFlags.SolidColor;
			mirror.backgroundColor = new Color(0.09f, 0.12f, 0.17f);
			lightObject = new GameObject("Validation light");
			Light light = lightObject.AddComponent<Light>();
			light.type = LightType.Directional;
			light.intensity = 1.3f;
			light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
			avatar = new PCVRAvatarRenderer(player);
			avatar.BeginPose(head);
			Require(avatar.Ready, "Live avatar binds to the original model");
			Transform rig = player.transform.Find("PCVR Live Penguin Avatar");
			Require(rig != null, "Visual rig created");
			Transform wrist = rig.GetComponentsInChildren<Transform>().First(t => t.name == "l_wrist_jnt");
            Transform sourceWrist = player.GetComponentsInChildren<Transform>().First(t => t.name == "r_wrist_jnt" && !t.IsChildOf(rig));
            GameObject socket = new GameObject("Snowball test socket");
            socket.transform.SetParent(sourceWrist, false);
            socket.transform.localPosition = new Vector3(0.02f, 0.03f, 0.01f);
            Require(avatar.TryGetAttachmentPosition(socket.transform, out _), "Snowball socket maps to visible rig");
            avatar.PoseHand(false, new Vector3(0.35f, 1.25f, 0.4f), Quaternion.identity, 0f);
            Transform visualSocket = rig.GetComponentsInChildren<Transform>().First(t => t.name == socket.name);
            Require(avatar.TryGetAttachmentPosition(socket.transform, out Vector3 heldPosition) &&
                Vector3.Distance(heldPosition, visualSocket.position) < 0.0001f,
                "Readied snowball uses the final flipper socket pose");
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Snowball copier exclusion test";
            ball.transform.SetParent(sourceWrist, false);
            ball.AddComponent<SnowballController>();
            typeof(PCVRAvatarRenderer).GetMethod("ScanRenderers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, null);
            Require(!rig.GetComponentsInChildren<Transform>().Any(t => t.name == ball.name + " VR mesh") &&
                !ball.GetComponent<Renderer>().forceRenderingOff, "Held snowball is neither hidden nor cloned by the avatar");
            UnityEngine.Object.DestroyImmediate(ball);
			ValidateFishingAttachments(player, rig, head, avatar, sourceWrist);
			Quaternion wristRest = wrist.localRotation;
			avatar.PoseHand(true, new Vector3(-0.3f, 1.15f, 0.3f), Quaternion.Euler(90f, 120f, 45f), 0f);
			Require(Quaternion.Angle(wrist.localRotation, wristRest) < 0.01f, "Controller rotation does not twist the flipper tip");
			Transform[] originalBones = player.GetComponentsInChildren<Transform>().Where(t => !t.IsChildOf(rig)).ToArray();
			Vector3[] positions = originalBones.Select(t => t.localPosition).ToArray();
			Quaternion[] rotations = originalBones.Select(t => t.localRotation).ToArray();
			avatar.PoseHand(true, new Vector3(-0.3f, 1.15f, 0.3f), Quaternion.identity, 0f);
			avatar.PoseHand(false, new Vector3(0.4f, 1.7f, 0.15f), Quaternion.Euler(-45f, 0f, 25f), 0.5f);
			for (int i = 0; i < originalBones.Length; i++)
			{
				Require(originalBones[i].localPosition == positions[i], "Source positions unchanged");
				Require(Quaternion.Angle(originalBones[i].localRotation, rotations[i]) < 0.01f, "Source rotations unchanged");
			}
			SkinnedMeshRenderer[] views = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
			int fullTriangles = views.Sum(v => v.sharedMesh.triangles.Length);
			MethodInfo before = typeof(PCVRAvatarRenderer).GetMethod("BeforeCamera", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo after = typeof(PCVRAvatarRenderer).GetMethod("AfterCamera", BindingFlags.Instance | BindingFlags.NonPublic);
			before.Invoke(avatar, new object[] { head });
			int headlessTriangles = views.Sum(v => v.sharedMesh != null ? v.sharedMesh.triangles.Length : 0);
			Require(headlessTriangles > 0 && headlessTriangles < fullTriangles, "Only head geometry removed in headset");
			before.Invoke(avatar, new object[] { mirror });
			Require(views.Sum(v => v.sharedMesh.triangles.Length) == fullTriangles, "Nested mirror gets complete character");
			after.Invoke(avatar, new object[] { mirror });
			Require(views.Sum(v => v.sharedMesh != null ? v.sharedMesh.triangles.Length : 0) == headlessTriangles, "Nested mirror restores headset mesh");
			after.Invoke(avatar, new object[] { head });
			Directory.CreateDirectory("Library/PCVRAvatarValidation");
			Render(mirror, "Library/PCVRAvatarValidation/mirror.png");
			head.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
			Render(head, "Library/PCVRAvatarValidation/first-person.png");
			// Mesh replacement exercises outfit/LOD refresh without rebuilding the rig.
			Mesh replacement = UnityEngine.Object.Instantiate(originalRenderers[0].sharedMesh);
			originalRenderers[0].sharedMesh = replacement;
			avatar.BeginPose(head);
			Require(views.Any(v => v.sharedMesh == replacement), "Outfit mesh changes reach visual copy");
			avatar.Dispose();
			avatar = null;
			Require(originalRenderers.All(r => !r.forceRenderingOff), "Source visibility restored when VR ends");
			GameObject menuModel = Resources.Load<GameObject>("PCVR/MenuPenguin");
			Require(menuModel != null, "Main menu penguin resource is available in players");
			GameObject menuPenguin = UnityEngine.Object.Instantiate(menuModel);
			try
			{
				using (PCVRAvatarRenderer menuAvatar = new PCVRAvatarRenderer(menuPenguin))
				{
					menuAvatar.BeginPose(head);
					Require(menuAvatar.Ready, "Main menu model binds to the tracked avatar rig");
					player.SetActive(false);
					Render(mirror, "Library/PCVRAvatarValidation/menu-penguin.png");
					Transform menuRig = menuPenguin.transform.Find("PCVR Live Penguin Avatar");
					before.Invoke(menuAvatar, new object[] { head });
					Require(menuRig.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r => r.sharedMesh != null && r.sharedMesh.vertexCount > 0),
						"Menu avatar has readable first-person geometry");
					after.Invoke(menuAvatar, new object[] { head });
					foreach (Renderer renderer in menuPenguin.GetComponentsInChildren<Renderer>())
						if (!renderer.transform.IsChildOf(menuRig)) renderer.enabled = false;
					menuAvatar.BeginPose(head);
					Require(!menuAvatar.Ready && menuRig.GetComponentsInChildren<Renderer>().All(r => !r.enabled),
						"Cannon-style source hiding also hides the tracked avatar");
				}
			}
			finally { UnityEngine.Object.DestroyImmediate(menuPenguin); }
			File.WriteAllText("Library/PCVRAvatarValidation/result.txt", "PASS: 1000 IK targets, original and menu model binding, wrist stability, held snowball socket and visibility, cast bobber world position, line retrieval, left-flipper prize reparenting, source isolation, nested mirror/headset visibility, outfit mesh replacement, cannon visibility, and visibility restoration.\n");
			Debug.Log("CPI PCVR avatar validation passed. Previews: Library/PCVRAvatarValidation");
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
			if (Application.isBatchMode) EditorApplication.Exit(1);
			throw;
		}
		finally
		{
			avatar?.Dispose();
			if (player != null) UnityEngine.Object.DestroyImmediate(player);
			if (headObject != null) UnityEngine.Object.DestroyImmediate(headObject);
			if (mirrorObject != null) UnityEngine.Object.DestroyImmediate(mirrorObject);
			if (lightObject != null) UnityEngine.Object.DestroyImmediate(lightObject);
		}
	}

	private static void ValidateFishingAttachments(GameObject player, Transform rig, Camera head,
		PCVRAvatarRenderer avatar, Transform rightWrist)
	{
		Transform leftWrist = player.GetComponentsInChildren<Transform>().First(t =>
			t.name == "l_wrist_jnt" && !t.IsChildOf(rig));
		GameObject rod = new GameObject("Fishing test rod", typeof(SkinnedMeshRenderer));
		rod.transform.SetParent(rightWrist, false);
		GameObject lineEnd = new GameObject("Fishing test line end");
		lineEnd.transform.SetParent(rod.transform, false);
		GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		SkinnedMeshRenderer skin = rod.GetComponent<SkinnedMeshRenderer>();
		skin.sharedMesh = shape.GetComponent<MeshFilter>().sharedMesh;
		skin.sharedMaterials = shape.GetComponent<Renderer>().sharedMaterials;
		skin.bones = new[] { lineEnd.transform };
		UnityEngine.Object.DestroyImmediate(shape);
		MethodInfo scan = typeof(PCVRAvatarRenderer).GetMethod("ScanRenderers", BindingFlags.Instance | BindingFlags.NonPublic);
		scan.Invoke(avatar, null);
		avatar.BeginPose(head);
		SkinnedMeshRenderer copy = rig.GetComponentsInChildren<SkinnedMeshRenderer>().First(t => t.name == rod.name + " VR mesh");
		lineEnd.transform.SetParent(head.transform, false);
		lineEnd.transform.position = new Vector3(2f, 0.2f, 4f);
		avatar.BeginPose(head);
		avatar.PoseHand(false, new Vector3(0.4f, 1.1f, 0.2f), Quaternion.identity, 0f);
		Require(Vector3.Distance(copy.bones[0].position, lineEnd.transform.position) < 0.0001f,
			"Cast bobber stays at the water position after flipper IK");
		lineEnd.transform.SetParent(rod.transform, false);
		avatar.BeginPose(head);
		Require(copy.bones[0].parent.name == rod.name, "Reeled line reattaches to the visible rod");
		GameObject prize = GameObject.CreatePrimitive(PrimitiveType.Sphere);
		prize.name = "Fishing caught prize test";
		prize.transform.SetParent(rightWrist, false);
		scan.Invoke(avatar, null);
		prize.transform.SetParent(leftWrist, false);
		avatar.BeginPose(head);
		Require(rig.GetComponentsInChildren<Transform>().First(t => t.name == prize.name + " VR mesh").parent.parent.name == "l_wrist_jnt",
			"Caught prize follows the left flipper after reparenting");
		Transform visiblePrize = rig.GetComponentsInChildren<Transform>().First(t => t.name == prize.name + " VR mesh").parent;
		Quaternion wristRotation = visiblePrize.parent.rotation;
		Quaternion sourceRotation = prize.transform.rotation;
		Vector3 prizePosition = visiblePrize.position;
		Quaternion controllerRotation = Quaternion.Euler(35f, 120f, -60f);
		Require(avatar.RotateAttachment(prize.transform, controllerRotation) &&
			Quaternion.Angle(visiblePrize.rotation, controllerRotation) < 0.01f,
			"Caught fish rotates with the controller");
		Require(Quaternion.Angle(visiblePrize.parent.rotation, wristRotation) < 0.01f &&
			Vector3.Distance(visiblePrize.position, prizePosition) < 0.0001f &&
			Quaternion.Angle(prize.transform.rotation, sourceRotation) < 0.01f,
			"Fish rotation preserves flipper pose, attachment position, and gameplay rig");
		UnityEngine.Object.DestroyImmediate(prize);
		UnityEngine.Object.DestroyImmediate(rod);
	}

	private static void ValidateSolver()
	{
		System.Random random = new System.Random(4417);
		for (int i = 0; i < 1000; i++)
		{
			Vector3 target = i == 0 ? Vector3.zero : new Vector3((float)random.NextDouble() * 4f - 2f,
				(float)random.NextDouble() * 4f - 2f, (float)random.NextDouble() * 4f - 2f);
			Vector3 pole = i % 2 == 0 ? Vector3.down : target;
			PCVRArmIK.Solve(Vector3.zero, target, pole, 0.31f, 0.26f, out Vector3 elbow, out Vector3 wrist);
			Require(!float.IsNaN(elbow.x) && !float.IsNaN(wrist.x), "Finite solver result");
			Require(Mathf.Abs(elbow.magnitude - 0.31f) < 0.0001f, "Upper arm length preserved");
			Require(Mathf.Abs(Vector3.Distance(elbow, wrist) - 0.26f) < 0.0001f, "Forearm length preserved");
			if (target.magnitude > 0.051f && target.magnitude < 0.569f)
				Require(Vector3.Distance(target, wrist) < 0.0001f, "Reachable target reached");
		}
	}
	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException("PCVR avatar validation: " + message);
	}
	private static void Render(Camera camera, string path)
	{
		RenderTexture texture = new RenderTexture(960, 960, 24);
		RenderTexture previous = RenderTexture.active;
		camera.targetTexture = texture;
		camera.Render();
		RenderTexture.active = texture;
		Texture2D image = new Texture2D(960, 960, TextureFormat.RGB24, false);
		image.ReadPixels(new Rect(0, 0, 960, 960), 0, 0);
		image.Apply();
		File.WriteAllBytes(path, image.EncodeToPNG());
		RenderTexture.active = previous;
		camera.targetTexture = null;
		UnityEngine.Object.DestroyImmediate(image);
		UnityEngine.Object.DestroyImmediate(texture);
	}
}
