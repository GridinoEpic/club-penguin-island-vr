using ClubPenguin.Avatar;
using System;
using UnityEngine;

namespace ClubPenguin.ClothingDesigner.ItemCustomizer
{
	public class CustomizerChild : MonoBehaviour
	{
		private AvatarViewDistinctChild avatarViewDistinctChild;

		private SkinnedMeshRenderer smr;

		private MeshCollider meshCollider;
        private Mesh posedColliderMesh;
        private Mesh colliderSourceMesh;
        private Matrix4x4[] colliderBonePoses;
        private float nextColliderPoseCheck;
        public int ColliderPoseBakeCount { get; private set; }

		public int SlotIndex
		{
			get
			{
				return avatarViewDistinctChild.SlotIndex;
			}
		}

		public Material SharedMaterial
		{
			get
			{
				return smr.sharedMaterial;
			}
		}

		public bool HasEquipment
		{
			get
			{
				return avatarViewDistinctChild.Model[avatarViewDistinctChild.SlotIndex, avatarViewDistinctChild.PartIndex].Equipment != null;
			}
		}

		public event Action<Renderer, DecalMaterialProperties[]> EquipmentSet;

		private void Awake()
		{
			avatarViewDistinctChild = GetComponent<AvatarViewDistinctChild>();
		}

		private void OnEnable()
		{
			avatarViewDistinctChild.OnReady += avatarBaseAsync_OnReady;
		}

		private void OnDisable()
		{
			avatarViewDistinctChild.OnReady -= avatarBaseAsync_OnReady;
		}

		private void avatarBaseAsync_OnReady(AvatarBaseAsync avatarBase)
		{
			if (HasEquipment)
			{
				smr = GetComponent<SkinnedMeshRenderer>();
				smr.updateWhenOffscreen = true;
				if (meshCollider == null)
				{
					meshCollider = base.gameObject.AddComponent<MeshCollider>();
				}
				meshCollider.enabled = true;
                RefreshColliderPose(true);
				if (this.EquipmentSet != null)
				{
					this.EquipmentSet(smr, avatarViewDistinctChild.ViewPart.GetDefaultDecalProps());
				}
			}
		}

		public void Reset()
		{
			if (meshCollider != null)
			{
				meshCollider.enabled = false;
			}
		}

        public void RefreshColliderPose(bool force = false)
        {
            if (smr == null || smr.sharedMesh == null || meshCollider == null || !meshCollider.enabled) return;
            // Collision cooking is expensive. Refresh only when a pointer needs
            // a target, at most ten times per second, and only if the pose changed.
            if (!force && colliderSourceMesh == smr.sharedMesh && Time.unscaledTime < nextColliderPoseCheck) return;
            nextColliderPoseCheck = Time.unscaledTime + 0.1f;
            Transform[] bones = smr.bones;
            bool changed = force || colliderSourceMesh != smr.sharedMesh || colliderBonePoses == null || colliderBonePoses.Length != bones.Length;
            if (colliderBonePoses == null || colliderBonePoses.Length != bones.Length) colliderBonePoses = new Matrix4x4[bones.Length];
            Matrix4x4 local = smr.transform.worldToLocalMatrix;
            for (int i = 0; i < bones.Length; i++)
            {
                Matrix4x4 pose = bones[i] != null ? local * bones[i].localToWorldMatrix : Matrix4x4.identity;
                if (colliderBonePoses[i] != pose) changed = true;
                colliderBonePoses[i] = pose;
            }
            if (!changed) return;
            if (posedColliderMesh == null) posedColliderMesh = new Mesh { name = "Customizer clothing hit mesh" };
            smr.BakeMesh(posedColliderMesh);
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = posedColliderMesh;
            colliderSourceMesh = smr.sharedMesh;
            ColliderPoseBakeCount++;
        }

        private void OnDestroy()
        {
            if (posedColliderMesh != null)
            {
                if (Application.isPlaying) Destroy(posedColliderMesh);
                else DestroyImmediate(posedColliderMesh);
            }
        }
	}
}
