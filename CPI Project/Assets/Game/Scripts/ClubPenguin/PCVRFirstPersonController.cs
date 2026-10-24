using System.Collections.Generic;
using System.Reflection;
using ClubPenguin.Cinematography;
using ClubPenguin.Core;
using ClubPenguin.Locomotion;
using ClubPenguin.Props;
using ClubPenguin.UI;
using Disney.LaunchPadFramework;
using Disney.MobileNetwork;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using InputSystemXRController = UnityEngine.InputSystem.XR.XRController;

namespace ClubPenguin
{
	/// <summary>
	/// PCVR first-person bridge for the existing local penguin. It is inert in
	/// non-XR play and only takes over the camera after an OpenXR headset starts.
	/// </summary>
	[DefaultExecutionOrder(10000)]
	public sealed class PCVRFirstPersonController : MonoBehaviour
	{
		[SerializeField] private float snapTurnDegrees = 30f;
		[SerializeField] private float snapTurnCooldown = 0.28f;
		[SerializeField, Range(0f, 0.5f)] private float movementStickDeadzone = 0.15f;
		[SerializeField, Range(0.7f, 1f)] private float penguinHeightScale = 0.9f;
		[SerializeField] private float flipperLength = 0.48f;
		[SerializeField, Range(0f, 25f)] private float flipperGripBendDegrees = 14f;

