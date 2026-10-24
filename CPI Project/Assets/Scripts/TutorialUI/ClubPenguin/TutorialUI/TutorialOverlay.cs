using ClubPenguin.Core;
using ClubPenguin.UI;
using Disney.Kelowna.Common;
using Disney.Kelowna.Common.DataModel;
using Disney.LaunchPadFramework;
using Disney.MobileNetwork;
using UnityEngine;
using UnityEngine.UI;

namespace ClubPenguin.TutorialUI
{
	public class TutorialOverlay : MonoBehaviour
	{
		public const float DEFAULT_OVERLAY_OPACITY = 0.6f;

		private const float EXTRA_SCREEN_SPACE = 200f;

		private static SpriteContentKey arrowIconContentKey = new SpriteContentKey("Images/TutorialOverlayArrow_*");

		public Sprite CircleSprite;

		public Sprite RectangleSprite;

		public Sprite SquareSprite;

		public GameObject HighlightOutlinePrefab;

		public RectTransform BackgroundShape;

		public RectTransform LeftBG;

		public RectTransform RightBG;

		public RectTransform TopBG;

		public RectTransform BottomBG;

		public RectTransform CenterBGGroup;

		public RectTransform PopupPanel;

		public RectTransform TextBox;

		public RectTransform PopupArrow;

		public Text PopupText;

		public RectTransform HighlightOutlineContainer;

		private Vector2 highlightPosition = Vector2.zero;

		private Vector2 highlightSize = new Vector2(100f, 100f);

		private DTutorialOverlay data;

		private bool isShowing = false;

		private GameObject HighlightOutline;

		private bool enableUIElementsOnClose = false;

		private bool hasDispatchedHideEvents = false;

		// VR raycasters need to distinguish a guided target from the decorative
        // canvas surrounding it, including siblings outside this CanvasGroup.
        public GameObject InteractiveTarget => isShowing && data != null && data.EnableTarget ? data.Target : null;

        public Vector2 HighlightPosition
		{
			get
			{
				return highlightPosition;
			}
		}

		public Vector2 HighlightSize
		{
			get
			{
				return highlightSize;
			}
		}

		public bool IsShowing
		{
			get
			{
				return isShowing;
			}
		}

