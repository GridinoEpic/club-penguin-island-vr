using System;
using System.Collections.Generic;
using ClubPenguin.Avatar;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClubPenguin
{
	/// <summary>
	/// Render-only copy of the live outfit and skeleton. Never clones gameplay components
	/// or modifies the locomotion/animation rig. Both camera views share the same posed bones.
	/// </summary>
	public sealed class PCVRAvatarRenderer : IDisposable
	{
		private readonly GameObject source;
		private readonly Transform root;
		private readonly Dictionary<Transform, Transform> bones = new Dictionary<Transform, Transform>();
		private readonly List<KeyValuePair<Transform, Transform>> boneSnapshot = new List<KeyValuePair<Transform, Transform>>();
		private readonly List<Part> parts = new List<Part>();
		private readonly Stack<bool> cameraViews = new Stack<bool>();
		private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
		private readonly Transform neck, leftEye, rightEye;
		private readonly Arm leftArm, rightArm;
		private Camera viewCamera;
		private float nextScan;
		private float calibratedScale;
		private float bodyYaw;
		private bool firstPerson;
		private Transform twoHandedProp;
		private Vector3 propPosition, leftGrip, rightGrip;
		private Quaternion propRotation;
		public bool Ready { get; private set; }

		private sealed class Part
		{
			public Renderer Source, View;
			public Mesh Mesh, Headless;
			public Transform[] SourceBones;
			public Material[] SourceMaterials;
			public readonly List<Material> OwnedMaterials = new List<Material>();
			public bool OriginalForceOff, IsHead, Gpu;
		}

		private sealed class Arm
		{
			public Transform Shoulder, Elbow, Wrist;
			public bool Valid => Shoulder != null && Elbow != null && Wrist != null;
		}

		public PCVRAvatarRenderer(GameObject player)
		{
			source = player;
			root = new GameObject("PCVR Live Penguin Avatar").transform;
			root.SetParent(player.transform, false);
			bones.Add(player.transform, root);
			Transform sourceNeck = Find(player.transform, "neck_jnt");
			if (sourceNeck != null) neck = CopyTransform(sourceNeck);
			Transform eye = Find(player.transform, "l_pupil_jnt");
			if (eye != null) leftEye = CopyTransform(eye);
			eye = Find(player.transform, "r_pupil_jnt");
			if (eye != null) rightEye = CopyTransform(eye);
			leftArm = CreateArm("l_");
			rightArm = CreateArm("r_");
			bodyYaw = player.transform.eulerAngles.y;
			Camera.onPreCull += BeforeCamera;
			Camera.onPostRender += AfterCamera;
		}

		private static Transform Find(Transform parent, string name)
		{
			foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
				if (child.name == name) return child;
			return null;
		}

		private Transform CopyTransform(Transform original)
		{
			if (bones.TryGetValue(original, out Transform copy)) return copy;
			if (!original.IsChildOf(source.transform))
			{
				// A rod may first be scanned after casting has already detached its bones.
				copy = new GameObject(original.name).transform;
				copy.SetParent(root, false);
				bones.Add(original, copy);
				return copy;
			}
			Transform parent = CopyTransform(original.parent);
			copy = new GameObject(original.name).transform;
			copy.SetParent(parent, false);
			copy.localPosition = original.localPosition;
			copy.localRotation = original.localRotation;
			copy.localScale = original.localScale;
			bones.Add(original, copy);
			return copy;
		}

		private Arm CreateArm(string prefix)
		{
			Arm arm = new Arm();
			Transform shoulder = Find(source.transform, prefix + "shoulder_jnt");
			Transform elbow = Find(source.transform, prefix + "elbow_jnt");
			Transform wrist = Find(source.transform, prefix + "wrist_jnt");
			if (shoulder == null || elbow == null || wrist == null) return arm;
			arm.Shoulder = CopyTransform(shoulder);
			arm.Elbow = CopyTransform(elbow);
			arm.Wrist = CopyTransform(wrist);
			return arm;
		}

		public void BeginPose(Camera camera)
		{
			viewCamera = camera;
			if (source == null || camera == null || neck == null || !leftArm.Valid || !rightArm.Valid) return;
			if (Time.unscaledTime >= nextScan)
			{
				ScanRenderers();
				nextScan = Time.unscaledTime + 0.5f;
			}
			root.localPosition = Vector3.zero;
			root.localRotation = Quaternion.identity;
			root.localScale = Vector3.one;
			// Gameplay can reparent attachments (the fishing line leaves the rod).
			// Reconcile the copied hierarchy before applying this frame's hand pose.
			boneSnapshot.Clear();
			boneSnapshot.AddRange(bones);
			foreach (var pair in boneSnapshot)
			{
				if (pair.Key == null || pair.Key == source.transform) continue;
				if (!pair.Key.IsChildOf(source.transform)) continue;
				Transform parent = CopyTransform(pair.Key.parent);
				if (pair.Value.parent != parent) pair.Value.SetParent(parent, false);
				pair.Value.localPosition = pair.Key.localPosition;
				pair.Value.localRotation = pair.Key.localRotation;
				pair.Value.localScale = pair.Key.localScale;
			}
			Vector3 eyes = EyePosition();
			if (calibratedScale == 0f)
			{
				float modelHeight = eyes.y - source.transform.position.y;
				float trackedHeight = camera.transform.position.y - source.transform.position.y;
				calibratedScale = Mathf.Clamp(trackedHeight / Mathf.Max(0.1f, modelHeight), 0.5f, 3f);
			}
			root.localScale = Vector3.one * calibratedScale;
			Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
			float headYaw = forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward).eulerAngles.y : bodyYaw;
			float yawDelta = Mathf.DeltaAngle(bodyYaw, headYaw);
			// Small glances turn only the head; larger turns pull the torso around.
			float desiredYaw = headYaw - Mathf.Clamp(yawDelta, -30f, 30f);
			bodyYaw = Mathf.LerpAngle(bodyYaw, desiredYaw, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
			root.rotation = Quaternion.Euler(0f, bodyYaw, 0f);
			Quaternion headDelta = Quaternion.Inverse(root.rotation) * camera.transform.rotation;
			neck.rotation = root.rotation * headDelta * Quaternion.Inverse(root.rotation) * neck.rotation;
			root.position += camera.transform.position - EyePosition();
			// Detached skin bones must remain at their gameplay world positions,
			// independent of avatar scale, head movement, and controller IK.
			foreach (var pair in bones)
			{
				if (pair.Key == null || pair.Key == source.transform || pair.Key.IsChildOf(source.transform)) continue;
				pair.Value.SetParent(root, false);
				pair.Value.SetPositionAndRotation(pair.Key.position, pair.Key.rotation);
				Vector3 scale = pair.Key.lossyScale;
				Vector3 parentScale = root.lossyScale;
				pair.Value.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
			}
			// Capture the animated hold before controller IK moves either wrist.
			twoHandedProp = null;
			ClubPenguin.Props.Prop prop = source.GetComponent<ClubPenguin.Props.PropUser>()?.Prop;
			if (prop != null && prop.gameObject.activeInHierarchy && prop.transform.IsChildOf(source.transform) &&
				prop.PropDef != null && (string.Equals(prop.PropDef.NameOnServer, "JackHammer", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(prop.PropDef.NameOnServer, "GearJackhammer", StringComparison.OrdinalIgnoreCase)))
			{
				twoHandedProp = CopyTransform(prop.transform);
				propPosition = twoHandedProp.position;
				propRotation = twoHandedProp.rotation;
				leftGrip = leftArm.Wrist.position;
				rightGrip = rightArm.Wrist.position;
			}
			Ready = false;
			foreach (Part part in parts)
			{
				if (part.Source == null) { part.View.enabled = false; continue; }
				if (!part.Source.transform.IsChildOf(source.transform))
				{
					part.View.enabled = false;
					part.Source.forceRenderingOff = part.OriginalForceOff;
					continue;
				}
				SyncPart(part);
				part.View.enabled = part.Source.enabled && !part.OriginalForceOff && part.Source.gameObject.activeInHierarchy && part.Mesh != null;
				// Loading/outfit changes remain authoritative; do not force disabled source parts on.
				part.Source.forceRenderingOff = true;
				if (part.View.enabled && part.SourceBones != null) Ready = true;
			}
		}

		private Vector3 EyePosition() => leftEye != null && rightEye != null
			? (leftEye.position + rightEye.position) * 0.5f : neck.position;

		public bool PoseHand(bool left, Vector3 position, Quaternion rotation, float grip)
		{
			if (!Ready) return false;
			Arm arm = left ? leftArm : rightArm;
			float upper = Vector3.Distance(arm.Shoulder.position, arm.Elbow.position);
			float lower = Vector3.Distance(arm.Elbow.position, arm.Wrist.position);
			Vector3 pole = root.right * (left ? -0.65f : 0.65f) - Vector3.up * 0.85f - root.forward * 0.25f;
			PCVRArmIK.Solve(arm.Shoulder.position, position, pole, upper, lower, out Vector3 elbow, out Vector3 wrist);
			arm.Shoulder.rotation = Quaternion.FromToRotation(arm.Elbow.position - arm.Shoulder.position,
				elbow - arm.Shoulder.position) * arm.Shoulder.rotation;
			arm.Elbow.rotation = Quaternion.FromToRotation(arm.Wrist.position - arm.Elbow.position,
				wrist - arm.Elbow.position) * arm.Elbow.rotation;
			// A flipper has no independently rotating hand. Keep the animated wrist
			// local pose, so the tip follows the forearm instead of twisting at its end.
			return true;
		}

		public bool RotateAttachment(Transform sourceAttachment, Quaternion worldRotation)
		{
			if (!Ready || sourceAttachment == null || !sourceAttachment.IsChildOf(source.transform)) return false;
			// Rotate only the render copy of this attachment, after hand IK. Its
			// position stays on the posed wrist, and the wrist itself does not twist.
			CopyTransform(sourceAttachment).rotation = worldRotation;
			return true;
		}

		public void PoseTwoHandedProp()
		{
			if (!Ready || twoHandedProp == null) return;
			Vector3 originalSpan = rightGrip - leftGrip;
			Vector3 handSpan = rightArm.Wrist.position - leftArm.Wrist.position;
			if (originalSpan.sqrMagnitude < 0.000001f) return;
			Quaternion turn = handSpan.sqrMagnitude > 0.000001f
				? Quaternion.FromToRotation(originalSpan, handSpan) : Quaternion.identity;
			Vector3 originalCenter = (leftGrip + rightGrip) * 0.5f;
			Vector3 center = (leftArm.Wrist.position + rightArm.Wrist.position) * 0.5f;
			// Keep the tool rigid. Both controllers move its center and handle axis;
			// the flippers then solve to the handles at their original spacing.
			twoHandedProp.SetPositionAndRotation(center + turn * (propPosition - originalCenter), turn * propRotation);
			Vector3 position = twoHandedProp.position;
			Quaternion rotation = twoHandedProp.rotation;
			PoseHand(true, center + turn * (leftGrip - originalCenter), Quaternion.identity, 0f);
			PoseHand(false, center + turn * (rightGrip - originalCenter), Quaternion.identity, 0f);
			// Solving the right arm also moves the prop's parent. Restore the fitted pose.
			twoHandedProp.SetPositionAndRotation(position, rotation);
		}

        public bool TryGetAttachmentPosition(Transform sourceSocket, out Vector3 position)
        {
            position = Vector3.zero;
            if (!Ready || sourceSocket == null || !sourceSocket.IsChildOf(source.transform)) return false;
            Transform socket = CopyTransform(sourceSocket);
            if (socket == null) return false;
            position = socket.position;
            return true;
        }

		private void ScanRenderers()
		{
			foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
			{
				if (renderer.transform.IsChildOf(root) || parts.Exists(p => p.Source == renderer)) continue;
                // Snowballs have their own held/projectile lifecycle and follow the
                // posed socket directly; never hide or duplicate them as avatar parts.
                if (renderer.GetComponentInParent<SnowballController>() != null) continue;
				SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
				GpuSkinnedRenderer gpu = renderer.GetComponent<GpuSkinnedRenderer>();
				bool rigid = skinned == null && gpu == null;
				// Include bone-attached props, but leave particles, UI, and blob shadows to the game.
				Transform hips = Find(source.transform, "hips_jnt");
				if (rigid && (!(renderer is MeshRenderer) || ((hips == null || !renderer.transform.IsChildOf(hips)) && renderer.GetComponentInParent<ClubPenguin.Props.Prop>() == null))) continue;
				Transform[] sourceBones = skinned != null ? skinned.bones : gpu != null ? gpu.Bones : null;
				if (!rigid && (sourceBones == null || sourceBones.Length == 0)) continue;
				Transform anchor = CopyTransform(renderer.transform);
				GameObject viewObject = new GameObject(renderer.name + " VR mesh");
				viewObject.layer = renderer.gameObject.layer;
				viewObject.transform.SetParent(anchor, false);
				Renderer view;
				if (!rigid)
				{
					SkinnedMeshRenderer skin = viewObject.AddComponent<SkinnedMeshRenderer>();
					Transform[] mapped = new Transform[sourceBones.Length];
					for (int i = 0; i < mapped.Length; i++) mapped[i] = sourceBones[i] != null ? CopyTransform(sourceBones[i]) : null;
					skin.bones = mapped;
					skin.rootBone = root;
					skin.updateWhenOffscreen = true;
					view = skin;
				}
				else { viewObject.AddComponent<MeshFilter>(); view = viewObject.AddComponent<MeshRenderer>(); }
				view.shadowCastingMode = renderer.shadowCastingMode;
				view.receiveShadows = renderer.receiveShadows;
				parts.Add(new Part { Source = renderer, View = view, SourceBones = sourceBones, Gpu = gpu != null,
					OriginalForceOff = renderer.forceRenderingOff, IsHead = anchor.IsChildOf(neck) });
			}
		}

		private void SyncPart(Part part)
		{
			Transform[] sourceBones = part.Source is SkinnedMeshRenderer sourceSkin ? sourceSkin.bones
				: part.Gpu ? part.Source.GetComponent<GpuSkinnedRenderer>().Bones : null;
			bool bonesChanged = sourceBones != null && (part.SourceBones == null || sourceBones.Length != part.SourceBones.Length);
			for (int i = 0; !bonesChanged && sourceBones != null && i < sourceBones.Length; i++)
				bonesChanged = sourceBones[i] != part.SourceBones[i];
			if (bonesChanged)
			{
				part.SourceBones = sourceBones;
				Transform[] mapped = new Transform[sourceBones.Length];
				for (int i = 0; i < mapped.Length; i++) mapped[i] = sourceBones[i] != null ? CopyTransform(sourceBones[i]) : null;
				((SkinnedMeshRenderer)part.View).bones = mapped;
			}
			Mesh mesh = part.Source is SkinnedMeshRenderer skin ? skin.sharedMesh : part.Source.GetComponent<MeshFilter>().sharedMesh;
			if (mesh != part.Mesh || bonesChanged)
			{
				if (part.Headless != null) Release(part.Headless);
				part.Mesh = mesh;
				part.Headless = mesh != null && part.SourceBones != null ? CreateHeadlessMesh(mesh, part.SourceBones) : null;
				SetMesh(part, firstPerson && part.Headless != null ? part.Headless : mesh);
			}
			Material[] materials = part.Source.sharedMaterials;
			bool changed = part.SourceMaterials == null || materials.Length != part.SourceMaterials.Length;
			for (int i = 0; !changed && i < materials.Length; i++) changed = materials[i] != part.SourceMaterials[i];
			if (changed)
			{
				foreach (Material material in part.OwnedMaterials) Release(material);
				part.OwnedMaterials.Clear();
				part.SourceMaterials = materials;
				Material[] viewMaterials = (Material[])materials.Clone();
				if (part.Gpu)
				{
					for (int i = 0; i < materials.Length; i++)
					{
						if (materials[i] == null) continue;
						Material converted = new Material(materials[i]);
						converted.shader = Shader.Find(materials[i].shader.name.Replace("GPU Combined", "Combined"));
						viewMaterials[i] = converted;
						part.OwnedMaterials.Add(converted);
					}
				}
				part.View.sharedMaterials = viewMaterials;
			}
			part.Source.GetPropertyBlock(properties);
			part.View.SetPropertyBlock(properties);
		}

		private Mesh CreateHeadlessMesh(Mesh mesh, Transform[] sourceBones)
		{
			if (!mesh.isReadable) return null;
			bool[] headBones = new bool[sourceBones.Length];
			for (int i = 0; i < sourceBones.Length; i++)
				headBones[i] = sourceBones[i] != null && bones.TryGetValue(sourceBones[i], out Transform bone) && (bone == neck || bone.IsChildOf(neck));
			BoneWeight[] weights = mesh.boneWeights;
			if (weights.Length != mesh.vertexCount) return null;
			bool[] headVertices = new bool[weights.Length];
			for (int i = 0; i < weights.Length; i++)
			{
				BoneWeight w = weights[i];
				float amount = HeadWeight(w.boneIndex0, w.weight0, headBones) + HeadWeight(w.boneIndex1, w.weight1, headBones)
					+ HeadWeight(w.boneIndex2, w.weight2, headBones) + HeadWeight(w.boneIndex3, w.weight3, headBones);
				headVertices[i] = amount > 0.25f;
			}
			Mesh copy = UnityEngine.Object.Instantiate(mesh);
			copy.name = mesh.name + " (VR first person)";
			for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
			{
				int[] triangles = mesh.GetTriangles(submesh);
				List<int> kept = new List<int>(triangles.Length);
				for (int i = 0; i < triangles.Length; i += 3)
					if (!headVertices[triangles[i]] && !headVertices[triangles[i + 1]] && !headVertices[triangles[i + 2]])
					{ kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]); }
				copy.SetTriangles(kept, submesh, false);
			}
			return copy;
		}

		private static float HeadWeight(int index, float weight, bool[] headBones) => index >= 0 && index < headBones.Length && headBones[index] ? weight : 0f;
		private static void SetMesh(Part part, Mesh mesh)
		{
			if (part.View is SkinnedMeshRenderer skin) skin.sharedMesh = mesh;
			else part.View.GetComponent<MeshFilter>().sharedMesh = mesh;
		}

		private void BeforeCamera(Camera camera)
		{
			cameraViews.Push(firstPerson);
			SetFirstPerson(camera == viewCamera);
		}
		private void AfterCamera(Camera camera)
		{
			SetFirstPerson(cameraViews.Count > 0 && cameraViews.Pop());
		}
		private void SetFirstPerson(bool value)
		{
			firstPerson = value;
			foreach (Part part in parts)
			{
				if (part.View == null) continue;
				// Some bundled equipment meshes are not CPU-readable. Keep their
				// geometry when head filtering is unavailable instead of assigning null.
				SetMesh(part, value && part.Headless != null ? part.Headless : part.Mesh);
				part.View.forceRenderingOff = value && part.IsHead;
			}
		}

		private static void Release(UnityEngine.Object value)
		{
			if (Application.isPlaying) UnityEngine.Object.Destroy(value);
			else UnityEngine.Object.DestroyImmediate(value);
		}

		public void Dispose()
		{
			Camera.onPreCull -= BeforeCamera;
			Camera.onPostRender -= AfterCamera;
			foreach (Part part in parts)
			{
				if (part.Source != null) part.Source.forceRenderingOff = part.OriginalForceOff;
				if (part.Headless != null) Release(part.Headless);
				foreach (Material material in part.OwnedMaterials) Release(material);
			}
			if (root != null)
			{
				root.gameObject.SetActive(false);
				root.SetParent(null, true);
				Release(root.gameObject);
			}
		}
	}
}
