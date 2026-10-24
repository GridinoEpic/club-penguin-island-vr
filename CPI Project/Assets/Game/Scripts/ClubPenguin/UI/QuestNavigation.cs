using ClubPenguin.Adventure;
using Disney.Kelowna.Common;
using Disney.LaunchPadFramework;
using Disney.MobileNetwork;
using Tweaker.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ClubPenguin.UI
{
	public class QuestNavigation : MonoBehaviour
	{
		private enum QuestNavigationState
		{
			inactive,
			offscreen,
			onscreen
		}

		public RectTransform TransformRotation;

		public RectTransform TransformDerotation;

		public RectTransform TransformPosition;

		public GameObject OnScreenGO;

		public GameObject OffScreenGO;

		public Image NavArrow;

		public Image NavCircle;

		public float PADDING_LEFT = 0.07f;

		public float PADDING_RIGHT = 0.07f;

		public float PADDING_TOP = 0.15f;

		public float PADDING_BOTTOM = 0.15f;

		public float MOVEMENT_SPEED = 25f;

		public float ARROW_SCALE_DIST = 40f;

		public float MIN_ARROW_SCALE = 0.5f;

		public float DIST_TEXT_FADE_START_DIST = 5f;

		public float DIST_TEXT_FADE_END_DIST = 3f;

		public Vector3 OnscreenIndicatorOffset = new Vector3(0f, 2f, 0f);

		private bool showOnscreenIndicator;

		private Transform targetTransform;

		private Vector3 customOnscreenIndicatorOffset = Vector3.zero;

		private RectTransform worldContainerRect;

		private RectTransform childContainerRect;

		private QuestNavigationState currentState;

		private bool isOnScreenVisible;

		private bool isOffScreenVisible;

		private bool isDisabled = false;

		private Vector2 desiredIndicatorPos = Vector2.zero;

		private Quaternion desiredIndicatorRot = Quaternion.identity;

		private SpriteContentKey navArrowContentKey = new SpriteContentKey("Sprites/Quests_Inworld_Nav_Arrow_*");

		private SpriteContentKey navCircleContentKey = new SpriteContentKey("Sprites/Quests_Nav_Spot_*");

		private EventChannel eventChannel;

		private void Start()
		{
			Vector2 vector2 = TransformPosition.anchorMax = (TransformPosition.anchorMin = Vector2.zero);
			worldContainerRect = GetComponent<RectTransform>();
			childContainerRect = GetComponent<RectTransform>();
			currentState = QuestNavigationState.inactive;
			OffScreenGO.gameObject.SetActive(false);
			OnScreenGO.gameObject.SetActive(false);
			showOnscreenIndicator = true;
			eventChannel = new EventChannel(Service.Get<EventDispatcher>());
			eventChannel.AddListener<HudEvents.SetNavigationTarget>(onNavigationTargetSet);
			eventChannel.AddListener<CinematicSpeechEvents.ShowSpeechEvent>(onShowSpeech);
			eventChannel.AddListener<CinematicSpeechEvents.SpeechCompleteEvent>(onSpeechComplete);
			eventChannel.AddListener<TrayEvents.TrayOpened>(onTrayOpened);
			eventChannel.AddListener<TrayEvents.TrayClosed>(onTrayClosed);
			eventChannel.AddListener<HudEvents.HideQuestNavigation>(onHideQuestNavigation);
			eventChannel.AddListener<HudEvents.ShowQuestNavigation>(onShowQuestNavigation);
		}

		private void OnDestroy()
		{
			eventChannel.RemoveAllListeners();
		}

		public void Update()
		{
			if (currentState != 0 && targetTransform != null)
			{
				updateIndicatorTransform();
			}
		}

		private bool onNavigationTargetSet(HudEvents.SetNavigationTarget evt)
		{
			targetTransform = evt.TargetTransform;
			showOnscreenIndicator = evt.ShowOnscreenIndicator;
			customOnscreenIndicatorOffset = evt.OnscreenIndicatorOffset;
			if (targetTransform != null)
			{
				updateIndicatorTransform();
				if (currentState == QuestNavigationState.inactive)
				{
					TransformPosition.anchoredPosition = desiredIndicatorPos;
				}
			}
			else
			{
				setState(QuestNavigationState.inactive);
			}
			return false;
		}

		private bool onShowSpeech(CinematicSpeechEvents.ShowSpeechEvent evt)
		{
			hideQuestNavigation();
			return false;
		}

		private bool onSpeechComplete(CinematicSpeechEvents.SpeechCompleteEvent evt)
		{
			showQuestNavigation();
			return false;
		}

		private void updateIndicatorTransform()
		{
			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
			{
				updateWorldSpaceIndicatorTransform(canvas.rootCanvas.worldCamera != null ? canvas.rootCanvas.worldCamera : Camera.main);
				return;
			}
			Vector3 viewportPos = Camera.main.WorldToViewportPoint(targetTransform.position);
			bool snap = false;
			if (viewportPos.x < 0f || viewportPos.x > 1f || viewportPos.y < 0.11f || viewportPos.y > 1f || viewportPos.z < 0f)
			{
				if (currentState != QuestNavigationState.offscreen)
				{
					setState(QuestNavigationState.offscreen);
					snap = true;
				}
				updateOffscreenIndicatorTransform(ref viewportPos);
			}
			else
			{
				if (currentState != QuestNavigationState.onscreen)
				{
					setState(QuestNavigationState.onscreen);
					snap = true;
				}
				updateOnscreenIndicatorTransform();
			}
			applyDesiredIndicatorTransform(snap);
		}

		// Called after the VR bridge has applied the latest head pose and HUD pose.
		public void RefreshWorldSpaceIndicator()
		{
			if (worldContainerRect != null && targetTransform != null && currentState != QuestNavigationState.inactive)
			{
				updateIndicatorTransform();
			}
		}

		private void updateWorldSpaceIndicatorTransform(Camera camera)
		{
			if (camera == null) return;
			Vector3 target = targetTransform.position + OnscreenIndicatorOffset + customOnscreenIndicatorOffset;
			Vector3 eye = worldContainerRect.InverseTransformPoint(camera.transform.position);
			Vector3 direction = worldContainerRect.InverseTransformPoint(target) - eye;
			// Intersect the eye-to-target ray with the actual UI plane. Viewport fractions
			// are not coordinates on a world-space canvas, especially when looking up/down.
			float distanceToPlane = -eye.z;
			bool inFront = direction.z * distanceToPlane > 0f;
			float projection = Mathf.Abs(distanceToPlane) / Mathf.Max(0.0001f, Mathf.Abs(direction.z));
			Vector2 point = new Vector2(eye.x + direction.x * projection, eye.y + direction.y * projection);
			Rect rect = worldContainerRect.rect;
			float left = rect.xMin + rect.width * PADDING_LEFT;
			float right = rect.xMax - rect.width * PADDING_RIGHT;
			float bottom = rect.yMin + rect.height * PADDING_BOTTOM;
			float top = rect.yMax - rect.height * PADDING_TOP;
			// The HUD occupies only part of the headset view. A nearby objective
			// must not turn into an edge dot just because its raised arrow crosses
			// the HUD boundary. Classify the objective itself using the eye view,
			// then keep its marker inside the readable panel area.
			Vector3 viewport = camera.WorldToViewportPoint(targetTransform.position);
			bool onPanel = inFront && viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f &&
				viewport.y >= 0f && viewport.y <= 1f;
			setState(onPanel ? QuestNavigationState.onscreen : QuestNavigationState.offscreen);
			if (onPanel)
			{
				point.x = Mathf.Clamp(point.x, left, right);
				point.y = Mathf.Clamp(point.y, bottom, top);
				desiredIndicatorRot = Quaternion.identity;
			}
			else
			{
				Vector2 heading = point - rect.center;
				if (heading.sqrMagnitude < 0.0001f) heading = Vector2.down;
				desiredIndicatorRot = Quaternion.Euler(0f, 0f, Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg - 90f);
				float horizontal = heading.x > 0f ? (right - rect.center.x) / heading.x :
					heading.x < 0f ? (left - rect.center.x) / heading.x : float.PositiveInfinity;
				float vertical = heading.y > 0f ? (top - rect.center.y) / heading.y :
					heading.y < 0f ? (bottom - rect.center.y) / heading.y : float.PositiveInfinity;
				point = rect.center + heading * Mathf.Min(horizontal, vertical);
			}
			// Account for the position transform's parent, anchors and pivot.
			RectTransform parent = (RectTransform)TransformPosition.parent;
			Vector3 localPoint = parent.InverseTransformPoint(worldContainerRect.TransformPoint(new Vector3(point.x, point.y, 0f)));
			Vector2 anchor = TransformPosition.anchorMin + Vector2.Scale(TransformPosition.anchorMax - TransformPosition.anchorMin, TransformPosition.pivot);
			Vector2 anchorPosition = parent.rect.min + Vector2.Scale(parent.rect.size, anchor);
			desiredIndicatorPos = new Vector2(localPoint.x, localPoint.y) - anchorPosition;
			// Head movement must not leave the marker trailing behind its objective.
			applyDesiredIndicatorTransform(true);
			if (TransformDerotation != null) TransformDerotation.rotation = worldContainerRect.rotation;
			GameObject player = SceneRefs.ZoneLocalPlayerManager != null ? SceneRefs.ZoneLocalPlayerManager.LocalPlayerGameObject : null;
			float distance = Vector3.Distance(targetTransform.position, player != null ? player.transform.position : camera.transform.position);
			Text label = (onPanel ? OnScreenGO : OffScreenGO).GetComponentInChildren<Text>(true);
			if (label != null)
			{
				label.text = string.Format("{0}m", (int)distance);
				float alpha = onPanel ? Mathf.InverseLerp(DIST_TEXT_FADE_END_DIST, DIST_TEXT_FADE_START_DIST, distance) : 1f;
				label.color = new Color(1f, 1f, 1f, alpha);
			}
			float scale = Mathf.Max(MIN_ARROW_SCALE, 1f - Vector3.Distance(targetTransform.position, camera.transform.position) / ARROW_SCALE_DIST);
			OnScreenGO.transform.localScale = new Vector3(scale, scale, 1f);
		}

		private Vector2 calcDesiredScreenOffset()
		{
			return new Vector2(worldContainerRect.rect.width - childContainerRect.rect.width, 0f - (worldContainerRect.rect.height - childContainerRect.rect.height));
		}

		private void applyDesiredIndicatorTransform(bool snap)
		{
			TransformPosition.anchoredPosition = (snap ? desiredIndicatorPos : Vector2.Lerp(TransformPosition.anchoredPosition, desiredIndicatorPos, Time.deltaTime * MOVEMENT_SPEED));
			TransformRotation.localRotation = desiredIndicatorRot;
		}

		private void updateOnscreenIndicatorTransform()
		{
			Vector3 vector = Camera.main.WorldToViewportPoint(targetTransform.position + OnscreenIndicatorOffset + customOnscreenIndicatorOffset);
			desiredIndicatorPos.x = Mathf.Clamp(vector.x, PADDING_LEFT, 1f - PADDING_RIGHT);
			desiredIndicatorPos.y = Mathf.Clamp(vector.y, PADDING_BOTTOM, 1f - PADDING_TOP);
			desiredIndicatorPos.x = worldContainerRect.rect.width * vector.x;
			desiredIndicatorPos.y = worldContainerRect.rect.height * vector.y;
			desiredIndicatorPos += calcDesiredScreenOffset();
			GameObject localPlayerGameObject = SceneRefs.ZoneLocalPlayerManager.LocalPlayerGameObject;
			float num = Vector3.Distance(targetTransform.position, localPlayerGameObject.transform.position);
			float num2 = Vector3.Distance(targetTransform.position, Camera.main.transform.position);
			float b = 1f - Mathf.Max(0f, num2 / ARROW_SCALE_DIST);
			b = Mathf.Max(MIN_ARROW_SCALE, b);
			float a = 1f;
			if (num < DIST_TEXT_FADE_START_DIST)
			{
				float num3 = num - DIST_TEXT_FADE_END_DIST;
				a = num3 / (DIST_TEXT_FADE_START_DIST - DIST_TEXT_FADE_END_DIST);
			}
			OnScreenGO.GetComponentInChildren<Text>().color = new Color(1f, 1f, 1f, a);
			OnScreenGO.GetComponentInChildren<Text>().text = string.Format("{0}m", (int)num);
			OnScreenGO.transform.localScale = new Vector3(b, b, 1f);
		}

		private void updateOffscreenIndicatorTransform(ref Vector3 viewportPos)
		{
			Vector2 vector = new Vector2(worldContainerRect.rect.width, worldContainerRect.rect.height);
			if (viewportPos.z < 0f)
			{
				viewportPos.x = 0f - viewportPos.x;
				viewportPos.y = 0f - viewportPos.y;
			}
			Vector2 vector2 = viewportPos;
			vector2.x -= 0.5f;
			vector2.y -= 0.5f;
			float z = Mathf.Atan2(vector2.y, vector2.x) * 57.29578f - 90f;
			desiredIndicatorRot = Quaternion.Euler(0f, 0f, z);
			if (TransformDerotation != null)
			{
				Vector3 eulerAngles = TransformRotation.rotation.eulerAngles;
				eulerAngles.z = 0f;
				TransformDerotation.rotation = Quaternion.Euler(eulerAngles);
			}
			float num = vector2.y / vector2.x;
			vector2.y = Mathf.Clamp(vector2.y, 0f - (0.5f - PADDING_BOTTOM), 0.5f - PADDING_TOP);
			vector2.x = vector2.y / num;
			vector2.x = Mathf.Clamp(vector2.x, 0f - (0.5f - PADDING_LEFT), 0.5f - PADDING_RIGHT);
			vector2.x = (vector2.x + 0.5f) * vector.x;
			vector2.y = (vector2.y + 0.5f) * vector.y;
			desiredIndicatorPos = vector2;
			desiredIndicatorPos += calcDesiredScreenOffset();
			GameObject localPlayerGameObject = SceneRefs.ZoneLocalPlayerManager.LocalPlayerGameObject;
			float num2 = Vector3.Distance(targetTransform.position, localPlayerGameObject.transform.position);
			OffScreenGO.GetComponentInChildren<Text>().text = string.Format("{0}m", (int)num2);
		}

		[Invokable("Quest.SetNavigationTarget")]
		public static void TweakerSetNavigationTarget(string targetName)
		{
			GameObject gameObject = GameObject.Find(targetName);
			if (gameObject != null)
			{
				Service.Get<EventDispatcher>().DispatchEvent(new HudEvents.SetNavigationTarget(gameObject.transform));
			}
			else
			{
				Service.Get<EventDispatcher>().DispatchEvent(new HudEvents.SetNavigationTarget(null));
			}
		}

		private void setState(QuestNavigationState newState)
		{
			if (currentState != newState)
			{
				switch (newState)
				{
				case QuestNavigationState.inactive:
					isOffScreenVisible = false;
					isOnScreenVisible = false;
					break;
				case QuestNavigationState.offscreen:
					isOffScreenVisible = true;
					isOnScreenVisible = false;
					break;
				case QuestNavigationState.onscreen:
					isOffScreenVisible = false;
					isOnScreenVisible = showOnscreenIndicator;
					break;
				}
				if (currentState == QuestNavigationState.inactive)
				{
					updateNavigationSprites();
				}
				else
				{
					updateVisibility();
				}
				currentState = newState;
			}
		}

		private void updateNavigationSprites()
		{
			Quest activeQuest = Service.Get<QuestService>().ActiveQuest;
			if (activeQuest != null)
			{
				string abbreviatedName = activeQuest.Mascot.AbbreviatedName;
				Content.LoadAsync(onNavArrowLoaded, navArrowContentKey, abbreviatedName);
				Content.LoadAsync(onNavCircleLoaded, navCircleContentKey, abbreviatedName);
			}
			else
			{
				updateVisibility();
			}
		}

		private void updateVisibility()
		{
			OffScreenGO.SetActive(isOffScreenVisible && !isDisabled);
			OnScreenGO.SetActive(isOnScreenVisible && !isDisabled);
		}

		private void onNavArrowLoaded(string path, Sprite navArrow)
		{
			NavArrow.sprite = navArrow;
			OnScreenGO.SetActive(isOnScreenVisible && !isDisabled);
		}

		private void onNavCircleLoaded(string path, Sprite navCircle)
		{
			NavCircle.sprite = navCircle;
			OffScreenGO.SetActive(isOffScreenVisible && !isDisabled);
		}

		private bool onTrayOpened(TrayEvents.TrayOpened evt)
		{
			if (currentState == QuestNavigationState.offscreen)
			{
				OffScreenGO.SetActive(false);
			}
			return false;
		}

		private bool onTrayClosed(TrayEvents.TrayClosed evt)
		{
			if (currentState == QuestNavigationState.offscreen)
			{
				OffScreenGO.SetActive(true);
			}
			return false;
		}

		private bool onHideQuestNavigation(HudEvents.HideQuestNavigation evt)
		{
			hideQuestNavigation();
			return false;
		}

		private bool onShowQuestNavigation(HudEvents.ShowQuestNavigation evt)
		{
			showQuestNavigation();
			return false;
		}

		private void hideQuestNavigation()
		{
			OnScreenGO.SetActive(false);
			OffScreenGO.SetActive(false);
			isDisabled = true;
		}

		private void showQuestNavigation()
		{
			if (currentState == QuestNavigationState.onscreen)
			{
				OnScreenGO.SetActive(true);
			}
			else if (currentState == QuestNavigationState.offscreen)
			{
				OffScreenGO.SetActive(true);
			}
			isDisabled = false;
		}
	}
}