		public void SetHighlight(DTutorialOverlay overlayData)
		{
			data = overlayData;
			isShowing = true;
			Vector3 worldPoint = Vector3.zero;
			Vector2 targetCanvasPosition = Vector2.zero;
			bool hasTargetCanvasPosition = false;
			highlightSize = Vector2.zero;
			if (data.Target != null)
			{
				RectTransform component = data.Target.GetComponent<RectTransform>();
				if (component != null)
				{
					Canvas componentInParent = data.Target.GetComponentInParent<Canvas>();
					worldPoint = component.position;
					Canvas overlayCanvas = GetComponentInParent<Canvas>();
					RectTransform overlayRect = overlayCanvas != null
						? overlayCanvas.rootCanvas.GetComponent<RectTransform>()
						: null;
					if (componentInParent != null && overlayRect != null &&
						componentInParent.rootCanvas.renderMode == RenderMode.WorldSpace &&
						overlayCanvas.rootCanvas.renderMode == RenderMode.WorldSpace)
					{
						// Both canvases have been converted to world space for VR. Map the
						// target directly into the overlay's own design units; projecting to
						// headset pixels shrinks the target toward the screen center.
						Vector3 localTarget = overlayRect.InverseTransformPoint(component.position);
						targetCanvasPosition = new Vector2(
							localTarget.x - overlayRect.rect.xMin,
							localTarget.y - overlayRect.rect.yMin);
						hasTargetCanvasPosition = true;
					}
					else if (componentInParent != null &&
						(componentInParent.renderMode == RenderMode.ScreenSpaceCamera ||
						 componentInParent.renderMode == RenderMode.WorldSpace))
					{
						// The PCVR bridge converts screen overlay canvases to World Space.
						// Tutorial positions are laid out in screen coordinates, so project
						// the target back to pixels before positioning the highlight/callout.
						Camera targetCamera = componentInParent.worldCamera != null
							? componentInParent.worldCamera
							: Camera.main;
						if (targetCamera != null)
						{
							worldPoint = RectTransformUtility.WorldToScreenPoint(targetCamera, worldPoint);
						}
					}
					if (data.AutoSize)
					{
						highlightSize = component.rect.size;
					}
				}
				else
				{
					worldPoint = Camera.main.WorldToScreenPoint(data.Target.transform.position);
				}
			}
			CanvasScalerExt component2 = GetComponentInParent<Canvas>().GetComponent<CanvasScalerExt>();
			Vector2 vector = new Vector2(component2.ReferenceResolutionY / (float)Screen.height, component2.ReferenceResolutionY / (float)Screen.height);
			vector *= 1f / component2.ScaleModifier;
			int num = 0;
			DataEntityHandle entityByType = Service.Get<CPDataEntityCollection>().GetEntityByType<SystemBarsData>();
			if (!entityByType.IsNull)
			{
				SystemBarsData component3 = Service.Get<CPDataEntityCollection>().GetComponent<SystemBarsData>(entityByType);
				num = component3.CurrentNavigationBarHeight;
			}
			if (data.Target != null && hasTargetCanvasPosition)
			{
				highlightPosition = targetCanvasPosition + data.Position;
			}
			else if (data.Target != null)
			{
				highlightPosition = new Vector2((data.Position.x + worldPoint.x) * vector.x, (data.Position.y - (float)num + worldPoint.y) * vector.y);
			}
			else
			{
				highlightPosition = new Vector2((float)Screen.width * data.Position.x * vector.x, (float)Screen.height * (data.Position.y - (float)num) * vector.y);
			}
			highlightSize += data.Size;
			Color color = new Color(1f, 1f, 1f, Mathf.Clamp(data.Opacity, 0f, 1f));
			BackgroundShape.GetComponent<Image>().sprite = getShapeSprite(data.Shape);
			BackgroundShape.GetComponent<Image>().color = color;
			LeftBG.GetComponent<Image>().color = color;
			RightBG.GetComponent<Image>().color = color;
			TopBG.GetComponent<Image>().color = color;
			BottomBG.GetComponent<Image>().color = color;
			updateBackground();
			updatePopup();
			isShowing = true;
			base.gameObject.SetActive(true);
			if (data.DisableUI)
			{
				Service.Get<EventDispatcher>().DispatchEvent(default(UIDisablerEvents.DisableAllUIElements));
				Service.Get<EventDispatcher>().DispatchEvent(new UIDisablerEvents.DisableUIElement("Joystick"));
				if (data.Target != null && data.EnableTarget)
				{
					UIElementDisabler component4 = data.Target.GetComponentInParent<UIElementDisabler>() ?? data.Target.GetComponentInChildren<UIElementDisabler>();
					if (component4 != null)
					{
						Service.Get<EventDispatcher>().DispatchEvent(new UIDisablerEvents.EnableUIElement(component4.UIElementID));
					}
				}
			}
			if (data.Target != null && data.EnableTarget)
            {
                Button targetButton = data.Target.GetComponentInParent<Button>() ?? data.Target.GetComponentInChildren<Button>();
                if (targetButton != null) targetButton.interactable = true;
            }
            UpdateHighlightOutline(data.ShowHighlightOutline);
			enableUIElementsOnClose = data.DisableUI;
			// A targeted tutorial has to leave the highlighted control reachable.
			// This overlay is a full-screen graphic, so blocking raycasts here would
			// cover the target as well as the dimmed background.
			GetComponent<CanvasGroup>().blocksRaycasts = data.BlocksRaycast && data.Target == null;
			Service.Get<BackButtonController>().Add(onBackClicked);
		}

		private void onBackClicked()
		{
			Service.Get<EventDispatcher>().DispatchEvent(default(TutorialUIEvents.HideHighlightOverlay));
			Service.Get<EventDispatcher>().DispatchEvent(default(TutorialUIEvents.ResetTutorialUISorting));
			if (Service.IsSet<ClubPenguin.Tutorial.TutorialManager>())
				Service.Get<ClubPenguin.Tutorial.TutorialManager>().EndTutorial();
		}

		private void LateUpdate()
		{
			if (!isShowing || data == null) return;
			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.WorldSpace) return;
			RectTransform canvasRect = canvas.rootCanvas.GetComponent<RectTransform>();
			Vector2 position = GetWorldCanvasHighlightPosition(canvasRect, data.Target, data.Position);
			Vector2 size = data.Size;
			RectTransform targetRect = data.Target != null ? data.Target.GetComponent<RectTransform>() : null;
			if (data.AutoSize && targetRect != null)
			{
				Vector3[] corners = new Vector3[4];
				targetRect.GetWorldCorners(corners);
				size += new Vector2(
					canvasRect.InverseTransformVector(corners[3] - corners[0]).magnitude,
					canvasRect.InverseTransformVector(corners[1] - corners[0]).magnitude);
			}
			if (position == highlightPosition && size == highlightSize) return;
			highlightPosition = position;
			highlightSize = size;
			updateBackground();
			updatePopup(false);
			UpdateHighlightOutline(data.ShowHighlightOutline);
		}