		private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
		private readonly Dictionary<int, WorldCanvasAnchor> worldAnchoredMenuCanvases = new Dictionary<int, WorldCanvasAnchor>();
		private readonly Dictionary<Transform, Vector3[]> flipperRestVertices = new Dictionary<Transform, Vector3[]>();
		private readonly Dictionary<Transform, Vector3[]> flipperAnimatedVertices = new Dictionary<Transform, Vector3[]>();
		private PCVRAvatarRenderer avatarRenderer;
		private GameObject menuPenguin;
		private PCVRAvatarRenderer menuAvatarRenderer;
		private readonly Dictionary<Transform, CanvasGroup> menuVisibilityGroups = new Dictionary<Transform, CanvasGroup>();
		private readonly HashSet<int> enlargedQuestLabels = new HashSet<int>();
        private readonly HashSet<int> transientUiIds = new HashSet<int>();
		private readonly HashSet<int> currentTransientUiIds = new HashSet<int>();
		private float nextMenuVisibilityScan;
		private GameObject player;
		private GameObject bodyVisibilityPlayer;
		private Camera playerCamera;
		private bool cameraAttached;
		private float nextSnapTurnTime;
		private bool snapTurnArmed = true;
		private float nextWaitingLogTime;
		private float nextTransientUiScanTime;
		private bool transientUiActiveCached;
		private string lastUiVisibilityScene;
		private float nextPenguinColorCheckTime;
		private Camera menuTrackingCamera;
		private PCVRMenuEnvironment menuEnvironment;
		private bool menuEnvironmentLoadAttempted;
		private Vector3 menuTrackingOriginPosition;
		private Vector3 menuHeadPositionOrigin;
		private Quaternion menuTrackingOriginRotation = Quaternion.identity;
		private Transform closetPreview;
		private InputDevice leftHand;
		private InputDevice rightHand;
		private InputDevice headDevice;
		private Vector3 previousLeftSwimPosition;
		private Vector3 previousRightSwimPosition;
		private bool hasLeftSwimPosition;
		private bool hasRightSwimPosition;
		private float flipperSwimPower;
		private GameObject swimInputPlayer;
		private Quaternion trackingHeadRotationOrigin = Quaternion.identity;
		private Quaternion movementHeadingOriginRotation = Quaternion.identity;
		private bool hasTrackingHeadingOrigin;
		private Transform leftFlipper;
		private Transform rightFlipper;
		private Material flipperMaterial;
		private Material flipperDetailMaterial;
		private Color currentFlipperColor;
		private bool hasFlipperColor;
		private LineRenderer leftAimRay;
		private LineRenderer rightAimRay;
		private int uiPointerCacheFrame = -1;
		private bool uiPointerActiveThisFrame;
		private Canvas vrKeyboardCanvas;
		private RectTransform vrKeyboardRect;
		private InputField vrKeyboardTarget;
		private bool vrKeyboardShift;
		private readonly List<KeyboardKey> vrKeyboardKeys = new List<KeyboardKey>();
		private bool leftTriggerWasPressed;
		private bool rightTriggerWasPressed;
		private bool leftPrimaryWasPressed;
		private bool leftSecondaryWasPressed;
		private bool rightPrimaryWasPressed;
		private bool rightSecondaryWasPressed;
		private bool leftUiToggleWasPressed;
		private bool rightUiToggleWasPressed;
        private float snowballGripStart;
        private static Transform activeShopAnchor;
        private static Vector3 shopApproachPosition;
        private static Vector3 shopPanelPosition;
        private static Quaternion shopPanelRotation;
        public static void SetShopAnchor(Transform shop, Transform visitor)
        {
            activeShopAnchor = visitor != null ? shop : null;
            if (activeShopAnchor == null) return;
            shopApproachPosition = visitor.position;
            if (instance != null && instance.playerCamera != null) shopApproachPosition = instance.playerCamera.transform.position;
            // Capture the actual head position, including room-scale leaning, once
            // when opening. Keep the panel fixed after the visitor starts moving.
            Vector3 normal = Vector3.ProjectOnPlane(shop.forward, Vector3.up).normalized;
            Vector3 towardVisitor = Vector3.ProjectOnPlane(shopApproachPosition - shop.position, Vector3.up);
            if (normal.sqrMagnitude < 0.001f) normal = towardVisitor.normalized;
            if (normal.sqrMagnitude < 0.001f) normal = Vector3.back;
            if (Vector3.Dot(normal, towardVisitor) < 0f) normal = -normal;
            shopPanelRotation = Quaternion.LookRotation(-normal, Vector3.up);
            shopPanelPosition = GetClearShopPanelPosition(shopApproachPosition, shopApproachPosition - normal * 0.9f, visitor);
        }
        private static Vector3 GetClearShopPanelPosition(Vector3 headPosition, Vector3 desiredPosition, Transform visitor)
        {
            const float wallClearance = 0.12f;
            Vector3 offset = desiredPosition - headPosition;
            float distance = offset.magnitude;
            if (distance < 0.001f) return desiredPosition;
            Vector3 direction = offset / distance;
            float closest = float.PositiveInfinity;
            // Include a small margin beyond the panel so it cannot sit flush on
            // a wall. Interaction triggers and the visitor's body are not walls.
            foreach (RaycastHit hit in Physics.RaycastAll(headPosition, direction, distance + wallClearance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (visitor != null && hit.collider.transform.IsChildOf(visitor)) continue;
                if (hit.distance < closest) closest = hit.distance;
            }
            if (float.IsPositiveInfinity(closest)) return desiredPosition;
            // When extremely close, reduce the margin rather than pushing the
            // panel behind the head or enforcing a minimum through the wall.
            float clearDistance = closest - Mathf.Min(wallClearance, closest * 0.5f);
            return headPosition + direction * Mathf.Min(distance, clearDistance);
        }
        public static void SetShopAnchorFromInteraction(GameObject visitor)
        {
            // Popup actions run on the avatar, not the storefront. Capture the
            // sequence's trigger before Completed() releases that association.
            GameObject shop = SceneRefs.ActionSequencer != null ? SceneRefs.ActionSequencer.GetTrigger(visitor) : null;
            SetShopAnchor(shop != null ? shop.transform : null, visitor != null ? visitor.transform : null);
        }
        public static bool TryGetSnowballHand(GameObject owner, out Vector3 position, out Vector3 direction)
        {
            position = direction = Vector3.zero;
            if (instance == null || !XRSettings.enabled || instance.player != owner) return false;
            InputDevice hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!hand.isValid || !hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p) ||
                !hand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r)) return false;
            position = instance.GetMenuAimOrigin(XRNode.RightHand, null, p);
            direction = instance.GetMenuAimDirection(XRNode.RightHand, r).normalized;
            LocalPenguinSnowballThrower thrower = owner.GetComponent<LocalPenguinSnowballThrower>();
            if (thrower != null && instance.avatarRenderer != null &&
                instance.avatarRenderer.TryGetAttachmentPosition(thrower.LauncherBone, out Vector3 socketPosition))
                position = socketPosition;
            return true;
        }
		private bool vrUiManuallyVisible;
		private bool transientUiWasActive;
        private bool automaticUiSuppressed;
        private bool tutorialUiActiveCached;
        private bool UiVisible => IsMainMenuScene(SceneManager.GetActiveScene().name) || tutorialUiActiveCached ||
            vrUiManuallyVisible || (transientUiActiveCached && !automaticUiSuppressed);
        private Camera voidCamera;
        private CameraClearFlags savedClearFlags;
        private Color savedBackground;
        private int savedCullingMask;
        private bool presentationWasActive;
		private static PCVRFirstPersonController instance;
		public static bool TryGetInteractionPromptPose(out Vector3 position, out Quaternion rotation)
		{
			position = Vector3.zero;
			rotation = Quaternion.identity;
			if (instance == null || !instance.isActiveAndEnabled || !XRSettings.enabled || instance.playerCamera == null) return false;
			foreach (XRNode node in new[] { XRNode.RightHand, XRNode.LeftHand })
			{
				InputDevice hand = InputDevices.GetDeviceAtXRNode(node);
				if (!hand.isValid || (hand.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) ||
					!hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 handPosition)) continue;
				position = instance.TrackingToWorldPosition(handPosition) + Vector3.up * 0.12f;
				rotation = instance.playerCamera.transform.rotation;
				return true;
			}
			return false;
		}
		public static bool OwnsMovement => instance != null && instance.isActiveAndEnabled &&
			XRSettings.enabled && instance.cameraAttached && instance.player != null;
		private readonly PCVRPointer leftPointer = new PCVRPointer(-101);
		private readonly PCVRPointer rightPointer = new PCVRPointer(-102);
        private ClubPenguin.MiniGames.Jigsaw.JigsawController pointerPuzzle;
        private float nextPointerPuzzleScan;
		private Canvas[] cachedCanvases = new Canvas[0];
		private float nextCanvasScanTime;
		private readonly List<ClubPenguin.TutorialUI.TutorialOverlay> targetedTutorials = new List<ClubPenguin.TutorialUI.TutorialOverlay>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
		private Vector3 trackingPositionOrigin;
		private Vector2 previousMoveAxis;

		private struct WorldCanvasAnchor
		{
			public Canvas Canvas;
			public Vector3 Position;
			public Quaternion Rotation;
			public Vector3 Scale;
			public Vector3 NormalScale;
			public bool HasVisibleGraphics;
			public bool FollowView;
            public Vector2 DesignSize;
			public float FollowDistance;
			public CanvasGroup VrVisibilityGroup;
		}

		private struct KeyboardKey
		{
			public Button Button;
			public string Value;
			public bool IsLetter;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void CreatePersistentController()
		{
			if (FindFirstObjectByType<PCVRFirstPersonController>() != null)
			{
				return;
			}

			GameObject controllerObject = new GameObject("CPI PCVR First Person Controller");
			DontDestroyOnLoad(controllerObject);
			controllerObject.AddComponent<PCVRFirstPersonController>();
		}

		private void OnEnable()
		{
			instance = this;
			if (GetComponent<PCVRMovementDriver>() == null) gameObject.AddComponent<PCVRMovementDriver>();
			Application.onBeforeRender += ApplyTrackedHeadPose;
			Application.onBeforeRender += EnforceAnchoredMenuCanvases;
		}

		private void OnDisable()
		{
			ResetFlipperSwimming();
            RestorePresentation();
			leftPointer.Cancel();
			rightPointer.Cancel();
            CancelPuzzlePointers();
			Application.onBeforeRender -= ApplyTrackedHeadPose;
			Application.onBeforeRender -= EnforceAnchoredMenuCanvases;
			RestoreLocalPenguinBody();
			SetFallbackFlippersVisible(false);
			SetAimRaysEnabled(false);
			RemoveMenuEnvironment();
		}

		private void LateUpdate()
		{
			if (!XRSettings.enabled)
			{
				leftPointer.Cancel();
				rightPointer.Cancel();
                CancelPuzzlePointers();
				SetAimRaysEnabled(false);
				RestoreLocalPenguinBody();
				SetFallbackFlippersVisible(false);
				RemoveMenuEnvironment();
                RestorePresentation();
				return;
			}

			if (player == null) cameraAttached = false;
			if (SceneManager.GetActiveScene().name == "ClothingDesigner") { cameraAttached = false; player = null; }
			if (!cameraAttached)
			{
				playerCamera = Camera.main;
			}
			UpdateMenuEnvironment();
			if (!cameraAttached)
			{
				ApplyTrackedHeadPose();
			}
			EnsureFlipperNearClip(playerCamera);
			MakeSceneCamerasXrCompatible();
			bool attachedToPlayer = TryAttachToLocalPlayer();
			// Hit testing and the head-following UI must use the same camera pose.
			ApplyTrackedHeadPose();
			AnchorScreenSpaceUiInWorld();
			UpdateVrUiToggles();
			UpdateVrKeyboard();
			Canvas.ForceUpdateCanvases();
            RefreshQuestNavigation();
            InsetActionControls();

			if (!attachedToPlayer)
			{
				RestoreLocalPenguinBody();
				// The Home scene presents the main menu before a local penguin exists.
				// Still show tracked flippers and let their triggers operate the menu.
				UpdateMenuPointers();
				LogWaitingForPlayer();
				return;
			}

			UpdateLocalPenguinAvatar();
			UpdateFlippers();
			avatarRenderer?.PoseTwoHandedProp();
            // The held object must use this frame's final IK pose.
            player.GetComponent<LocalPenguinSnowballThrower>()?.UpdateVrHeldSnowball();
		}

		private void UpdateMenuEnvironment()
		{
			if (SceneManager.GetActiveScene().name != "Home")
			{
				RemoveMenuEnvironment();
				return;
			}
			if (playerCamera == null || menuEnvironment != null || menuEnvironmentLoadAttempted) return;
			menuEnvironmentLoadAttempted = true;
			PCVRMenuEnvironment prefab = Resources.Load<PCVRMenuEnvironment>("PCVR/TownMenuEnvironment");
			if (prefab == null)
			{
				Debug.LogError("CPI PCVR: Town menu scenery is missing. Run CPI VR > Rebuild Town Menu Scenery.");
				return;
			}
			if (menuTrackingCamera != playerCamera)
			{
				menuTrackingCamera = playerCamera;
				menuTrackingOriginPosition = playerCamera.transform.position;
				menuTrackingOriginRotation = playerCamera.transform.rotation;
			}
			menuEnvironment = Instantiate(prefab);
			menuEnvironment.PlaceAround(playerCamera, menuTrackingOriginPosition, menuTrackingOriginRotation);
			Debug.Log("CPI PCVR: main menu placed in the Town plaza.");
		}

		private void RemoveMenuEnvironment()
		{
			menuAvatarRenderer?.Dispose();
			menuAvatarRenderer = null;
			if (menuPenguin != null) Destroy(menuPenguin);
			menuPenguin = null;
			if (menuEnvironment != null) Destroy(menuEnvironment.gameObject);
			menuEnvironment = null;
			menuEnvironmentLoadAttempted = false;
		}

		private void MakeSceneCamerasXrCompatible()
		{
			string sceneName = SceneManager.GetActiveScene().name;
			Camera[] cameras = Camera.allCameras;
			for (int i = 0; i < cameras.Length; i++)
			{
				Camera sceneCamera = cameras[i];
				if (sceneCamera == null || sceneCamera.targetTexture != null)
				{
					continue;
				}

				if (sceneCamera != playerCamera)
				{
					// UI and preview cameras retain their authored projection. They must
					// not clear the headset after the main stereo camera has rendered.
					sceneCamera.stereoTargetEye = StereoTargetEyeMask.None;
					continue;
				}
				sceneCamera.stereoTargetEye = StereoTargetEyeMask.Both;
				sceneCamera.rect = new Rect(0f, 0f, 1f, 1f);
				if (sceneCamera.orthographic)
				{
					sceneCamera.orthographic = false;
					Debug.LogWarning($"CPI PCVR: changed scene camera '{sceneCamera.name}' to perspective for OpenXR ({sceneName}).");
				}
			}
		}

		private void AnchorScreenSpaceUiInWorld()
		{
			string activeSceneName = SceneManager.GetActiveScene().name;
			bool isMainMenuScene = IsMainMenuScene(activeSceneName);
			if (activeSceneName != lastUiVisibilityScene)
			{
				transientUiWasActive = false;
                nextMenuVisibilityScan = 0f;
                nextCanvasScanTime = 0f;
				nextTransientUiScanTime = 0f;
				bool wasMainMenuScene = IsMainMenuScene(lastUiVisibilityScene);
				if (isMainMenuScene && !wasMainMenuScene)
				{
					// Home and boot controls always remain available.
					vrUiManuallyVisible = true;
					uiPointerCacheFrame = -1;
				}
				else if (wasMainMenuScene && !isMainMenuScene)
				{
					// Only navigation starts hidden; essential HUD remains visible.
					vrUiManuallyVisible = false;
					uiPointerCacheFrame = -1;
				}
			}
			lastUiVisibilityScene = activeSceneName;

			Camera mainCamera = playerCamera != null ? playerCamera : Camera.main;
			if (mainCamera == null)
			{
				return;
			}

			Canvas[] canvases = GetUiCanvases();
            bool showroom = IsShowroom();
			bool transientUiActive = HasAnyTransientUi(canvases);
			UpdateTransientUiVisibility(transientUiActive);
			UpdateMenuVisibility(canvases);
            UpdatePresentation(mainCamera);

			for (int i = 0; i < canvases.Length; i++)
			{
				Canvas menuCanvas = canvases[i];
				if (menuCanvas == null || !menuCanvas.isRootCanvas ||
					!menuCanvas.gameObject.scene.isLoaded ||
					menuCanvas.GetComponent<AvatarPositionTranslator>() != null)
				{
					continue;
				}

				int canvasId = menuCanvas.GetInstanceID();
				WorldCanvasAnchor anchor;
				if (worldAnchoredMenuCanvases.TryGetValue(canvasId, out anchor) && anchor.Canvas == menuCanvas)
				{
					bool hasVisibleGraphics = HasVisibleUiGraphics(menuCanvas);
					bool shouldBeVisible = true;
					if (shouldBeVisible && anchor.VrVisibilityGroup != null && anchor.VrVisibilityGroup.alpha <= 0.01f)
					{
						Vector3 menuForward = GetHorizontalViewForward(mainCamera.transform);
						anchor.Position = mainCamera.transform.position + menuForward * 1.6f;
						anchor.Rotation = Quaternion.LookRotation(menuForward, Vector3.up);
					}
					if ((!anchor.HasVisibleGraphics && hasVisibleGraphics) || (anchor.FollowView && showroom))
                    {
                        anchor.Position = mainCamera.transform.position + GetHorizontalViewForward(mainCamera.transform) * 1.6f;
                        anchor.Rotation = Quaternion.LookRotation(GetHorizontalViewForward(mainCamera.transform), Vector3.up);
                    }
                    UpdateCanvasPresentationScale(ref anchor, showroom);
                    if (showroom) SetShowroomPose(ref anchor);
                    if (activeSceneName != "Home") anchor.FollowView = !showroom;
                    anchor.HasVisibleGraphics = hasVisibleGraphics;
					worldAnchoredMenuCanvases[canvasId] = anchor;
					ApplyWorldCanvasAnchor(menuCanvas, anchor, mainCamera);
					SetVrCanvasVisibility(anchor, shouldBeVisible);
					continue;
				}
				worldAnchoredMenuCanvases.Remove(canvasId);

				// Inactive prefabs have not run their platform layout settings yet.
				// Wait for Awake before capturing their design resolution.
				if (!menuCanvas.gameObject.activeInHierarchy) continue;

				if (menuCanvas.renderMode == RenderMode.WorldSpace && activeSceneName != "Boot" && activeSceneName != "Loading")
				{
					continue;
				}

				RectTransform menuRect = menuCanvas.GetComponent<RectTransform>();
				if (menuRect == null)
				{
					continue;
				}

				CanvasScaler scaler = menuCanvas.GetComponent<CanvasScaler>();
                Vector2 designSize = GetCanvasDesignSize(menuRect, scaler);
                float uiScale = Mathf.Min(1.1f / designSize.y, 2f / designSize.x);
                // Bring edge anchors inward without shrinking text or buttons.
                if (menuCanvas.name == "TopCanvas" || menuCanvas.name == "GuiHudTopCanvas" ||
                    menuCanvas.name == "HudRightCanvas" || menuCanvas.name == "HudLeftCanvas")
                    designSize = Vector2.Scale(designSize, new Vector2(0.74f, 0.76f));

				Vector3 forward = GetHorizontalViewForward(mainCamera.transform);

				menuCanvas.renderMode = RenderMode.WorldSpace;
				CanvasGroup vrVisibilityGroup = menuCanvas.gameObject.AddComponent<CanvasGroup>();
				// Keep the menu independent of the scene services and screen-space camera.

				foreach (Graphic graphic in menuCanvas.GetComponentsInChildren<Graphic>(true))
                    graphic.gameObject.layer = LayerMask.NameToLayer("UI");
                menuCanvas.worldCamera = mainCamera;
				menuCanvas.overrideSorting = true;
				menuCanvas.pixelPerfect = false;
				GraphicRaycaster menuRaycaster = menuCanvas.GetComponent<GraphicRaycaster>();
				if (menuRaycaster == null)
				{
					menuRaycaster = menuCanvas.gameObject.AddComponent<GraphicRaycaster>();
				}
				menuRaycaster.enabled = true;
				// The menu was authored as a screen overlay. Once it is placed in
				// world space, accept the same controls from either side of its plane.
				menuRaycaster.ignoreReversedGraphics = false;
				CameraSpacePopupManager cameraSpacePopup = menuCanvas.GetComponent<CameraSpacePopupManager>();
				if (cameraSpacePopup != null)
				{
					// Camera-space dialogs are now world canvases in VR. Keep them above
					// the menu and stop the old mono popup camera drawing them as well.
					menuCanvas.sortingOrder = Mathf.Max(menuCanvas.sortingOrder, 50);
					if (cameraSpacePopup.PopupCamera != null && cameraSpacePopup.PopupCamera != mainCamera)
					{
						cameraSpacePopup.PopupCamera.enabled = false;
					}
				}
				menuRect.anchorMin = new Vector2(0.5f, 0.5f);
				menuRect.anchorMax = new Vector2(0.5f, 0.5f);
				menuRect.pivot = new Vector2(0.5f, 0.5f);
				menuRect.sizeDelta = designSize;
				menuRect.position = mainCamera.transform.position + forward * 1.6f;
				// Match Unity's screen-space canvas front so the text remains readable
				// from the tracked camera after moving it into world space.
				menuRect.rotation = Quaternion.LookRotation(forward, Vector3.up);
				menuRect.localScale = Vector3.one * uiScale;
				if (scaler != null)
				{
					scaler.enabled = false;
				}

				// Screen UI often contains nested canvases for tabs and controls. Give
				// each one a raycaster after converting the root into a world canvas.
				Canvas[] nestedCanvases = menuCanvas.GetComponentsInChildren<Canvas>(true);
				for (int nestedIndex = 0; nestedIndex < nestedCanvases.Length; nestedIndex++)
				{
					Canvas nestedCanvas = nestedCanvases[nestedIndex];
					if (nestedCanvas == null) continue;
					nestedCanvas.worldCamera = mainCamera;
					GraphicRaycaster nestedRaycaster = nestedCanvas.GetComponent<GraphicRaycaster>();
					if (nestedRaycaster == null) nestedRaycaster = nestedCanvas.gameObject.AddComponent<GraphicRaycaster>();
					nestedRaycaster.enabled = true;
					nestedRaycaster.ignoreReversedGraphics = false;
				}

				anchor = new WorldCanvasAnchor
				{
					Canvas = menuCanvas,
					Position = menuRect.position,
					Rotation = menuRect.rotation,
					Scale = Vector3.one * uiScale,
					NormalScale = Vector3.one * uiScale,
					HasVisibleGraphics = HasVisibleUiGraphics(menuCanvas),
                    DesignSize = designSize,
                    FollowView = activeSceneName != "Home" && !showroom,
					FollowDistance = 1.6f,
					VrVisibilityGroup = vrVisibilityGroup
				};
				if (showroom) SetShowroomPose(ref anchor);
                worldAnchoredMenuCanvases.Add(canvasId, anchor);
				ApplyWorldCanvasAnchor(menuCanvas, anchor, mainCamera);
				SetVrCanvasVisibility(anchor, true);
				EventSystem activeEventSystem = EventSystem.current;
				if (activeEventSystem != null)
				{
					activeEventSystem.sendNavigationEvents = false;
				}
				Debug.Log($"CPI PCVR: anchored screen UI canvas '{menuCanvas.name}' in front of the headset for VR rendering and pointer input.");
			}
		}

		private static bool IsMainMenuScene(string sceneName)
		{
			return sceneName == "Home" || sceneName == "Boot";
		}

        private static Vector2 GetCanvasDesignSize(RectTransform rect, CanvasScaler scaler)
        {
            // Screen-space rects depend on the monitor/XR render target and on
            // whether CanvasScaler has updated this frame. They are not stable
            // design units when a room creates its HUD asynchronously.
            Vector2 size = scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                ? scaler.referenceResolution : rect.rect.size;
            return size.x > 0f && size.y > 0f ? size : new Vector2(1920f, 1080f);
        }

		private static void SetVrCanvasVisibility(WorldCanvasAnchor anchor, bool visible)
		{
			CanvasGroup visibilityGroup = anchor.VrVisibilityGroup;
			if (visibilityGroup == null)
			{
				return;
			}

			visibilityGroup.alpha = visible ? 1f : 0f;
			visibilityGroup.interactable = visible;
			visibilityGroup.blocksRaycasts = visible;
		}

		private Vector3 GetHorizontalViewForward(Transform view)
		{
			Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
			if (forward.sqrMagnitude < 0.01f)
			{
				forward = Vector3.ProjectOnPlane(menuTrackingOriginRotation * Vector3.forward, Vector3.up).normalized;
			}
			if (forward.sqrMagnitude < 0.001f)
			{
				Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
				forward = Vector3.Cross(right, Vector3.up).normalized;
			}
			return forward.sqrMagnitude >= 0.001f ? forward : Vector3.forward;
		}

		private bool IsCanvasSuppressedByVrVisibility(Canvas canvas)
		{
			WorldCanvasAnchor anchor;
			if (canvas != null && worldAnchoredMenuCanvases.TryGetValue(canvas.GetInstanceID(), out anchor) &&
				anchor.Canvas == canvas && anchor.VrVisibilityGroup != null)
			{
				return !anchor.VrVisibilityGroup.blocksRaycasts || anchor.VrVisibilityGroup.alpha <= 0.01f;
			}
			return false;
		}

		private static bool HasVisibleUiGraphics(Canvas canvas)
		{
			Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(false);
			for (int i = 0; i < graphics.Length; i++)
			{
				if (graphics[i] != null && graphics[i].isActiveAndEnabled && graphics[i].color.a > 0f)
				{
					return true;
				}
			}
			return false;
		}

		private bool HasTransientUi(Canvas canvas)
		{
			if (canvas == null)
			{
				return false;
			}

			MonoBehaviour[] behaviours = canvas.GetComponentsInChildren<MonoBehaviour>(false);
			for (int i = 0; i < behaviours.Length; i++)
			{
				MonoBehaviour behaviour = behaviours[i];
				if (behaviour == null || !behaviour.isActiveAndEnabled)
				{
					continue;
				}

				string typeName = behaviour.GetType().Name;
                var tutorial = behaviour as ClubPenguin.TutorialUI.TutorialOverlay;
                if (tutorial != null && tutorial.IsShowing)
                {
                    // Restore the HUD controls the callout points to, even when grip
                    // previously hid navigation or suppressed another popup.
                    tutorialUiActiveCached = true;
                    currentTransientUiIds.Add(behaviour.GetInstanceID());
                }
				bool isScreen = behaviour is MarketplaceScreenController || behaviour is AbstractDisneyStoreController ||
                    behaviour is ClubPenguin.CellPhone.CellPhoneController || typeName.IndexOf("TutorialOverlay", System.StringComparison.OrdinalIgnoreCase) >= 0;
				if (!isScreen && (typeName.IndexOf("Manager", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("Handler", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("Service", System.StringComparison.OrdinalIgnoreCase) >= 0))
				{
					continue;
				}
				QuestMessageHud questMessageHud = behaviour as QuestMessageHud;
				if (questMessageHud != null && (questMessageHud.IsOpen || questMessageHud.IsOpening))
				{
					currentTransientUiIds.Add(behaviour.GetInstanceID());
				}

				bool isTransient = isScreen ||
					typeName.IndexOf("Popup", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("Dialog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("TutorialOverlay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					typeName.IndexOf("RewardPanel", System.StringComparison.OrdinalIgnoreCase) >= 0;
				if (!isTransient)
				{
					continue;
				}

				Graphic[] graphics = behaviour.GetComponentsInChildren<Graphic>(false);
				for (int graphicIndex = 0; graphicIndex < graphics.Length; graphicIndex++)
				{
					if (graphics[graphicIndex] != null && graphics[graphicIndex].isActiveAndEnabled &&
						graphics[graphicIndex].color.a > 0.01f)
					{
						currentTransientUiIds.Add(behaviour.GetInstanceID());
						break;
					}
				}
			}

			return currentTransientUiIds.Count > 0;
		}

		private bool HasAnyTransientUi(Canvas[] canvases)
		{
			if (Time.unscaledTime < nextTransientUiScanTime)
			{
				return transientUiActiveCached;
			}

			nextTransientUiScanTime = Time.unscaledTime + 0.1f;
			transientUiActiveCached = false;
            tutorialUiActiveCached = false;
			currentTransientUiIds.Clear();
			for (int i = 0; canvases != null && i < canvases.Length; i++)
			{
				if (HasTransientUi(canvases[i]))
				{
					transientUiActiveCached = true;
				}
			}
			foreach (int id in currentTransientUiIds)
				if (!transientUiIds.Contains(id)) { automaticUiSuppressed = false; uiPointerCacheFrame = -1; }
			transientUiIds.Clear();
			transientUiIds.UnionWith(currentTransientUiIds);
			return transientUiActiveCached;
		}

        private void InsetActionControls()
        {
            foreach (Canvas canvas in GetUiCanvases())
            {
                if (canvas == null || !canvas.isRootCanvas || canvas.renderMode != RenderMode.WorldSpace) continue;
                RectTransform root = (RectTransform)canvas.transform;
                foreach (Transform control in canvas.GetComponentsInChildren<Transform>(false))
                {
                    if (control.GetComponent<TrayInputButtonGroup>() == null && !control.name.StartsWith("ChatMenu")) continue;
                    RectTransform panel = control as RectTransform;
                    if (panel == null) continue;
                    Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, panel);
                    // Move the cluster as a unit; repeated clamping cannot drift.
                    Vector2 half = root.rect.size * 0.34f;
                    float x = Mathf.Clamp(bounds.center.x, root.rect.center.x - half.x + bounds.extents.x,
                        Mathf.Max(root.rect.center.x - half.x + bounds.extents.x, root.rect.center.x + half.x - bounds.extents.x));
                    float y = Mathf.Clamp(bounds.center.y, root.rect.center.y - half.y + bounds.extents.y,
                        Mathf.Max(root.rect.center.y - half.y + bounds.extents.y, root.rect.center.y + half.y - bounds.extents.y));
                    panel.position += root.TransformVector(new Vector3(x - bounds.center.x, y - bounds.center.y, 0f));
                }
            }
        }

        private static bool IsPhoneLauncher(Transform child)
        {
            return child.name == "CellPhoneButton" || child.name == "CellphoneButton";
        }

        private static bool IsMainNavigationBranch(Transform child)
        {
            MainNavStateHandler navigation = child.GetComponentInParent<MainNavStateHandler>(true);
            return navigation != null && (navigation.ButtonParent == child.gameObject ||
                navigation.TitleParent == child.gameObject || navigation.CloseButtonParent == child.gameObject ||
                // The blue bar and snow are a sibling of the buttons in both
                // navigation prefabs. Keep the handler's own CanvasGroup separate.
                (child.parent == navigation.transform && child.GetComponent<Graphic>() != null));
        }

        private void UpdateMenuVisibility(Canvas[] canvases)
        {
            if (Time.unscaledTime >= nextMenuVisibilityScan)
            {
                nextMenuVisibilityScan = Time.unscaledTime + 0.1f;
                foreach (Canvas canvas in canvases)
                {
                    if (canvas == null || !canvas.isRootCanvas) continue;
                    foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == "ObjectiveText" && enlargedQuestLabels.Add(child.GetInstanceID()))
                        {
                            Text label = child.GetComponent<Text>();
                            if (label != null) { label.fontSize = Mathf.RoundToInt(label.fontSize * 1.4f); label.resizeTextMaxSize = Mathf.RoundToInt(label.resizeTextMaxSize * 1.4f); }
                            child.localScale *= 1.25f;
                        }
                        if (!IsMainNavigationBranch(child) &&
                            child.GetComponent<MainNavButton>() == null &&
                            child.GetComponent<MarketplaceScreenController>() == null &&
                            child.GetComponent<AbstractDisneyStoreController>() == null &&
                            child.GetComponent<ClubPenguin.CellPhone.CellPhoneController>() == null &&
                            child.GetComponent<ChatBarController>() == null &&
                            child.GetComponent<TrayInputButtonGroup>() == null &&
                            child.GetComponent<TrayInputButton>() == null &&
                            child.name != "ScreenContainerContent" &&
                            !child.name.StartsWith("ChatMenu") && !child.name.StartsWith("ChatScreenPanel") &&
                            !IsPhoneLauncher(child)) continue;
                        if (!menuVisibilityGroups.TryGetValue(child, out CanvasGroup group) || group == null)
                        {
                            menuVisibilityGroups[child] = child.gameObject.AddComponent<CanvasGroup>();
                        }
                    }
                }
            }
            foreach (var item in menuVisibilityGroups)
            {
                CanvasGroup group = item.Value;
                if (group == null) continue;
                if (IsPhoneLauncher(item.Key))
                {
                    // Nested canvases may ignore ancestor CanvasGroups. Never let this
                    // keyboard-only launcher participate in either hand's hit testing.
                    foreach (Graphic graphic in item.Key.GetComponentsInChildren<Graphic>(true))
                        graphic.raycastTarget = false;
                }
                bool visible = !IsPhoneLauncher(item.Key) && (IsMainMenuScene(SceneManager.GetActiveScene().name) || tutorialUiActiveCached || ((item.Key.name == "ScreenContainerContent" || item.Key.GetComponent<MarketplaceScreenController>() != null ||
                    item.Key.GetComponent<AbstractDisneyStoreController>() != null || item.Key.GetComponent<ClubPenguin.CellPhone.CellPhoneController>() != null) ? UiVisible : vrUiManuallyVisible));
                group.alpha = visible ? 1f : 0f;
                group.blocksRaycasts = group.interactable = visible;
            }
        }

        private static void UpdateCanvasPresentationScale(ref WorldCanvasAnchor anchor, bool showroom)
        {
            if (showroom && anchor.DesignSize.x > 0f && anchor.DesignSize.y > 0f)
                anchor.Scale = Vector3.one * Mathf.Min(0.72f / anchor.DesignSize.y, 1.25f / anchor.DesignSize.x);
            else if (anchor.NormalScale.x > 0f)
                anchor.Scale = anchor.NormalScale;
        }

        private void SetShowroomPose(ref WorldCanvasAnchor anchor)
        {
            UpdateCanvasPresentationScale(ref anchor, true);
            if (SceneManager.GetActiveScene().name == "ClothingDesigner")
            {
                SetClosetPose(ref anchor);
                return;
            }
            if (activeShopAnchor == null) return;
            anchor.Position = shopPanelPosition;
            anchor.Rotation = shopPanelRotation;
        }

        private void SetClosetPose(ref WorldCanvasAnchor anchor)
        {
            UpdateCanvasPresentationScale(ref anchor, true);
            var designerCamera = FindFirstObjectByType<ClubPenguin.ClothingDesigner.ItemCustomizer.ClothingDesignerCameraController>();
            bool creating = designerCamera != null && designerCamera.CurrentCameraState != ClubPenguin.ClothingDesigner.ClothingDesignerCameraState.Inventory;
            string previewName = creating ? "CustomizerPenguinPreview" : "InventoryPenguinPreview";
            if (closetPreview == null || !closetPreview.gameObject.activeInHierarchy || closetPreview.name != previewName)
            {
                GameObject preview = GameObject.Find(previewName);
                closetPreview = preview != null ? preview.transform : null;
            }
            // The feet are at floor level. Present the closet as a tilted tray
            // extending toward the viewer, so it appears below the penguin
            // without burying any part of the canvas beneath the ground.
            anchor.Scale *= 0.85f;
            Vector3 forward = Vector3.ProjectOnPlane(menuTrackingOriginRotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 feet = closetPreview != null ? closetPreview.position : menuTrackingOriginPosition + forward * 0.85f;
            float halfHeight = anchor.DesignSize.y * anchor.Scale.y * 0.5f;
            anchor.Rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(60f, 0f, 0f);
            Vector3 panelUp = anchor.Rotation * Vector3.up;
            anchor.Position = feet - forward * (0.12f + halfHeight * Vector3.Dot(panelUp, forward))
                + Vector3.up * (0.1f + halfHeight * panelUp.y);
            anchor.FollowView = false;
        }

        private bool IsShowroom()
        {
            return SceneManager.GetActiveScene().name == "ClothingDesigner" || HasModalPopup();
        }

        private void RestorePresentation()
        {
            if (voidCamera != null)
            {
                voidCamera.clearFlags = savedClearFlags;
                voidCamera.backgroundColor = savedBackground;
                voidCamera.cullingMask = savedCullingMask;
            }
            voidCamera = null;
        }

        private void UpdatePresentation(Camera camera)
        {
            string scene = SceneManager.GetActiveScene().name;
            bool active = scene == "Boot" || scene == "Loading" ||
                FindFirstObjectByType<Disney.Kelowna.Common.LoadingController>() != null;
            if (voidCamera != null && (voidCamera != camera || !active)) RestorePresentation();
            if (active && camera != null)
            {
                if (voidCamera == null)
                {
                    voidCamera = camera;
                    savedClearFlags = camera.clearFlags;
                    savedBackground = camera.backgroundColor;
                    savedCullingMask = camera.cullingMask;
                }
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 95f / 255f, 201f / 255f, 1f);
                camera.cullingMask = LayerMask.GetMask("UI", "ChatUI");
                if (!presentationWasActive)
                {
                    foreach (int id in new List<int>(worldAnchoredMenuCanvases.Keys))
                    {
                        WorldCanvasAnchor anchor = worldAnchoredMenuCanvases[id];
                        anchor.Position = camera.transform.position + GetHorizontalViewForward(camera.transform) * 1.6f;
                        anchor.Rotation = Quaternion.LookRotation(GetHorizontalViewForward(camera.transform), Vector3.up);
                        worldAnchoredMenuCanvases[id] = anchor;
                    }
                }
            }
            presentationWasActive = active;
        }

		private void ApplyWorldCanvasAnchor(Canvas canvas, WorldCanvasAnchor anchor, Camera worldCamera)
		{
			if (canvas == null)
			{
				return;
			}

			canvas.renderMode = RenderMode.WorldSpace;
			canvas.worldCamera = worldCamera;
			RectTransform rect = canvas.GetComponent<RectTransform>();
			if (rect == null)
			{
				return;
			}

			if (anchor.DesignSize.x > 0f && anchor.DesignSize.y > 0f)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = anchor.DesignSize;
            }
            if (anchor.FollowView && worldCamera != null)
			{
                // Follow horizontal head turns while keeping the UI level.
                Vector3 forward = Vector3.ProjectOnPlane(worldCamera.transform.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                {
                    // Retain the last heading when looking straight up or down.
                    forward = Vector3.ProjectOnPlane(rect.forward, Vector3.up);
                    if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                }
                forward.Normalize();
				float distance = anchor.FollowDistance > 0f ? anchor.FollowDistance : 1.6f;
				rect.SetPositionAndRotation(worldCamera.transform.position + forward * distance,
					Quaternion.LookRotation(forward, Vector3.up));
			}
			else
			{
				// World-anchored helper canvases such as the login keyboard keep their
				// explicitly assigned pose.
				rect.SetPositionAndRotation(anchor.Position, anchor.Rotation);
			}
			Vector3 parentScale = rect.parent != null ? rect.parent.lossyScale : Vector3.one;
			rect.localScale = new Vector3(anchor.Scale.x / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
				anchor.Scale.y / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
				anchor.Scale.z / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
		}

		private void EnforceAnchoredMenuCanvases()
		{
			if (!XRSettings.enabled)
			{
				return;
			}

			Camera mainCamera = Camera.main;
			foreach (KeyValuePair<int, WorldCanvasAnchor> item in worldAnchoredMenuCanvases)
			{
				Canvas canvas = item.Value.Canvas;
				ApplyWorldCanvasAnchor(canvas, item.Value, mainCamera);
				if (canvas != null)
				{
					CameraSpacePopupManager cameraSpacePopup = canvas.GetComponent<CameraSpacePopupManager>();
					if (cameraSpacePopup != null && cameraSpacePopup.PopupCamera != null &&
						cameraSpacePopup.PopupCamera != mainCamera)
					{
						cameraSpacePopup.PopupCamera.enabled = false;
					}
				}
			}
            RefreshQuestNavigation();
		}

        private void RefreshQuestNavigation()
        {
            foreach (Canvas canvas in GetUiCanvases())
            {
                if (canvas == null || !canvas.isRootCanvas || canvas.renderMode != RenderMode.WorldSpace) continue;
                foreach (QuestNavigation navigation in canvas.GetComponentsInChildren<QuestNavigation>(false))
                {
                    navigation.RefreshWorldSpaceIndicator();
                }
            }
        }

		private bool IsVrUiPointerActive()
		{
			// Informational tutorials can disable every shop button. Their
			// dismissal gesture still owns the trigger and needs a visible laser.
			if (tutorialUiActiveCached && ClubPenguin.Adventure.InputAnyKeyUpAction.IsWaitingForInput) return true;
			if (!UiVisible && SceneManager.GetActiveScene().name != "ClothingDesigner") return false;
			if (uiPointerCacheFrame == Time.frameCount)
			{
				return uiPointerActiveThisFrame;
			}

			uiPointerCacheFrame = Time.frameCount;
			uiPointerActiveThisFrame = false;
			Canvas[] canvases = GetUiCanvases();
			for (int i = 0; i < canvases.Length; i++)
			{
				Canvas canvas = canvases[i];
				GraphicRaycaster raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
				if (canvas != null && canvas.isActiveAndEnabled && canvas.enabled && canvas.gameObject.activeInHierarchy &&
					!IsCanvasSuppressedByVrVisibility(canvas.rootCanvas) &&
					canvas.renderMode == RenderMode.WorldSpace && raycaster != null && raycaster.isActiveAndEnabled &&
					HasClickableUi(canvas))
				{
					uiPointerActiveThisFrame = true;
					break;
				}
			}
			return uiPointerActiveThisFrame;
		}

        private static bool HasClickableUi(Canvas canvas)
        {
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.isActiveAndEnabled || !graphic.raycastTarget || graphic.canvasRenderer.GetInheritedAlpha() <= 0.01f ||
                    !HasUiRaycastArea(graphic.rectTransform)) continue;
                bool blocked = false;
                foreach (CanvasGroup group in graphic.GetComponentsInParent<CanvasGroup>())
                    if (!group.interactable || !group.blocksRaycasts || group.alpha <= 0.01f) { blocked = true; break; }
                if (!blocked && (ExecuteEvents.GetEventHandler<IPointerClickHandler>(graphic.gameObject) != null ||
                    ExecuteEvents.GetEventHandler<IPointerDownHandler>(graphic.gameObject) != null ||
                    ExecuteEvents.GetEventHandler<IDragHandler>(graphic.gameObject) != null)) return true;
            }
            return false;
        }

        private static bool HasUiRaycastArea(RectTransform rect)
        {
            // Animated widgets such as PatternToggle remain active at scale zero.
            // Their singular transform maps every hit to local (0, 0), falsely
            // placing the ray inside the hidden widget and all of its children.
            // Require two independent world-space axes before testing local bounds.
            return rect.rect.width > 0f && rect.rect.height > 0f &&
                Vector3.Cross(rect.TransformVector(Vector3.right), rect.TransformVector(Vector3.up)).sqrMagnitude > 0f;
        }

		private void SetAimRaysEnabled(bool enabled)
		{
			if (leftAimRay != null) leftAimRay.enabled = enabled;
			if (rightAimRay != null) rightAimRay.enabled = enabled;
		}

		private void UpdateVrKeyboard()
		{
			if (!vrUiManuallyVisible) return;
			if (SceneManager.GetActiveScene().name != "Home")
			{
				vrKeyboardTarget = null;
				DestroyVrKeyboard();
				return;
			}

			InputField focusedField = vrKeyboardTarget != null && vrKeyboardTarget.isActiveAndEnabled
				? vrKeyboardTarget
				: null;
			InputField[] fields = FindObjectsByType<InputField>(FindObjectsSortMode.None);
			for (int i = 0; focusedField == null && i < fields.Length; i++)
			{
				if (fields[i] != null && fields[i].isActiveAndEnabled && fields[i].isFocused)
				{
					focusedField = fields[i];
					break;
				}
			}
			if (focusedField == null)
			{
				EventSystem eventSystem = EventSystem.current;
				GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
				InputField selectedField = selected != null ? selected.GetComponentInParent<InputField>() : null;
				if (selectedField != null && selectedField.isActiveAndEnabled)
				{
					focusedField = selectedField;
				}
			}

			if (focusedField != null)
			{
				vrKeyboardTarget = focusedField;
			}
			else if (vrKeyboardTarget == null || !vrKeyboardTarget.isActiveAndEnabled)
			{
				vrKeyboardTarget = null;
				DestroyVrKeyboard();
				return;
			}

			if (vrKeyboardTarget == null || !vrKeyboardTarget.isActiveAndEnabled)
			{
				return;
			}

			if (vrKeyboardCanvas == null)
			{
				CreateVrKeyboard();
			}
			PositionVrKeyboard();
		}

		private void HideVrKeyboardWhenClickingOutside(GameObject hitObject)
		{
			if (vrKeyboardCanvas == null || hitObject == null ||
				hitObject.transform.IsChildOf(vrKeyboardCanvas.transform))
			{
				return;
			}
			InputField clickedField = hitObject.GetComponentInParent<InputField>();
			if (vrKeyboardTarget != null && clickedField == vrKeyboardTarget)
			{
				return;
			}
			if (vrKeyboardTarget != null)
			{
				vrKeyboardTarget.DeactivateInputField();
			}
			if (EventSystem.current != null)
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
			vrKeyboardTarget = null;
			DestroyVrKeyboard();
		}

		private void CreateVrKeyboard()
		{
			GameObject keyboardObject = new GameObject("CPI VR Login Keyboard", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			int keyboardLayer = vrKeyboardTarget != null ? vrKeyboardTarget.gameObject.layer : LayerMask.NameToLayer("UI");
			keyboardObject.layer = keyboardLayer >= 0 ? keyboardLayer : 0;
			vrKeyboardCanvas = keyboardObject.GetComponent<Canvas>();
			vrKeyboardCanvas.renderMode = RenderMode.WorldSpace;
			vrKeyboardCanvas.worldCamera = Camera.main;
			vrKeyboardCanvas.overrideSorting = true;
			vrKeyboardCanvas.sortingOrder = 250;
			vrKeyboardRect = keyboardObject.GetComponent<RectTransform>();
			vrKeyboardRect.sizeDelta = new Vector2(1000f, 500f);
			vrKeyboardRect.localScale = Vector3.one * 0.0008f;

			GameObject panelObject = new GameObject("Keyboard Background", typeof(RectTransform), typeof(Image));
			panelObject.layer = keyboardObject.layer;
			RectTransform panelRect = panelObject.GetComponent<RectTransform>();
			panelRect.SetParent(vrKeyboardRect, false);
			panelRect.anchorMin = Vector2.zero;
			panelRect.anchorMax = Vector2.one;
			panelRect.offsetMin = Vector2.zero;
			panelRect.offsetMax = Vector2.zero;
			panelObject.GetComponent<Image>().color = new Color(0.055f, 0.075f, 0.11f, 0.97f);

			vrKeyboardKeys.Clear();
			AddVrKeyboardRow("1234567890", -365f, 155f, 10, 78f, 68f);
			AddVrKeyboardRow("qwertyuiop", -365f, 65f, 10, 78f, 68f, true);
			AddVrKeyboardRow("asdfghjkl", -324f, -25f, 9, 78f, 68f, true);
			AddVrKeyboardRow("zxcvbnm", -243f, -115f, 7, 78f, 68f, true);
			CreateVrKeyboardKey("SHIFT", "shift", new Vector2(-398f, -210f), new Vector2(100f, 68f), false);
			CreateVrKeyboardKey("@", "@", new Vector2(-286f, -210f), new Vector2(68f, 68f), false);
			CreateVrKeyboardKey(".", ".", new Vector2(-208f, -210f), new Vector2(68f, 68f), false);
			CreateVrKeyboardKey("SPACE", "space", new Vector2(-45f, -210f), new Vector2(250f, 68f), false);
			CreateVrKeyboardKey("-", "-", new Vector2(116f, -210f), new Vector2(68f, 68f), false);
			CreateVrKeyboardKey("⌫", "backspace", new Vector2(228f, -210f), new Vector2(120f, 68f), false);
			CreateVrKeyboardKey("DONE", "done", new Vector2(390f, -210f), new Vector2(100f, 68f), false);

			WorldCanvasAnchor anchor = new WorldCanvasAnchor
			{
				Canvas = vrKeyboardCanvas,
				Position = Vector3.zero,
				Rotation = Quaternion.identity,
				Scale = vrKeyboardRect.localScale,
				HasVisibleGraphics = true
			};
			worldAnchoredMenuCanvases[vrKeyboardCanvas.GetInstanceID()] = anchor;
			PositionVrKeyboard();
		}

		private void AddVrKeyboardRow(string characters, float startX, float y, int count, float keyWidth, float keyHeight, bool letters = false)
		{
			for (int i = 0; i < count && i < characters.Length; i++)
			{
				string key = characters[i].ToString();
				CreateVrKeyboardKey(key, key, new Vector2(startX + i * keyWidth, y), new Vector2(keyWidth - 5f, keyHeight), letters);
			}
		}

		private void CreateVrKeyboardKey(string label, string value, Vector2 position, Vector2 size, bool isLetter)
		{
			GameObject keyObject = new GameObject("Keyboard Key " + value, typeof(RectTransform), typeof(Image), typeof(Button));
			keyObject.layer = vrKeyboardCanvas != null ? vrKeyboardCanvas.gameObject.layer : LayerMask.NameToLayer("UI");
			RectTransform keyRect = keyObject.GetComponent<RectTransform>();
			keyRect.SetParent(vrKeyboardRect, false);
			keyRect.anchorMin = keyRect.anchorMax = new Vector2(0.5f, 0.5f);
			keyRect.anchoredPosition = position;
			keyRect.sizeDelta = size;
			Image image = keyObject.GetComponent<Image>();
			image.color = value == "done" ? new Color(0.18f, 0.48f, 0.25f, 1f) : new Color(0.18f, 0.22f, 0.29f, 1f);
			Button button = keyObject.GetComponent<Button>();
			button.targetGraphic = image;
			button.navigation = new Navigation { mode = Navigation.Mode.None };
			button.onClick.AddListener(() => OnVrKeyboardKeyPressed(value));

			GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
			labelObject.layer = keyObject.layer;
			RectTransform labelRect = labelObject.GetComponent<RectTransform>();
			labelRect.SetParent(keyRect, false);
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(2f, 1f);
			labelRect.offsetMax = new Vector2(-2f, -1f);
			Text text = labelObject.GetComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = value.Length > 4 ? 24 : 31;
			text.fontStyle = FontStyle.Bold;
			text.alignment = TextAnchor.MiddleCenter;
			text.color = Color.white;
			text.raycastTarget = false;
			text.text = label;
			vrKeyboardKeys.Add(new KeyboardKey { Button = button, Value = value, IsLetter = isLetter });
		}

		private void OnVrKeyboardKeyPressed(string value)
		{
			if (vrKeyboardTarget == null)
			{
				return;
			}

			if (value == "shift")
			{
				vrKeyboardShift = !vrKeyboardShift;
				RefreshVrKeyboardLabels();
				return;
			}
			if (value == "done")
			{
				InputField completedField = vrKeyboardTarget;
				InputFieldEx extendedField = completedField as InputFieldEx;
				if (extendedField != null)
				{
					// Account forms listen to this event to validate the field and move
					// focus to the next one. Deactivating the InputField alone skips it.
					extendedField.TriggerOnKeyboardDone();
				}
				else
				{
					completedField.onSubmit.Invoke(completedField.text);
				}

				EventSystem eventSystem = EventSystem.current;
				GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
				InputField nextField = selected != null ? selected.GetComponentInParent<InputField>() : null;
				if (nextField != null && nextField != completedField && nextField.isActiveAndEnabled)
				{
					// InputFieldForm selects the next field from its keyboard-done event.
					// Keep the VR keyboard open and re-anchor it there.
					vrKeyboardTarget = nextField;
					vrKeyboardShift = false;
					PositionVrKeyboard();
					return;
				}

				completedField.DeactivateInputField();
				if (eventSystem != null && (selected == completedField.gameObject ||
					(selected != null && vrKeyboardCanvas != null && selected.transform.IsChildOf(vrKeyboardCanvas.transform))))
				{
					eventSystem.SetSelectedGameObject(null);
				}
				vrKeyboardTarget = null;
				DestroyVrKeyboard();
				return;
			}

			string text = vrKeyboardTarget.text ?? string.Empty;
			if (value == "backspace")
			{
				if (text.Length > 0) vrKeyboardTarget.text = text.Substring(0, text.Length - 1);
			}
			else
			{
				string character = value == "space" ? " " : (vrKeyboardShift && value.Length == 1 && char.IsLetter(value[0]) ? value.ToUpperInvariant() : value);
				int characterLimit = vrKeyboardTarget.characterLimit;
				if (characterLimit <= 0 || text.Length < characterLimit)
				{
					vrKeyboardTarget.text = text + character;
				}
			}
			vrKeyboardTarget.caretPosition = vrKeyboardTarget.text.Length;
			vrKeyboardTarget.ActivateInputField();
		}

		private void RefreshVrKeyboardLabels()
		{
			for (int i = 0; i < vrKeyboardKeys.Count; i++)
			{
				KeyboardKey key = vrKeyboardKeys[i];
				Text label = key.Button != null ? key.Button.GetComponentInChildren<Text>() : null;
				if (label != null && key.IsLetter)
				{
					label.text = vrKeyboardShift ? key.Value.ToUpperInvariant() : key.Value;
				}
			}
		}

		private void PositionVrKeyboard()
		{
			if (vrKeyboardTarget == null || vrKeyboardCanvas == null)
			{
				return;
			}

			RectTransform fieldRect = vrKeyboardTarget.transform as RectTransform;
			if (fieldRect == null)
			{
				return;
			}
			Vector3 up = fieldRect.up;
			float offset = fieldRect.rect.height * fieldRect.lossyScale.y * 0.5f + 0.22f;
			Vector3 position = fieldRect.position - up * offset;
			vrKeyboardRect.SetPositionAndRotation(position, fieldRect.rotation);
			vrKeyboardRect.localScale = Vector3.one * 0.0008f;
			int id = vrKeyboardCanvas.GetInstanceID();
			worldAnchoredMenuCanvases[id] = new WorldCanvasAnchor
			{
				Canvas = vrKeyboardCanvas,
				Position = position,
				Rotation = fieldRect.rotation,
				Scale = vrKeyboardRect.localScale,
				HasVisibleGraphics = true
			};
		}

		private void DestroyVrKeyboard()
		{
			if (vrKeyboardCanvas != null)
			{
				worldAnchoredMenuCanvases.Remove(vrKeyboardCanvas.GetInstanceID());
				Destroy(vrKeyboardCanvas.gameObject);
			}
			vrKeyboardCanvas = null;
			vrKeyboardRect = null;
			vrKeyboardKeys.Clear();
		}

		private void LogWaitingForPlayer()
		{
			if (Time.unscaledTime < nextWaitingLogTime)
			{
				return;
			}

			InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			ZoneLocalPlayerManager localPlayerManager = SceneRefs.ZoneLocalPlayerManager;
			Camera mainCamera = Camera.main;
			Debug.LogWarning(
				$"CPI PCVR waiting for gameplay camera: scene={SceneManager.GetActiveScene().name}, headValid={head.isValid}, " +
				$"mainCamera={(mainCamera != null ? mainCamera.name : "none")}, " +
				$"localPlayer={(localPlayerManager != null && localPlayerManager.LocalPlayerGameObject != null)}");
			nextWaitingLogTime = Time.unscaledTime + 5f;
		}

		private bool TryAttachToLocalPlayer()
		{
			if (!InputDevices.GetDeviceAtXRNode(XRNode.Head).isValid)
			{
				return false;
			}

			ZoneLocalPlayerManager localPlayerManager = SceneRefs.ZoneLocalPlayerManager;
			if (SceneManager.GetActiveScene().name == "ClothingDesigner" || localPlayerManager == null ||
				localPlayerManager.LocalPlayerGameObject == null || !localPlayerManager.LocalPlayerGameObject.activeInHierarchy)
			{
				cameraAttached = false;
				player = null;
				return false;
			}

			Camera mainCamera = Camera.main;
			if (mainCamera == null)
			{
				return false;
			}

			GameObject currentLocalPlayer = localPlayerManager.LocalPlayerGameObject;
			if (cameraAttached && player == currentLocalPlayer && playerCamera == mainCamera)
			{
				return true;
			}

			player = currentLocalPlayer;
			hasTrackingHeadingOrigin = false;
			playerCamera = mainCamera;
			playerCamera.transform.SetParent(player.transform, false);
			playerCamera.stereoTargetEye = StereoTargetEyeMask.Both;
			playerCamera.enabled = true;
			EnsureFlipperNearClip(playerCamera);

			BaseCamera cinematicCamera = playerCamera.GetComponent<BaseCamera>();
			if (cinematicCamera != null)
			{
				cinematicCamera.enabled = false;
			}

			CameraController cameraController = playerCamera.GetComponent<CameraController>();
			if (cameraController != null)
			{
				cameraController.enabled = false;
			}

			CreateFlippers();
			SetFlipperColor(GetCurrentPenguinColor());
			CreateFlipperAimRays(GetCurrentPenguinColor());
			RequestFloorTracking();
			cameraAttached = true;
			Debug.Log($"CPI PCVR: attached active camera '{playerCamera.name}' to local penguin; head pose will be applied directly before rendering. " +
				$"Parent={playerCamera.transform.parent.name}, localPosition={playerCamera.transform.localPosition}, " +
				$"cameraCount={Camera.allCameras.Length}, localPlayer={player.name}.");
			return true;
		}

		private void EnsureFlipperNearClip(Camera targetCamera)
		{
			if (targetCamera == null || !XRSettings.enabled)
			{
				return;
			}

			// Keep the tracked meshes visible when a hand is close to the headset.
			// This runs in Home too, before the local penguin and its camera are attached.
			if (targetCamera.nearClipPlane > 0.005f)
			{
				targetCamera.nearClipPlane = 0.005f;
			}
		}

        public static void SetClothingDesignerView(Camera camera, Vector3 position, Quaternion rotation)
        {
            if (camera == null) return;
            camera.transform.SetPositionAndRotation(position, rotation);
            if (instance != null) instance.SetMenuTrackingView(camera, position, rotation);
        }

        private void SetMenuTrackingView(Camera camera, Vector3 position, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            // Move the tracking origin when changing mannequins, while retaining
            // the user's head movement relative to that origin.
            if (menuTrackingCamera != camera)
            {
                headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
                menuHeadPositionOrigin = headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition)
                    ? headPosition : Vector3.zero;
            }
            playerCamera = camera;
            menuTrackingCamera = camera;
            menuTrackingOriginPosition = position;
            menuTrackingOriginRotation = rotation;
            closetPreview = null;
        }

		private void ApplyTrackedHeadPose()
		{
			if (!XRSettings.enabled || playerCamera == null)
			{
				return;
			}

			if (!headDevice.isValid)
			{
				headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			}
			Vector3 headPosition;
			Quaternion headRotation;
			if (!headDevice.isValid ||
				!headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out headPosition) ||
				!headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out headRotation))
			{
				return;
			}
			if (player == null)
			{
				// Home has no penguin transform yet. Treat the scene camera's initial
				// pose as the tracking origin and apply the HMD pose directly to it.
				if (menuTrackingCamera != playerCamera)
				{
					menuTrackingCamera = playerCamera;
					menuTrackingOriginPosition = playerCamera.transform.position;
					menuTrackingOriginRotation = playerCamera.transform.rotation;
					menuHeadPositionOrigin = headPosition;
				}
				playerCamera.transform.SetPositionAndRotation(
					menuTrackingOriginPosition + menuTrackingOriginRotation * ScaleTrackingPosition(
						SceneManager.GetActiveScene().name == "Home" ? headPosition : headPosition - menuHeadPositionOrigin),
					menuTrackingOriginRotation * headRotation);
				return;
			}

			// The game's camera director updates a third-person camera transform.
			// Keep the actual stereo camera at the tracked headset pose, relative to the penguin.
			if (playerCamera.transform.parent != player.transform)
			{
				playerCamera.transform.SetParent(player.transform, false);
			}
			if (!hasTrackingHeadingOrigin)
			{
				Vector3 playerForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
				if (playerForward.sqrMagnitude < 0.0001f)
				{
					playerForward = Vector3.forward;
				}
				Vector3 initialHeadForward = Vector3.ProjectOnPlane(headRotation * Vector3.forward, Vector3.up).normalized;
				if (initialHeadForward.sqrMagnitude < 0.0001f)
				{
					initialHeadForward = Vector3.forward;
				}
				trackingHeadRotationOrigin = Quaternion.LookRotation(initialHeadForward, Vector3.up);
				movementHeadingOriginRotation = Quaternion.LookRotation(playerForward, Vector3.up);
				trackingPositionOrigin = new Vector3(headPosition.x, 0f, headPosition.z);
				hasTrackingHeadingOrigin = true;
			}
			Quaternion trackingToWorldRotation = movementHeadingOriginRotation * Quaternion.Inverse(trackingHeadRotationOrigin);
			Quaternion worldHeadRotation = trackingToWorldRotation * headRotation;
			Vector3 trackedHeadPosition = ScaleTrackingPosition(headPosition - trackingPositionOrigin);
			playerCamera.transform.position = player.transform.position + trackingToWorldRotation * trackedHeadPosition;
			// Keep the tracked view independent from the penguin's auto-facing rotation.
			// Otherwise forward steering rotates the player under the headset pose, and
			// the next frame sees a new forward direction and keeps turning in a circle.
			playerCamera.transform.localRotation = Quaternion.Inverse(player.transform.rotation) * worldHeadRotation;
		}

		private void CreateFlippers()
		{
			if (leftFlipper != null && rightFlipper != null)
			{
				return;
			}

			Color penguinColor = GetCurrentPenguinColor();

			Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
			flipperMaterial = new Material(shader);
			flipperDetailMaterial = new Material(shader);
			SetFlipperColor(penguinColor);
			leftFlipper = CreateFlipper("Left VR Flipper");
			rightFlipper = CreateFlipper("Right VR Flipper");
			CreateFlipperAimRays(penguinColor);
			Debug.Log($"CPI PCVR: created custom tapered flippers using avatar color {penguinColor}.");
		}

		private void SetFlipperColor(Color penguinColor)
		{
			if (flipperMaterial == null || flipperDetailMaterial == null ||
				(hasFlipperColor && currentFlipperColor == penguinColor))
			{
				return;
			}

			if (flipperMaterial.HasProperty("_BaseColor")) flipperMaterial.SetColor("_BaseColor", penguinColor);
			if (flipperMaterial.HasProperty("_Color")) flipperMaterial.SetColor("_Color", penguinColor);
			Color detailColor = Color.Lerp(penguinColor, Color.white, 0.18f);
			if (flipperDetailMaterial.HasProperty("_BaseColor")) flipperDetailMaterial.SetColor("_BaseColor", detailColor);
			if (flipperDetailMaterial.HasProperty("_Color")) flipperDetailMaterial.SetColor("_Color", detailColor);
			currentFlipperColor = penguinColor;
			hasFlipperColor = true;

			if (leftAimRay != null)
			{
				leftAimRay.startColor = penguinColor;
				leftAimRay.endColor = new Color(penguinColor.r, penguinColor.g, penguinColor.b, 0.15f);
			}
			if (rightAimRay != null)
			{
				rightAimRay.startColor = penguinColor;
				rightAimRay.endColor = new Color(penguinColor.r, penguinColor.g, penguinColor.b, 0.15f);
			}
		}

		private void CreateFlipperAimRays(Color penguinColor)
		{
			if (leftAimRay == null) leftAimRay = CreateAimRay("Left VR Menu Pointer", penguinColor);
			if (rightAimRay == null) rightAimRay = CreateAimRay("Right VR Menu Pointer", penguinColor);
		}

		private Color GetCurrentPenguinColor()
		{
			Color fallback = new Color32(25, 210, 214, 255);
			if (player == null)
			{
				return fallback;
			}
			AvatarOutfitCombineManager avatarManager = player.GetComponent<AvatarOutfitCombineManager>();
			if (avatarManager == null)
			{
				return fallback;
			}

			FieldInfo detailsField = avatarManager.GetType().GetField("avatarDetailsData", BindingFlags.Instance | BindingFlags.NonPublic);
			object details = detailsField != null ? detailsField.GetValue(avatarManager) : null;
			PropertyInfo bodyColorProperty = details != null
				? details.GetType().GetProperty("BodyColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				: null;
			if (bodyColorProperty != null && bodyColorProperty.PropertyType == typeof(Color))
			{
				return (Color)bodyColorProperty.GetValue(details, null);
			}

			// The private AvatarDetailsData may not be populated until outfit loading completes.
			// Prefer the visible body material in that short window if it exposes a tint.
			SkinnedMeshRenderer[] renderers = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				foreach (Material material in renderers[i].sharedMaterials)
				{
					if (material != null && material.HasProperty("_BodyColor")) return material.GetColor("_BodyColor");
				}
			}
			return fallback;
		}

		private Transform CreateFlipper(string objectName)
		{
			GameObject flipper = new GameObject(objectName);
			flipper.transform.SetParent(transform, false);
			MeshFilter filter = flipper.AddComponent<MeshFilter>();
			MeshRenderer renderer = flipper.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = flipperMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			filter.sharedMesh = CreateFlipperMesh();
			filter.sharedMesh.MarkDynamic();
			flipperRestVertices[flipper.transform] = filter.sharedMesh.vertices;
			flipperAnimatedVertices[flipper.transform] = new Vector3[filter.sharedMesh.vertexCount];
			return flipper.transform;
		}

		private LineRenderer CreateAimRay(string objectName, Color color)
		{
			GameObject rayObject = new GameObject(objectName);
			rayObject.layer = LayerMask.NameToLayer("UI");
            rayObject.transform.SetParent(transform, false);
			LineRenderer line = rayObject.AddComponent<LineRenderer>();
			line.positionCount = 2;
			line.startWidth = 0.006f;
			line.endWidth = 0.002f;
			line.useWorldSpace = true;
			line.sharedMaterial = flipperDetailMaterial;
			line.startColor = color;
			line.endColor = new Color(color.r, color.g, color.b, 0.15f);
			line.enabled = false;
			return line;
		}

		private Mesh CreateFlipperMesh()
		{
			const int lengthRings = 20;
			const int radialSegments = 16;
			float halfWidth = flipperLength * 0.22f;
			float halfThickness = flipperLength * 0.065f;
			List<Vector3> vertices = new List<Vector3>(lengthRings * radialSegments + 2);
			List<int> triangles = new List<int>(lengthRings * radialSegments * 6);

			// A penguin flipper has a short rounded shoulder root, a broad flat
			// blade, and a long smooth taper to the controller-side point.
			for (int ring = 0; ring < lengthRings; ring++)
			{
				float along = Mathf.Lerp(0.025f, 0.965f, ring / (float)(lengthRings - 1));
				float widthProfile = along < 0.38f
					? Mathf.Lerp(0.22f, 1f, Mathf.SmoothStep(0f, 1f, along / 0.38f))
					: Mathf.Lerp(1f, 0.025f, Mathf.SmoothStep(0f, 1f, (along - 0.38f) / 0.62f));
				float ringWidth = halfWidth * widthProfile;
				float ringThickness = halfThickness * Mathf.Sqrt(widthProfile);
				for (int segment = 0; segment < radialSegments; segment++)
				{
					float angle = (2f * Mathf.PI * segment) / radialSegments;
					vertices.Add(new Vector3(
						Mathf.Cos(angle) * ringWidth,
						along * flipperLength,
						Mathf.Sin(angle) * ringThickness));
				}
			}

			int rootCenter = vertices.Count;
			vertices.Add(Vector3.zero);
			int tip = vertices.Count;
			vertices.Add(new Vector3(0f, flipperLength, 0f));

			for (int ring = 0; ring < lengthRings - 1; ring++)
			{
				int lowerStart = ring * radialSegments;
				int upperStart = (ring + 1) * radialSegments;
				for (int segment = 0; segment < radialSegments; segment++)
				{
					int next = (segment + 1) % radialSegments;
					int lower = lowerStart + segment;
					int lowerNext = lowerStart + next;
					int upper = upperStart + segment;
					int upperNext = upperStart + next;
					triangles.AddRange(new[] { lower, upper, upperNext, lower, upperNext, lowerNext });
				}
			}

			int lastRingStart = (lengthRings - 1) * radialSegments;
			for (int segment = 0; segment < radialSegments; segment++)
			{
				int next = (segment + 1) % radialSegments;
				triangles.AddRange(new[] { lastRingStart + segment, tip, lastRingStart + next });
				triangles.AddRange(new[] { rootCenter, segment, next });
			}

			Mesh mesh = new Mesh { name = "CPI Penguin Flipper Mesh" };
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private void UpdateFlippers()
		{
			if (player != null && Time.unscaledTime >= nextPenguinColorCheckTime)
			{
				SetFlipperColor(GetCurrentPenguinColor());
				nextPenguinColorCheckTime = Time.unscaledTime + 1f;
			}
			UpdateFlipper(leftFlipper, ref leftHand, XRNode.LeftHand, ref leftTriggerWasPressed, leftAimRay);
			UpdateFlipper(rightFlipper, ref rightHand, XRNode.RightHand, ref rightTriggerWasPressed, rightAimRay);
			ProcessControllerButtons();
		}

		private void UpdateMenuPointers()
		{
			if (playerCamera == null)
			{
				playerCamera = Camera.main;
			}
			if (playerCamera == null)
			{
				return;
			}

			if (leftFlipper == null || rightFlipper == null)
			{
				CreateFlippers();
			}
			if (SceneManager.GetActiveScene().name == "Home")
			{
				if (menuPenguin == null)
				{
					GameObject model = Resources.Load<GameObject>("PCVR/MenuPenguin");
					if (model != null)
					{
						menuPenguin = Instantiate(model);
						menuPenguin.name = "PCVR Menu Penguin";
						menuPenguin.transform.position = menuTrackingOriginPosition;
						menuAvatarRenderer = new PCVRAvatarRenderer(menuPenguin);
					}
				}
				menuAvatarRenderer?.BeginPose(playerCamera);
			}
			UpdateFlipper(leftFlipper, ref leftHand, XRNode.LeftHand, ref leftTriggerWasPressed, leftAimRay);
			UpdateFlipper(rightFlipper, ref rightHand, XRNode.RightHand, ref rightTriggerWasPressed, rightAimRay);
		}

		private void UpdateFlipper(Transform flipper, ref InputDevice device, XRNode node, ref bool triggerWasPressed, LineRenderer aimRay)
		{
			if (flipper == null) return;
			if (!device.isValid) device = InputDevices.GetDeviceAtXRNode(node);
			Vector3 position = Vector3.zero;
			Quaternion rotation = Quaternion.identity;
			float triggerValue = 0f;
			bool hasPose = device.isValid && device.TryGetFeatureValue(CommonUsages.devicePosition, out position) &&
				device.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
			if (hasPose && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked)) hasPose = tracked;
			flipper.gameObject.SetActive(hasPose);
			if (!hasPose)
			{
				(node == XRNode.LeftHand ? leftPointer : rightPointer).Cancel();
                if (pointerPuzzle != null) pointerPuzzle.CancelVrPointer(node == XRNode.LeftHand ? -101 : -102);
				triggerWasPressed = false;
				if (aimRay != null) aimRay.enabled = false;
				return;
			}
			device.TryGetFeatureValue(CommonUsages.trigger, out triggerValue);
			bool triggerButtonPressed = false;
			device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerButtonPressed);
			InputSystemXRController inputSystemController = GetInputSystemHandController(node);
			if (inputSystemController != null)
			{
				AxisControl inputTrigger = inputSystemController.TryGetChildControl<AxisControl>("trigger");
				ButtonControl inputTriggerButton = inputSystemController.TryGetChildControl<ButtonControl>("triggerPressed");
				if (inputTrigger != null)
				{
					triggerValue = Mathf.Max(triggerValue, inputTrigger.ReadValue());
				}
				triggerButtonPressed |= inputTriggerButton != null && inputTriggerButton.isPressed;
			}
			if (triggerButtonPressed)
			{
				triggerValue = Mathf.Max(triggerValue, 1f);
			}
			Vector3 flipperPosition = TrackingToWorldPosition(position);
			Quaternion controllerWorldRotation = TrackingToWorldRotation(rotation);
			float side = node == XRNode.LeftHand ? -1f : 1f;
			Vector3 shoulderPosition;
			Vector3 inwardDirection;
			if (player == null)
			{
				flipperPosition = KeepMenuFlipperInFrontOfCamera(flipperPosition);
				Camera viewCamera = playerCamera != null ? playerCamera : Camera.main;
				if (viewCamera == null)
				{
					flipper.gameObject.SetActive(false);
					return;
				}
				Transform view = viewCamera.transform;
				Vector3 virtualTorso = view.position - Vector3.up * 0.28f - view.forward * 0.08f;
				shoulderPosition = virtualTorso + view.right * (side * 0.16f);
				inwardDirection = -view.right * side;
			}
			else
			{
				Vector3 headsetLocalPosition = playerCamera != null
					? playerCamera.transform.localPosition
					: new Vector3(0f, 1.6f, 0f);
				Vector3 shoulderLocalPosition = new Vector3(
					headsetLocalPosition.x + side * 0.17f,
					Mathf.Clamp(headsetLocalPosition.y - 0.6f, 0.7f, 1.15f),
					headsetLocalPosition.z + 0.015f);
				shoulderPosition = player.transform.TransformPoint(shoulderLocalPosition);
				inwardDirection = -player.transform.right * side;
			}
			PCVRAvatarRenderer activeAvatar = player != null ? avatarRenderer : menuAvatarRenderer;
			if (activeAvatar != null)
			{
				activeAvatar.PoseHand(node == XRNode.LeftHand, flipperPosition, controllerWorldRotation, triggerValue);
				if (player != null && node == XRNode.LeftHand)
					activeAvatar.RotateAttachment(ClubPenguin.Adventure.FishingController.HeldPrize, controllerWorldRotation);
				// Hidden source meshes (cannons, transitions) remain hidden. They are
				// not a request to replace the penguin with procedural flippers.
				flipper.gameObject.SetActive(false);
			}
			else if (player == null && SceneManager.GetActiveScene().name != "Home")
			{
				UpdateFlipperFromShoulder(flipper, shoulderPosition, flipperPosition,
					inwardDirection, controllerWorldRotation, triggerValue);
			}
			else flipper.gameObject.SetActive(false);

			// Support controllers that expose only the digital trigger button, and
			// add hysteresis so noisy analog values do not drop/repeat a press.
			bool triggerPressed = triggerButtonPressed || triggerValue > (triggerWasPressed ? 0.45f : 0.55f);
			ProcessUiPointer(node, position, rotation, triggerPressed, triggerWasPressed, aimRay);
			triggerWasPressed = triggerPressed;
		}

		private void UpdateFlipperFromShoulder(Transform flipper, Vector3 shoulderPosition,
			Vector3 wristPosition, Vector3 inwardDirection, Quaternion controllerRotation, float triggerValue)
		{
			// The menu/loading fallback uses the same fixed-length elbow solve as the avatar.
			Vector3 pole = -inwardDirection * 0.65f + Vector3.down * 0.85f;
			PCVRArmIK.Solve(shoulderPosition, wristPosition, pole, flipperLength * 0.5f,
				flipperLength * 0.5f, out Vector3 elbow, out Vector3 wrist);
			Vector3 upper = (elbow - shoulderPosition).normalized;
			Vector3 lower = (wrist - elbow).normalized;
			Vector3 width = Vector3.ProjectOnPlane(controllerRotation * Vector3.right, upper);
			if (width.sqrMagnitude < 0.001f) width = Vector3.ProjectOnPlane(Vector3.forward, upper);
			if (width.sqrMagnitude < 0.001f) width = Vector3.ProjectOnPlane(Vector3.right, upper);
			Quaternion upperFrame = Quaternion.LookRotation(Vector3.Cross(width.normalized, upper), upper);
			Quaternion lowerFrame = Quaternion.FromToRotation(upper, lower) * upperFrame;
			Vector3[] rest = flipperRestVertices[flipper];
			Vector3[] posed = flipperAnimatedVertices[flipper];
			float hinge = flipperLength * 0.5f;
			Quaternion fold = Quaternion.AngleAxis(flipperGripBendDegrees * Mathf.Clamp01(triggerValue), Vector3.right);
			for (int i = 0; i < rest.Length; i++)
			{
				Vector3 p = rest[i];
				Vector3 upperPoint = shoulderPosition + upperFrame * p;
				Vector3 lowerPoint = elbow + lowerFrame * (p - Vector3.up * hinge);
				// Blend a small band around the joint to avoid a sharp crease or detached pieces.
				float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hinge - 0.025f, hinge + 0.025f, p.y));
				if (p.y > flipperLength * 0.82f)
				{
					Vector3 tipHinge = Vector3.up * (flipperLength * 0.82f - hinge);
					lowerPoint = elbow + lowerFrame * (tipHinge + fold * (p - Vector3.up * hinge - tipHinge));
				}
				posed[i] = Vector3.Lerp(upperPoint, lowerPoint, blend) - shoulderPosition;
			}
			flipper.SetPositionAndRotation(shoulderPosition, Quaternion.identity);
			flipper.localScale = Vector3.one;
			flipper.gameObject.SetActive(true);
			Mesh mesh = flipper.GetComponent<MeshFilter>().sharedMesh;
			mesh.vertices = posed;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
		}
		private Vector3 KeepMenuFlipperInFrontOfCamera(Vector3 flipperPosition)
		{
			if (playerCamera == null)
			{
				return flipperPosition;
			}

			Vector3 cameraLocalPosition = playerCamera.transform.InverseTransformPoint(flipperPosition);
			const float closeToHeadDistance = 0.3f;
			const float minimumVisibleDepth = 0.12f;
			if (cameraLocalPosition.sqrMagnitude > closeToHeadDistance * closeToHeadDistance ||
				cameraLocalPosition.z >= minimumVisibleDepth)
			{
				return flipperPosition;
			}

			// In Home, keep a hand-held flipper just in front of the headset when
			// the controller enters the camera's near clip region.
			cameraLocalPosition.z = minimumVisibleDepth;
			return playerCamera.transform.TransformPoint(cameraLocalPosition);
		}

		private void ProcessControllerButtons()
		{
			ProcessControllerButtons(ref leftHand, ref leftPrimaryWasPressed, ref leftSecondaryWasPressed,
				LocomotionController.LocomotionAction.Action1, LocomotionController.LocomotionAction.Action2);
			ProcessControllerButtons(ref rightHand, ref rightPrimaryWasPressed, ref rightSecondaryWasPressed,
				LocomotionController.LocomotionAction.Jump, LocomotionController.LocomotionAction.Action1);
		}

		private bool ReadVrUiGrip(InputDevice device, XRNode node)
		{
			bool pressed = false;
			float gripValue = 0f;
			device.TryGetFeatureValue(CommonUsages.gripButton, out pressed);
			device.TryGetFeatureValue(CommonUsages.grip, out gripValue);
			InputSystemXRController inputController = GetInputSystemHandController(node);
			if (inputController != null)
			{
				ButtonControl gripButton = inputController.TryGetChildControl<ButtonControl>("gripPressed");
				AxisControl gripAxis = inputController.TryGetChildControl<AxisControl>("grip");
				pressed |= gripButton != null && gripButton.isPressed;
				if (gripAxis != null)
				{
					gripValue = Mathf.Max(gripValue, gripAxis.ReadValue());
				}
			}
			pressed |= gripValue > ((node == XRNode.LeftHand ? leftUiToggleWasPressed : rightUiToggleWasPressed) ? 0.45f : 0.75f);

			return pressed;
		}

		private void UpdateTransientUiVisibility(bool active)
		{
			// Automatic visibility lasts only for the important panel's lifetime.
            if (active && !transientUiWasActive) automaticUiSuppressed = false;
            transientUiActiveCached = active;
			transientUiWasActive = active;
		}

		private void UpdateVrUiToggles()
		{
			if (!leftHand.isValid) leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
			if (!rightHand.isValid) rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
			bool rightGrip = ReadVrUiGrip(rightHand, XRNode.RightHand);
            if (rightGrip != rightUiToggleWasPressed && player != null && !IsShowroom() && Service.IsSet<EventDispatcher>())
            {
                if (rightGrip) snowballGripStart = Time.unscaledTime;
                Service.Get<EventDispatcher>().DispatchEvent(new InputEvents.ChargeActionEvent(
                    InputEvents.ChargeActions.Snowball, rightGrip, Time.unscaledTime - snowballGripStart));
            }
            rightUiToggleWasPressed = rightGrip;
            ProcessVrUiGrips(ReadVrUiGrip(leftHand, XRNode.LeftHand), false);
		}

		private void ProcessVrUiGrips(bool leftPressed, bool rightPressed)
		{
			bool toggle = leftPressed && !leftUiToggleWasPressed;
			leftUiToggleWasPressed = leftPressed;
			// Right grip state belongs to snowball charging; UI uses the left grip.
			if (!toggle) return;
            if (IsMainMenuScene(SceneManager.GetActiveScene().name) || tutorialUiActiveCached) return;
			bool wasVisible = UiVisible;
            vrUiManuallyVisible = !wasVisible;
            automaticUiSuppressed = wasVisible;
			uiPointerCacheFrame = -1;
			leftPointer.Cancel();
			rightPointer.Cancel();
			if (!vrUiManuallyVisible) DestroyVrKeyboard();
			// Catch navigation instantiated since the last cached scan, including
            // the first room's asynchronous HUD creation.
            nextMenuVisibilityScan = 0f;
            nextCanvasScanTime = 0f;
            // Apply before either hand is processed.
			foreach (WorldCanvasAnchor anchor in worldAnchoredMenuCanvases.Values)
				SetVrCanvasVisibility(anchor, true);
			UpdateMenuVisibility(GetUiCanvases());
			SetAimRaysEnabled(false);
			Debug.Log($"CPI PCVR: VR menu and pointers toggled {(vrUiManuallyVisible ? "on" : "off")} with grip.");
		}

		private void ProcessControllerButtons(ref InputDevice device, ref bool primaryWasPressed, ref bool secondaryWasPressed,
			LocomotionController.LocomotionAction primaryAction, LocomotionController.LocomotionAction secondaryAction)
		{
			if (!device.isValid) return;
			bool primary = false;
			bool secondary = false;
			device.TryGetFeatureValue(CommonUsages.primaryButton, out primary);
			device.TryGetFeatureValue(CommonUsages.secondaryButton, out secondary);
			if (primary && !primaryWasPressed)
            {
                if (primaryAction == LocomotionController.LocomotionAction.Action1) OpenVrPhone();
                else DoLocomotionAction(primaryAction);
            }
			if (secondary && !secondaryWasPressed)
            {
                if (primaryAction == LocomotionController.LocomotionAction.Action1)
                {
                    if (Service.IsSet<PropService>()) Service.Get<PropService>().LocalPlayerStoreProp();
                }
                else DoLocomotionAction(secondaryAction);
            }
			primaryWasPressed = primary;
			secondaryWasPressed = secondary;
		}

        private void OpenVrPhone()
        {
            if (FindFirstObjectByType<ClubPenguin.CellPhone.CellPhoneController>() != null) return;
            foreach (Canvas canvas in GetUiCanvases())
            {
                if (canvas == null || !canvas.isRootCanvas) continue;
                foreach (Transform child in canvas.GetComponentsInChildren<Transform>(false))
                {
                    if (!IsPhoneLauncher(child)) continue;
                    var listener = child.GetComponent<ClubPenguin.Input.ButtonClickListener>();
                    if (listener != null) listener.InvokeClick(ClubPenguin.Input.ButtonClickListener.ClickType.InputMap);
                    else { var button = child.GetComponent<Button>(); if (button != null && button.interactable) button.onClick.Invoke(); }
                    automaticUiSuppressed = false;
                    nextTransientUiScanTime = 0f;
                    return;
                }
            }
        }

        private void CancelPuzzlePointers()
        {
            if (pointerPuzzle == null) return;
            pointerPuzzle.CancelVrPointer(-101);
            pointerPuzzle.CancelVrPointer(-102);
        }

		private void ProcessUiPointer(XRNode node, Vector3 position, Quaternion rotation,
			bool held, bool wasHeld, LineRenderer line)
		{
			PCVRPointer pointer = node == XRNode.LeftHand ? leftPointer : rightPointer;
			bool active = IsVrUiPointerActive();
			Vector3 origin = GetMenuAimOrigin(node, null, position);
			Vector3 direction = GetMenuAimDirection(node, rotation);
			bool hit = TryGetWorldMenuGraphicHit(origin, direction, out Vector3 point,
				out GameObject target, out EventSystem system, out RaycastResult result);
			bool down = held && !wasHeld;
			bool up = !held && wasHeld;
			// "Press any key" tutorials also appear over modal stores. Let their
			// existing FSM advance on release even when UI owns the trigger.
			if (up) ClubPenguin.Adventure.InputAnyKeyUpAction.NotifyVrTriggerRelease();
            if ((pointerPuzzle == null || !pointerPuzzle.isActiveAndEnabled) && Time.unscaledTime >= nextPointerPuzzleScan)
            {
                pointerPuzzle = FindFirstObjectByType<ClubPenguin.MiniGames.Jigsaw.JigsawController>();
                nextPointerPuzzleScan = Time.unscaledTime + 0.1f;
            }
            var puzzle = pointerPuzzle;
            bool puzzleVisible = puzzle != null && puzzle.IsVrPointerVisible;
            if (puzzle != null && !puzzleVisible) puzzle.CancelVrPointer(node == XRNode.LeftHand ? -101 : -102);
            bool allowPressAnywhere = !active && !puzzleVisible && !pointer.HasPress;
            active |= hit || pointer.HasPress || puzzleVisible;
            bool interactiveHit = target != null && (ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null ||
                ExecuteEvents.GetEventHandler<IPointerDownHandler>(target) != null || ExecuteEvents.GetEventHandler<IDragHandler>(target) != null);
            // Mesh minigames predate EventSystem controls. Feed them the same
            // controller ray, while their close button still uses normal UI events.
            if (puzzleVisible && puzzle.ProcessVrPointer(node == XRNode.LeftHand ? -101 : -102,
                new Ray(origin, direction), down, held, up, interactiveHit || pointer.HasPress, out Vector3 puzzlePoint))
            {
                pointer.Cancel();
                point = puzzlePoint;
                hit = true;
                goto DrawPointer;
            }
            if (allowPressAnywhere && (down || up) && !HasModalPopup())
                ClubPenguin.Adventure.PressAnywhereAction.NotifyVrPointerPress(down, up);
            if (active && SceneManager.GetActiveScene().name == "ClothingDesigner")
            {
                var dragArea = FindFirstObjectByType<ClubPenguin.ClothingDesigner.DragAreaController>();
                Vector2 customizationPosition = playerCamera.WorldToScreenPoint(hit ? point : origin + direction * 1.6f);
                if (dragArea != null && dragArea.ProcessVrPointer(node == XRNode.LeftHand ? -101 : -102,
                    new Ray(origin, direction), target, customizationPosition, down, held, up))
                {
                    pointer.Cancel();
                    if (ClubPenguin.ClothingDesigner.DragAreaController.GetVrCustomizationUiTarget(target) == null &&
                        ClubPenguin.ClothingDesigner.DragAreaState.RaycastClothing(new Ray(origin, direction), out RaycastHit clothingHit))
                    {
                        point = clothingHit.point;
                        hit = true;
                    }
                    goto DrawPointer;
                }
            }
			if (!active)
			{
				pointer.Cancel();
				if (down && !HasModalPopup() && !TryClickActiveCinematicDialogue() && player != null)
					UseTriggerAction();
			}
			else
			{
                // Visible UI owns the trigger, including empty panel space.
                // Never turn a cursor press into dialogue or a world interaction.
				bool keyboardKey = target != null && vrKeyboardCanvas != null && target.transform.IsChildOf(vrKeyboardCanvas.transform);
				if (down && target != null) HideVrKeyboardWhenClickingOutside(target);
				Vector2 scroll = node == XRNode.RightHand ? ApplyRadialDeadzone(ReadAxis(ref rightHand, node), 0.2f) : Vector2.zero;
				Vector2 screen = hit ? result.screenPosition : (Vector2)playerCamera.WorldToScreenPoint(origin + direction * 1.6f);
				screen = pointer.ProjectDragPosition(new Ray(origin, direction), screen);
				pointer.Process(system, result, screen, down, up, new Vector2(0f, scroll.y * Time.unscaledDeltaTime * 10f), keyboardKey);
				if (up && target != null)
				{
					InputField field = target.GetComponentInParent<InputField>();
					if (field != null && field.IsInteractable() && field.isFocused) vrKeyboardTarget = field;
				}
			}
		DrawPointer:
			if (line != null)
			{
				line.enabled = active;
				line.SetPosition(0, origin);
				line.SetPosition(1, hit ? point : origin + direction * 5f);
				Color color = target != null && (ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null || ExecuteEvents.GetEventHandler<IDragHandler>(target) != null)
					? new Color(0.3f, 1f, 0.65f) : Color.white;
				line.startColor = line.endColor = color;
			}
		}

		private void UseTriggerAction()
		{
			// Interact deploys/unequips props. Action1 is their existing use action,
			// and is also the fishing game's hook/reel input.
			bool useItem = ClubPenguin.Adventure.FishingController.IsActiveFishing ||
				(Service.IsSet<PropService>() && Service.Get<PropService>().IsLocalUserUsingProp());
			DoLocomotionAction(useItem ? LocomotionController.LocomotionAction.Action1 :
				LocomotionController.LocomotionAction.Interact);
		}

		private bool TryClickActiveCinematicDialogue()
		{
			CinematicSpeechController[] speechControllers = FindObjectsByType<CinematicSpeechController>(FindObjectsSortMode.None);
			for (int i = 0; i < speechControllers.Length; i++)
			{
				if (speechControllers[i] != null && speechControllers[i].HandleVrPointerClick())
				{
					return true;
				}
			}
			return false;
		}

		private bool HasModalPopup()
		{
			return FindFirstObjectByType<MarketplaceScreenController>() != null ||
				FindFirstObjectByType<AbstractDisneyStoreController>() != null ||
                FindFirstObjectByType<ClubPenguin.Igloo.Catalog.IglooCatalogController>() != null;
		}

		private Canvas[] GetUiCanvases()
		{
			if (Time.unscaledTime >= nextCanvasScanTime)
			{
				cachedCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				nextCanvasScanTime = Time.unscaledTime + 0.1f;
				List<int> expired = new List<int>();
				foreach (var entry in worldAnchoredMenuCanvases)
					if (entry.Value.Canvas == null) expired.Add(entry.Key);
				foreach (int id in expired) worldAnchoredMenuCanvases.Remove(id);
			}
			return cachedCanvases;
		}

		private bool TryGetWorldMenuGraphicHit(Vector3 origin, Vector3 direction, out Vector3 hitPoint,
			out GameObject hitObject, out EventSystem eventSystem, out RaycastResult menuHit)
		{
			hitPoint = Vector3.zero;
			hitObject = null;
			menuHit = default;
			eventSystem = EventSystem.current != null ? EventSystem.current : FindFirstObjectByType<EventSystem>();
			if (playerCamera == null || eventSystem == null) return false;
			uiHits.Clear();
            targetedTutorials.Clear();
            foreach (Canvas uiCanvas in GetUiCanvases())
            {
                if (uiCanvas == null || !uiCanvas.isRootCanvas) continue;
                foreach (var tutorial in uiCanvas.GetComponentsInChildren<ClubPenguin.TutorialUI.TutorialOverlay>(false))
                    if (tutorial.isActiveAndEnabled && tutorial.InteractiveTarget != null) targetedTutorials.Add(tutorial);
            }
			Ray ray = new Ray(origin, direction.normalized);
			foreach (Canvas canvas in GetUiCanvases())
			{
				if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace ||
					IsCanvasSuppressedByVrVisibility(canvas.rootCanvas)) continue;
				// Screens and inventory items are created after their root was converted
				// to VR, and popup code can restore the original GUI camera. A non-null
				// camera is not necessarily the camera displaying this world-space UI.
				// Rebind every canvas before projecting hits or evaluating its masks.
				canvas.worldCamera = playerCamera;
				GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
				if (raycaster == null)
				{
					raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
					raycaster.ignoreReversedGraphics = false;
				}
				if (!raycaster.isActiveAndEnabled) continue;
				Camera eventCamera = playerCamera;
				// Test every graphic with the controller ray. Projecting the root plane
				// into a camera ray selects the wrong control when children have depth.
				// Only use this canvas's registry: nested graphics keep their own sorting.
				IList<Graphic> graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
				for (int i = 0; i < graphics.Count; i++)
				{
					Graphic graphic = graphics[i];
					if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget ||
						graphic.depth == -1 || graphic.canvasRenderer.cull || graphic.canvasRenderer.GetInheritedAlpha() <= 0.001f) continue;
                    if (graphic.GetComponentInParent<ClubPenguin.ClothingDesigner.DragContainer>(true) != null) continue;
					bool tutorialDecoration = false;
                    foreach (var tutorial in targetedTutorials)
                    {
                        Canvas tutorialCanvas = tutorial.GetComponentInParent<Canvas>();
                        if (tutorialCanvas != null && IsTutorialDecoration(graphic, tutorial.transform,
                            tutorialCanvas.transform, tutorial.InteractiveTarget.transform))
                        { tutorialDecoration = true; break; }
                    }
                    if (tutorialDecoration) continue;
                    RectTransform rect = graphic.rectTransform;
                    if (!HasUiRaycastArea(rect)) continue;
					Plane plane = new Plane(rect.forward, rect.position);
					if (!plane.Raycast(ray, out float distance) || distance < 0f || distance > 10f) continue;
					Vector3 point = ray.GetPoint(distance);
					Vector3 local = rect.InverseTransformPoint(point);
					Rect bounds = rect.rect;
					// Several nav icons stretch across their layout slot but preserve the
					// sprite aspect ratio. Raycast their visible icon, not its empty margins.
					Image icon = graphic as Image;
					if (icon != null && icon.preserveAspect && icon.type == Image.Type.Simple && icon.sprite != null)
					{
						Vector2 size = icon.sprite.rect.size;
						if (size.x > 0f && size.y > 0f && bounds.width > 0f && bounds.height > 0f)
						{
							float scale = Mathf.Min(bounds.width / size.x, bounds.height / size.y);
							Vector2 drawn = size * scale;
							bounds.position += Vector2.Scale(bounds.size - drawn, rect.pivot);
							bounds.size = drawn;
						}
					}
					Vector4 padding = graphic.raycastPadding;
					bounds.xMin += padding.x;
					bounds.yMin += padding.y;
					bounds.xMax -= padding.z;
					bounds.yMax -= padding.w;
					if (!bounds.Contains(new Vector2(local.x, local.y))) continue;
					Vector3 screen = eventCamera.WorldToScreenPoint(point);
					// Preserve authored CanvasGroup, mask and Image raycast filters.
					if (screen.z <= 0f || !graphic.Raycast(screen, eventCamera)) continue;
					uiHits.Add(new RaycastResult
					{
						gameObject = graphic.gameObject,
						module = raycaster,
						distance = distance,
						depth = graphic.depth,
						sortingLayer = canvas.sortingLayerID,
						sortingOrder = canvas.sortingOrder,
						worldPosition = point,
						worldNormal = -rect.forward,
						screenPosition = screen,
						index = uiHits.Count
					});
				}
			}
			if (uiHits.Count == 0) return false;
			uiHits.Sort((a, b) => {
				int layer = SortingLayer.GetLayerValueFromID(b.sortingLayer).CompareTo(SortingLayer.GetLayerValueFromID(a.sortingLayer));
				if (layer != 0) return layer;
				int order = b.sortingOrder.CompareTo(a.sortingOrder);
				if (order != 0) return order;
				if (a.module.rootRaycaster == b.module.rootRaycaster && a.depth != b.depth) return b.depth.CompareTo(a.depth);
				return a.distance.CompareTo(b.distance);
			});
			menuHit = uiHits[0];
			hitPoint = menuHit.worldPosition;
			hitObject = menuHit.gameObject;
			return true;
		}

        private static bool IsTutorialDecoration(Graphic graphic, Transform overlay, Transform tutorialRoot, Transform target)
        {
            if (graphic.transform == target || graphic.transform.IsChildOf(target)) return false;
            if (graphic.transform == overlay || graphic.transform.IsChildOf(overlay)) return true;
            // The tutorial's own canvas also owns full-screen sibling graphics. Its
            // overlay CanvasGroup cannot filter those. Do not expand this boundary to
            // rootCanvas: a nested tutorial and its target can share that root, which
            // would leave the tutorial blocker in front of the highlighted button.
            // Keep normal modal blockers and controls outside the tutorial intact.
            return !target.IsChildOf(tutorialRoot) && graphic.transform.IsChildOf(tutorialRoot);
        }

		private InputSystemXRController GetInputSystemHandController(XRNode node)
		{
			if (node == XRNode.LeftHand)
			{
				return InputSystemXRController.leftHand;
			}
			if (node == XRNode.RightHand)
			{
				return InputSystemXRController.rightHand;
			}
			return null;
		}

		private Vector3 GetMenuAimOrigin(XRNode node, Transform flipper, Vector3 trackingPosition)
		{
			InputSystemXRController controller = GetInputSystemHandController(node);
			Vector3Control pointerPosition = controller != null
				? controller.TryGetChildControl<Vector3Control>("pointerPosition")
				: null;
			return pointerPosition != null && pointerPosition.ReadValue().sqrMagnitude > 0.0001f
				? TrackingToWorldPosition(pointerPosition.ReadValue())
				: GetAimOrigin(flipper, trackingPosition);
		}

		private Vector3 GetMenuAimDirection(XRNode node, Quaternion gripRotation)
		{
			InputSystemXRController controller = GetInputSystemHandController(node);
			QuaternionControl aim = controller != null ? controller.TryGetChildControl<QuaternionControl>("pointerRotation") : null;
			Quaternion rotation = aim != null ? aim.ReadValue() : gripRotation;
			if (Quaternion.Dot(rotation, rotation) < 0.5f) rotation = gripRotation;
			return (TrackingToWorldRotation(rotation) * Vector3.forward).normalized;
		}

		private Vector3 GetAimOrigin(Transform flipper, Vector3 position)
		{
			return TrackingToWorldPosition(position);
		}

		private Vector3 ScaleTrackingPosition(Vector3 position)
		{
			position.y *= penguinHeightScale;
			return position;
		}

		private Vector3 TrackingToWorldPosition(Vector3 trackingPosition)
		{
			if (player != null)
			{
				return player.transform.position + TrackingToWorldRotation(Quaternion.identity) * ScaleTrackingPosition(trackingPosition - trackingPositionOrigin);
			}

			// Before a local avatar exists there is no player transform to represent
			// the tracking origin.
			// Reconstruct it from the tracked HMD pose so hand and head coordinates
			// share the same world-space origin.
			if (playerCamera == null)
			{
				return trackingPosition;
			}
			Vector3 headPosition = Vector3.zero;
			Quaternion headRotation = Quaternion.identity;
			if (!headDevice.isValid)
			{
				headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			}
			if (headDevice.isValid)
			{
				headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out headPosition);
				headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out headRotation);
			}
			headPosition.y *= penguinHeightScale;
			Quaternion originRotation = playerCamera.transform.rotation * Quaternion.Inverse(headRotation);
			Vector3 originPosition = playerCamera.transform.position - originRotation * headPosition;
			return originPosition + originRotation * ScaleTrackingPosition(trackingPosition);
		}

		private Quaternion TrackingToWorldRotation(Quaternion trackingRotation)
		{
			if (player != null)
			{
				return movementHeadingOriginRotation * Quaternion.Inverse(trackingHeadRotationOrigin) * trackingRotation;
			}

			if (playerCamera == null)
			{
				return trackingRotation;
			}
			Quaternion headRotation = Quaternion.identity;
			if (!headDevice.isValid)
			{
				headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			}
			if (headDevice.isValid)
			{
				headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out headRotation);
			}
			return playerCamera.transform.rotation * Quaternion.Inverse(headRotation) * trackingRotation;
		}

		private void DoLocomotionAction(LocomotionController.LocomotionAction action)
		{
			if (!Service.IsSet<EventDispatcher>()) return;
			InputEvents.Actions inputAction;
			switch (action)
			{
				case LocomotionController.LocomotionAction.Interact:
					inputAction = InputEvents.Actions.Interact;
					break;
				case LocomotionController.LocomotionAction.Jump:
					inputAction = InputEvents.Actions.Jump;
					break;
				case LocomotionController.LocomotionAction.Action1:
					inputAction = InputEvents.Actions.Action1;
					break;
				case LocomotionController.LocomotionAction.Action2:
					inputAction = InputEvents.Actions.Action2;
					break;
				case LocomotionController.LocomotionAction.Action3:
					inputAction = InputEvents.Actions.Action3;
					break;
				default:
					return;
			}

			// Reuse the game's regular input pipeline so controller presses obey the
			// same prop restrictions, action-sequence handling, and confirmations as C.
			Service.Get<EventDispatcher>().DispatchEvent(new InputEvents.ActionEvent(inputAction));
		}

		private void UpdateLocalPenguinAvatar()
		{
			if (player == null) { RestoreLocalPenguinBody(); return; }
			if (bodyVisibilityPlayer != player || avatarRenderer == null)
			{
				RestoreLocalPenguinBody();
				bodyVisibilityPlayer = player;
				avatarRenderer = new PCVRAvatarRenderer(player);
			}
			avatarRenderer.BeginPose(playerCamera);
		}

		private void RestoreLocalPenguinBody()
		{
			avatarRenderer?.Dispose();
			avatarRenderer = null;
			bodyVisibilityPlayer = null;
		}

		private void SetFallbackFlippersVisible(bool visible)
		{
			if (leftFlipper != null) leftFlipper.gameObject.SetActive(visible);
			if (rightFlipper != null) rightFlipper.gameObject.SetActive(visible);
		}
		private void RequestFloorTracking()
		{
			inputSubsystems.Clear();
			SubsystemManager.GetSubsystems(inputSubsystems);
			for (int i = 0; i < inputSubsystems.Count; i++)
			{
				if (inputSubsystems[i].running)
				{
					inputSubsystems[i].TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
				}
			}
		}

		internal void UpdateLocomotion()
		{
			if (!XRSettings.enabled)
			{
				ResetFlipperSwimming();
				return;
			}
			InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			if (!head.isValid || (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked))
			{
				StopTrackedInput();
				return;
			}
			if (!TryAttachToLocalPlayer()) return;
			ApplyTrackedHeadPose();
			MoveAndTurn();
		}

		private void StopTrackedInput()
		{
			ResetFlipperSwimming();
			leftPointer.Cancel();
			rightPointer.Cancel();
			if (player == null) return;
			PenguinUserControl userControl = player.GetComponent<PenguinUserControl>();
			LocomotionTracker tracker = player.GetComponent<LocomotionTracker>();
			if (userControl != null && userControl.isActiveAndEnabled && tracker != null && tracker.GetCurrentController() != null)
				tracker.GetCurrentController().Steer(Vector2.zero);
		}

		private void MoveAndTurn()
		{
			if (player == null || playerCamera == null)
			{
				return;
			}

			Vector2 moveAxis = ApplyRadialDeadzone(ReadAxis(ref leftHand, XRNode.LeftHand), movementStickDeadzone);
			if (!Service.IsSet<EventDispatcher>()) return;
			UIElementDisablerManager disablerManager = Service.IsSet<UIElementDisablerManager>() ? Service.Get<UIElementDisablerManager>() : null;
			bool joystickDisabled = disablerManager != null && disablerManager.IsUIElementDisabled("Joystick");
			// Deliberate stick input can end an interaction authored to abort on
			// input even while that interaction has disabled PenguinUserControl.
			if (!joystickDisabled && moveAxis.sqrMagnitude > 0f && SceneRefs.ActionSequencer != null)
				SceneRefs.ActionSequencer.UserInputReceived();
			PenguinUserControl userControl = player.GetComponent<PenguinUserControl>();
			if (userControl == null || !userControl.isActiveAndEnabled)
			{
				ResetFlipperSwimming();
				return;
			}
			// Tutorial overlays are informational, not modal. Movement must remain
			// available so the player can complete the opening movement prompt.
			if (joystickDisabled)
			{
				moveAxis = Vector2.zero;
			}

			Vector2 turnAxis = ReadAxis(ref rightHand, XRNode.RightHand);
			if (Mathf.Abs(turnAxis.x) < 0.3f)
			{
				snapTurnArmed = true;
			}

			if (!joystickDisabled && snapTurnArmed && Mathf.Abs(turnAxis.x) > 0.7f && !rightPointer.HasPress)
			{
				if (Time.unscaledTime >= nextSnapTurnTime)
				{
					float turnDegrees = Mathf.Sign(turnAxis.x) * snapTurnDegrees;
					Vector3 headBefore = playerCamera.transform.position;
					movementHeadingOriginRotation = Quaternion.AngleAxis(turnDegrees, Vector3.up) * movementHeadingOriginRotation;
					ApplyTrackedHeadPose();
					player.transform.position += headBefore - playerCamera.transform.position;
					ApplyTrackedHeadPose();
					nextSnapTurnTime = Time.unscaledTime + snapTurnCooldown;
					snapTurnArmed = false;
				}
			}

			LocomotionTracker movementTracker = player.GetComponent<LocomotionTracker>();
			SwimController swimController = movementTracker != null ? movementTracker.GetCurrentController() as SwimController : null;
			if (!joystickDisabled && swimController != null && swimController.Active &&
				swimController.CurState == SwimController.State.None && !leftPointer.HasPress && !rightPointer.HasPress)
			{
				float strokePower = UpdateFlipperSwimming();
				moveAxis = Vector2.ClampMagnitude(moveAxis + Vector2.up * strokePower, 1f);
			}
			else ResetFlipperSwimming();

			// Route the stick through the regular input event so action sequencing and
			// non-running locomotion states keep their existing behavior.
			// Idle frames must not keep aborting action sequences. Send zero once
			// when the stick stops, then leave the running interaction alone.
			if (moveAxis != previousMoveAxis || moveAxis.sqrMagnitude > 0f)
				Service.Get<EventDispatcher>().DispatchEvent(new InputEvents.MoveEvent(moveAxis));
			previousMoveAxis = moveAxis;
		}

		private void ResetFlipperSwimming()
		{
			hasLeftSwimPosition = hasRightSwimPosition = false;
			flipperSwimPower = 0f;
			swimInputPlayer = null;
		}

		private float UpdateFlipperSwimming()
		{
			if (swimInputPlayer != player) ResetFlipperSwimming();
			swimInputPlayer = player;
			InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			if (!head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition) ||
				!head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation))
			{
				ResetFlipperSwimming();
				return 0f;
			}
			// Sample in tracking space relative to the head. Walking, snap turns,
			// and avatar root motion must not generate phantom swimming strokes.
			float left = ReadSwimStroke(XRNode.LeftHand, headPosition, headRotation,
				ref previousLeftSwimPosition, ref hasLeftSwimPosition);
			float right = ReadSwimStroke(XRNode.RightHand, headPosition, headRotation,
				ref previousRightSwimPosition, ref hasRightSwimPosition);
			if (!hasLeftSwimPosition && !hasRightSwimPosition) flipperSwimPower = 0f;
			float power = Mathf.Clamp01((left + right) * 0.5f);
			flipperSwimPower = Mathf.Max(power, Mathf.MoveTowards(flipperSwimPower, 0f, Time.deltaTime * 1.8f));
			return flipperSwimPower;
		}

		private static float ReadSwimStroke(XRNode node, Vector3 headPosition, Quaternion headRotation,
			ref Vector3 previousPosition, ref bool hasPosition)
		{
			InputDevice hand = InputDevices.GetDeviceAtXRNode(node);
			if (!hand.isValid || (hand.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) ||
				!hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
			{
				hasPosition = false;
				return 0f;
			}
			position -= headPosition;
			Vector3 delta = position - previousPosition;
			bool canSample = hasPosition && Time.deltaTime > 0f && Time.deltaTime < 0.1f && delta.magnitude < 0.35f;
			previousPosition = position;
			hasPosition = true;
			return canSample ? CalculateSwimStroke(delta / Time.deltaTime, headRotation * Vector3.forward) : 0f;
		}

		internal static float CalculateSwimStroke(Vector3 velocity, Vector3 headForward)
		{
			// Backward pulls and downward flaps propel the penguin. Returning
			// the hands forward/up prepares another stroke without reversing it.
			float pull = Mathf.Max(Vector3.Dot(velocity, -headForward), -velocity.y);
			return Mathf.Clamp01((pull - 0.2f) * 0.7f);
		}

		private static Vector2 ApplyRadialDeadzone(Vector2 axis, float deadzone)
		{
			float magnitude = axis.magnitude;
			if (magnitude <= deadzone)
			{
				return Vector2.zero;
			}

			float remappedMagnitude = Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
			return axis * (remappedMagnitude / magnitude);
		}

		private static Vector2 ReadAxis(ref InputDevice device, XRNode node)
		{
			if (!device.isValid)
			{
				device = InputDevices.GetDeviceAtXRNode(node);
			}

			Vector2 axis;
			if (device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) return Vector2.zero;
			return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis)
				? axis
				: Vector2.zero;
		}
	}
}