		private static Vector2 GetWorldCanvasHighlightPosition(RectTransform canvasRect, GameObject target, Vector2 position)
		{
			if (target == null) return Vector2.Scale(canvasRect.rect.size, position);
			Vector3 localTarget = canvasRect.InverseTransformPoint(target.transform.position);
			return new Vector2(localTarget.x - canvasRect.rect.xMin, localTarget.y - canvasRect.rect.yMin) + position;
		}

		public void Hide()
		{
			if (!isShowing) return;
			isShowing = false;
			dispatchHideEvents();
			// UI animators can be paused or culled in VR. Closing a tutorial
			// must remove its graphics and hit targets without an animation event.
			base.gameObject.SetActive(false);
			Object.Destroy(base.gameObject);
		}

		private void dispatchHideEvents()
		{
			if (Service.IsSet<BackButtonController>()) Service.Get<BackButtonController>().Remove(onBackClicked);
			if (!hasDispatchedHideEvents && enableUIElementsOnClose && Service.IsSet<EventDispatcher>())
			{
				Service.Get<EventDispatcher>().DispatchEvent(default(UIDisablerEvents.EnableAllUIElements));
				hasDispatchedHideEvents = true;
			}
		}

		private void OnDestroy()
		{
			dispatchHideEvents();
		}

		public void OnCloseAnimationComplete()
		{
			Object.Destroy(base.gameObject);
		}

		private void updateBackground()
		{
			Vector2 vector = highlightSize * 0.5f;
			TopBG.GetComponent<LayoutElement>().flexibleHeight = 1000f;
			BottomBG.GetComponent<LayoutElement>().flexibleHeight = 0f;
			BottomBG.GetComponent<LayoutElement>().preferredHeight = highlightPosition.y - vector.y + 200f;
			LeftBG.GetComponent<LayoutElement>().flexibleWidth = 0f;
			LeftBG.GetComponent<LayoutElement>().preferredWidth = highlightPosition.x - vector.x + 200f;
			BackgroundShape.GetComponent<LayoutElement>().preferredWidth = highlightSize.x;
			CenterBGGroup.GetComponent<LayoutElement>().preferredHeight = highlightSize.y;
		}

		private void updatePopup(bool loadArrow = true)
		{
			if (string.IsNullOrEmpty(data.Text))
			{
				TextBox.gameObject.SetActive(false);
			}
			else
			{
				TextBox.gameObject.SetActive(true);
				PopupText.text = data.Text;
			}
			TextBox.GetComponent<RectTransform>().pivot = data.TextBoxPivot;
			Canvas.ForceUpdateCanvases();
			Vector2 vector = TextBox.sizeDelta * 0.5f;
			if (data.MaxTextBoxSize != 0f && PopupText.rectTransform.sizeDelta.x > data.MaxTextBoxSize)
			{
				PopupText.GetComponent<LayoutElement>().preferredWidth = data.MaxTextBoxSize;
				vector = new Vector2(data.MaxTextBoxSize * 0.5f, TextBox.sizeDelta.y);
			}
			Vector2 vector2 = PopupArrow.sizeDelta * 0.5f;
			Vector2 vector3 = HighlightSize * 0.5f;
			string text = "";
			Vector2 b;
			Vector2 b2;
			switch (data.ArrowPosition)
			{
			case TutorialOverlayArrowPosition.LEFT:
				text = "Left";
				b = new Vector2(0f - vector2.x - vector3.x - 10f + data.ArrowOffset.x, data.ArrowOffset.y);
				b2 = new Vector2(0f - vector.x - vector3.x - PopupArrow.sizeDelta.x - 60f + data.TextBoxOffset.x, data.TextBoxOffset.y);
				break;
			case TutorialOverlayArrowPosition.RIGHT:
				text = "Right";
				b = new Vector2(vector2.x + vector3.x + 10f + data.ArrowOffset.x, data.ArrowOffset.y);
				b2 = new Vector2(vector.x + vector3.x + PopupArrow.sizeDelta.x + 60f + data.TextBoxOffset.x, data.TextBoxOffset.y);
				break;
			case TutorialOverlayArrowPosition.TOP:
				text = "Top";
				b = new Vector2(data.ArrowOffset.x, vector2.y + vector3.y + 10f + data.ArrowOffset.y);
				b2 = new Vector2(data.TextBoxOffset.x, vector.y + vector3.y + PopupArrow.sizeDelta.y + 60f + data.TextBoxOffset.y);
				break;
			case TutorialOverlayArrowPosition.BOTTOM:
				text = "Bottom";
				b = new Vector2(data.ArrowOffset.x, 0f - vector2.y - vector3.y - 10f + data.ArrowOffset.y);
				b2 = new Vector2(data.TextBoxOffset.x, 0f - vector.y - vector3.y - PopupArrow.sizeDelta.y - 60f + data.TextBoxOffset.y);
				break;
			default:
				b = Vector2.zero;
				b2 = Vector2.zero;
				break;
			}
			PopupArrow.anchoredPosition = highlightPosition + b + new Vector2(200f, 200f);
			TextBox.anchoredPosition = highlightPosition + b2 + new Vector2(200f, 200f);
			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
				ClampTextBoxToCanvas(TextBox, canvas.rootCanvas.GetComponent<RectTransform>());
			if (!loadArrow) return;
			PopupArrow.gameObject.SetActive(false);
			if (data.ShowArrow)
			{
				Content.LoadAsync(onArrowAssetLoaded, arrowIconContentKey, text);
			}
		}

		private static void ClampTextBoxToCanvas(RectTransform textBox, RectTransform canvasRect)
		{
			Canvas.ForceUpdateCanvases();
			RectTransform parent = textBox.parent as RectTransform;
			if (parent == null) return;
			Vector3[] corners = new Vector3[4];
			canvasRect.GetWorldCorners(corners);
			Vector2 min = parent.InverseTransformPoint(corners[0]);
			Vector2 max = parent.InverseTransformPoint(corners[2]);
			textBox.GetWorldCorners(corners);
			Vector2 textMin = parent.InverseTransformPoint(corners[0]);
			Vector2 textMax = parent.InverseTransformPoint(corners[2]);
			Vector2 correction = Vector2.zero;
			const float margin = 24f;
			if (textMin.x < min.x + margin) correction.x = min.x + margin - textMin.x;
			else if (textMax.x > max.x - margin) correction.x = max.x - margin - textMax.x;
			if (textMin.y < min.y + margin) correction.y = min.y + margin - textMin.y;
			else if (textMax.y > max.y - margin) correction.y = max.y - margin - textMax.y;
			textBox.anchoredPosition += correction;
		}

		private void UpdateHighlightOutline(bool showOutline)
		{
			if (showOutline)
			{
				if (HighlightOutline == null)
				{
					HighlightOutline = Object.Instantiate(HighlightOutlinePrefab);
					HighlightOutline.transform.SetParent(HighlightOutlineContainer.transform, false);
				}
				HighlightOutline.GetComponent<RectTransform>().anchoredPosition = highlightPosition + new Vector2(200f, 200f);
				RectTransform[] componentsInChildren = HighlightOutline.GetComponentsInChildren<RectTransform>();
				for (int i = 0; i < componentsInChildren.Length; i++)
				{
					if (componentsInChildren[i] != HighlightOutline.transform)
					{
						componentsInChildren[i].sizeDelta = highlightSize;
					}
				}
			}
			else if (HighlightOutline != null)
			{
				Object.Destroy(HighlightOutline);
			}
		}

		private void onArrowAssetLoaded(string path, Sprite sprite)
		{
			if (this == null || !isShowing) return;
			PopupArrow.transform.Find("Icon").GetComponent<Image>().sprite = sprite;
			PopupArrow.gameObject.SetActive(true);
			if (data.ArrowPosition == TutorialOverlayArrowPosition.LEFT || data.ArrowPosition == TutorialOverlayArrowPosition.RIGHT)
			{
				PopupArrow.Find("Icon").GetComponent<Animator>().SetBool("UpDown", false);
			}
			else
			{
				PopupArrow.Find("Icon").GetComponent<Animator>().SetBool("UpDown", true);
			}
		}

		private Sprite getShapeSprite(TutorialOverlayShape shape)
		{
			Sprite result = null;
			switch (shape)
			{
			case TutorialOverlayShape.CIRCLE:
				result = CircleSprite;
				break;
			case TutorialOverlayShape.RECTANGLE:
				result = RectangleSprite;
				break;
			case TutorialOverlayShape.NONE:
				result = SquareSprite;
				break;
			}
			return result;
		}
	}
}
