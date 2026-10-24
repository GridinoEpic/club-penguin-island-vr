using System;
using System.Reflection;
using System.IO;
using ClubPenguin;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run without a headset: Unity -batchmode -executeMethod PCVRInteractionChecks.Run.
internal static class PCVRInteractionChecks
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static PCVRFirstPersonController controller;
	private static int checks;
	private const string RequestPath = "Library/PCVR_UI_CHECK_REQUEST.txt";
	private const string ResultPath = "Library/PCVR_UI_CHECK_RESULT.txt";

	[InitializeOnLoadMethod]
	private static void RegisterChecks()
	{
		EditorApplication.update += () =>
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
				!File.Exists(RequestPath)) return;
			bool rebuildMenu = File.ReadAllText(RequestPath).Trim() == "build-menu-avatar";
			File.Delete(RequestPath);
			try
			{
				if (rebuildMenu) PCVRMenuAvatarBuilder.Rebuild();
				Run();
				PCVRAvatarValidation.Run();
				File.WriteAllText(ResultPath, $"PASSED: {checks} checks");
			}
			catch (Exception exception)
			{
				File.WriteAllText(ResultPath, exception.ToString());
				Debug.LogException(exception);
			}
		};
	}

	[MenuItem("CPI VR/Validate UI interaction")]
	public static void Run()
	{
		checks = 0;
		UnityEngine.SceneManagement.Scene previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
		UnityEngine.SceneManagement.Scene testScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
			UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            Application.isBatchMode && string.IsNullOrEmpty(previousScene.path)
                ? UnityEditor.SceneManagement.NewSceneMode.Single
                : UnityEditor.SceneManagement.NewSceneMode.Additive);
		UnityEngine.SceneManagement.SceneManager.SetActiveScene(testScene);
		GameObject root = new GameObject("PCVR interaction checks");
		try
		{
			GameObject host = new GameObject("Inactive controller");
			host.transform.SetParent(root.transform);
			host.SetActive(false);
			controller = host.AddComponent<PCVRFirstPersonController>();
			GameObject cameraObject = new GameObject("Test camera", typeof(Camera));
			cameraObject.transform.SetParent(root.transform);
			Camera camera = cameraObject.GetComponent<Camera>();
			camera.tag = "MainCamera";
			camera.pixelRect = new Rect(0, 0, 800, 600);
			Set("playerCamera", camera);
			GameObject eventObject = new GameObject("Event system", typeof(EventSystem));
			eventObject.transform.SetParent(root.transform);
			EventSystem events = eventObject.GetComponent<EventSystem>();
			GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
			panel.transform.SetParent(root.transform);
			Canvas canvas = panel.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.WorldSpace;
			canvas.worldCamera = camera;
			panel.transform.position = new Vector3(0, 0, 3);
			((RectTransform)panel.transform).sizeDelta = new Vector2(4, 3);
			Image background = MakeImage("Background", panel.transform, Vector3.zero, new Vector2(4, 3));
			Image buttonImage = MakeImage("My Penguin", panel.transform, new Vector3(0.5f, 0, -1), new Vector2(0.15f, 0.15f));
			Button button = buttonImage.gameObject.AddComponent<Button>();
			int clicks = 0;
			button.onClick.AddListener(() => clicks++);
			Canvas.ForceUpdateCanvases();
			camera.Render();
			Set("cachedCanvases", new[] { canvas });
			Set("nextCanvasScanTime", float.MaxValue);
			Set("vrUiManuallyVisible", true);
			Vector3 origin = new Vector3(1, 0, 0);
			Vector3 direction = (buttonImage.transform.position - origin).normalized;
			RaycastResult hit = Hit(origin, direction);
			Require(hit.gameObject == button.gameObject, "Offset hand ray hits the raised button despite a background hit on the root plane");
			PCVRPointer pointer = new PCVRPointer(-101);
			pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
			pointer.Process(events, hit, hit.screenPosition, false, true, Vector2.zero, false);
			Require(clicks == 1, "Trigger press/release delivers exactly one button click");
			pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
			pointer.Cancel();
			pointer.Process(events, hit, hit.screenPosition, false, true, Vector2.zero, false);
			Require(clicks == 1, "Cancelled press does not click after UI hides");
            ScrollRect scroller = panel.AddComponent<ScrollRect>();
            scroller.content = (RectTransform)buttonImage.transform;
            pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
            pointer.Process(events, hit, hit.screenPosition + new Vector2(8f, 0f), false, false, Vector2.zero, false);
            pointer.Process(events, hit, hit.screenPosition + new Vector2(8f, 0f), false, true, Vector2.zero, false);
            Require(clicks == 2, "Store button clicks survive small hand jitter inside a scroll view");
            pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
            pointer.Process(events, hit, hit.screenPosition + new Vector2(40f, 0f), false, false, Vector2.zero, false);
            pointer.Process(events, hit, hit.screenPosition + new Vector2(40f, 0f), false, true, Vector2.zero, false);
            Require(clicks == 2, "Deliberate scrolling does not also activate the store button");
            UnityEngine.Object.DestroyImmediate(scroller);
            buttonImage.transform.localPosition = new Vector3(0.5f, 0, -1);
            Canvas.ForceUpdateCanvases();
            var pressAnywhere = typeof(ClubPenguin.Adventure.PressAnywhereAction);
            var downSequence = pressAnywhere.GetField("vrDownSequence", BindingFlags.Static | BindingFlags.NonPublic);
            var upSequence = pressAnywhere.GetField("vrUpSequence", BindingFlags.Static | BindingFlags.NonPublic);
            object downBefore = downSequence.GetValue(null), upBefore = upSequence.GetValue(null);
			var anyKeyRelease = typeof(ClubPenguin.Adventure.InputAnyKeyUpAction).GetField("vrReleaseSequence", BindingFlags.Static | BindingFlags.NonPublic);
			uint releaseBefore = (uint)anyKeyRelease.GetValue(null);
            Call("ProcessUiPointer", UnityEngine.XR.XRNode.LeftHand, Vector3.zero, Quaternion.identity, true, false, null);
			Require((uint)anyKeyRelease.GetValue(null) == releaseBefore, "Any-key tutorial waits for trigger release");
            Call("ProcessUiPointer", UnityEngine.XR.XRNode.LeftHand, Vector3.zero, Quaternion.identity, false, true, null);
			Require((uint)anyKeyRelease.GetValue(null) == releaseBefore + 1, "UI-owned trigger release reaches informational tutorial input");
            Require(downBefore.Equals(downSequence.GetValue(null)) && upBefore.Equals(upSequence.GetValue(null)),
                "Clicking a screen background does not also advance press-anywhere quest input");
            Call("ProcessUiPointer", UnityEngine.XR.XRNode.LeftHand, origin, Quaternion.LookRotation(direction), true, false, null);
            Call("ProcessUiPointer", UnityEngine.XR.XRNode.LeftHand, origin, Quaternion.LookRotation(direction), false, true, null);
            Require(clicks == 3, "Controller routing still delivers exactly one cursor click to the button");
            var dragProbe = buttonImage.gameObject.AddComponent<PCVRCursorEventProbe>();
            var dropProbe = background.gameObject.AddComponent<PCVRCursorEventProbe>();
            pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
            pointer.Process(events, default, hit.screenPosition + Vector2.right * 80, false, false, Vector2.zero, false);
            RaycastResult dropHit = Hit(Vector3.zero, Vector3.forward);
            pointer.Process(events, dropHit, hit.screenPosition + Vector2.right * 80, false, true, Vector2.zero, false);
            Require(dragProbe.Begins == 1 && dragProbe.Drags == 1 && dragProbe.Ends == 1 && dropProbe.Drops == 1 && clicks == 3,
                "UI drag stays captured outside its source and releases with one drop, one end-drag, and no click");
            UnityEngine.Object.DestroyImmediate(dragProbe);
            UnityEngine.Object.DestroyImmediate(dropProbe);
            CheckPuzzleRayDrag(root.transform);
            buttonImage.transform.localPosition = new Vector3(0.5f, 0, -1);
			CanvasGroup blocked = buttonImage.gameObject.AddComponent<CanvasGroup>();
			blocked.blocksRaycasts = false;
			Require(Hit(origin, direction).gameObject == background.gameObject, "Authored CanvasGroup blocking remains respected");
			blocked.blocksRaycasts = true;
			Canvas nested = buttonImage.gameObject.AddComponent<Canvas>();
			nested.overrideSorting = true;
			nested.sortingOrder = 20;
			buttonImage.gameObject.AddComponent<GraphicRaycaster>();
			Set("cachedCanvases", new[] { canvas, nested });
			Canvas.ForceUpdateCanvases();
			camera.Render();
			Require(Hit(origin, direction).sortingOrder == 20, "Nested button retains its own canvas sorting order");
			// A wide, aspect-preserving icon must not steal clicks from its neighbour.
			Image wideIcon = MakeImage("Island Live", panel.transform, new Vector3(0, 0, -1), new Vector2(4, 0.15f));
			Texture2D iconTexture = new Texture2D(16, 16);
			Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f);
			wideIcon.sprite = iconSprite;
			wideIcon.preserveAspect = true;
			wideIcon.gameObject.AddComponent<Button>();
			Canvas iconCanvas = wideIcon.gameObject.AddComponent<Canvas>();
			iconCanvas.overrideSorting = true;
			iconCanvas.sortingOrder = 30;
			wideIcon.gameObject.AddComponent<GraphicRaycaster>();
			Set("cachedCanvases", new[] { canvas, nested, iconCanvas });
			Canvas.ForceUpdateCanvases();
			camera.Render();
			Require(Hit(origin, direction).gameObject == button.gameObject, "Island Live icon margins do not capture another button's click");
			UnityEngine.Object.DestroyImmediate(wideIcon.gameObject);
			UnityEngine.Object.DestroyImmediate(iconSprite);
			UnityEngine.Object.DestroyImmediate(iconTexture);
			Set("cachedCanvases", new[] { canvas, nested });
			GameObject navigation = new GameObject("ScreenContainerContent", typeof(RectTransform));
			navigation.transform.SetParent(panel.transform, false);
			// Exercise the actual screen-to-world conversion and visibility groups.
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			Call("AnchorScreenSpaceUiInWorld");
			CanvasGroup visibility = panel.GetComponent<CanvasGroup>();
			Require(visibility != null && canvas.renderMode == RenderMode.WorldSpace, "Screen HUD is registered as a VR canvas");
			LineRenderer leftLaser = new GameObject("Left laser", typeof(LineRenderer)).GetComponent<LineRenderer>();
			LineRenderer rightLaser = new GameObject("Right laser", typeof(LineRenderer)).GetComponent<LineRenderer>();
			leftLaser.transform.SetParent(root.transform);
			rightLaser.transform.SetParent(root.transform);
			Set("leftAimRay", leftLaser);
			Set("rightAimRay", rightLaser);
			Call("UpdateTransientUiVisibility", true);
			Call("ProcessVrUiGrips", true, false);
			Call("UpdateTransientUiVisibility", true);
			Require(!(bool)Get("vrUiManuallyVisible"), "Grip hides an already visible popup and it stays hidden");
			CanvasGroup menuVisibility = navigation.GetComponent<CanvasGroup>();
			Require(visibility.alpha == 1f && menuVisibility.alpha == 0f && !menuVisibility.blocksRaycasts,
				"Grip hides navigation while the objective, dialogue and counter HUD remains visible");
			Require(!leftLaser.enabled && !rightLaser.enabled, "Grip hides both lasers in the same frame");
			Require(!(bool)Call("IsVrUiPointerActive"), "Hidden UI disables pointers even with active nested canvases");
			Call("ProcessVrUiGrips", true, false);
			Require(!(bool)Get("vrUiManuallyVisible"), "Holding grip does not repeat the toggle");
			Call("ProcessVrUiGrips", false, false);
			Call("ProcessVrUiGrips", true, true);
			Require((bool)Get("vrUiManuallyVisible"), "Both grips in one frame toggle only once");
			Require(visibility.alpha == 1f && visibility.blocksRaycasts && visibility.interactable,
				"Grip restores rendered HUD and its interaction");
			Call("ProcessVrUiGrips", false, false);
			Call("ProcessVrUiGrips", false, true);
			Require((bool)Get("vrUiManuallyVisible"), "Right grip is reserved for snowballs and does not hide UI");
            Call("ProcessVrUiGrips", false, false);
            Call("ProcessVrUiGrips", true, false);
			Call("UpdateTransientUiVisibility", false);
			Call("UpdateTransientUiVisibility", true);
			Require(!(bool)Get("vrUiManuallyVisible") && (bool)Get("transientUiActiveCached"), "Important UI does not change manual visibility");
            Call("UpdateTransientUiVisibility", false);
            Require(!(bool)Call("IsVrUiPointerActive"), "Dismissing automatic UI hides its pointers");
            GameObject phone = new GameObject("CellPhoneButton", typeof(RectTransform), typeof(Image), typeof(Button));
            phone.transform.SetParent(panel.transform, false);
            GameObject chat = new GameObject("ChatMenu_Standalone", typeof(RectTransform));
            chat.transform.SetParent(panel.transform, false);
            Set("nextMenuVisibilityScan", 0f);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            Require(phone.GetComponent<CanvasGroup>().alpha == 0f && chat.GetComponent<CanvasGroup>().alpha == 0f,
                "Phone and chat are hidden with the menu closed");
            Set("vrUiManuallyVisible", true);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            Require(phone.GetComponent<CanvasGroup>().alpha == 0f && !phone.GetComponent<CanvasGroup>().blocksRaycasts &&
                chat.GetComponent<CanvasGroup>().alpha == 1f && !phone.GetComponent<Image>().raycastTarget, "Phone never steals menu clicks while chat follows grip visibility");
            int phoneClicks = 0;
            phone.GetComponent<Button>().onClick.AddListener(() => phoneClicks++);
            Call("OpenVrPhone");
            Require(phoneClicks == 1, "Controller phone command invokes the hidden launcher");
            Set("vrUiManuallyVisible", false);
            Set("automaticUiSuppressed", true);
            Set("tutorialUiActiveCached", true);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            Require(menuVisibility.alpha == 1f && menuVisibility.blocksRaycasts && chat.GetComponent<CanvasGroup>().alpha == 1f,
                "Tutorial restores hidden navigation and action controls despite prior popup suppression");
            Require((bool)Call("IsVrUiPointerActive"), "Tutorial restores controller pointers automatically");
            Call("ProcessVrUiGrips", false, false);
            Call("ProcessVrUiGrips", true, false);
            Require(!(bool)Get("vrUiManuallyVisible") && menuVisibility.alpha == 1f,
                "Grip cannot hide tutorial controls or change the saved manual preference");
            Set("tutorialUiActiveCached", false);
            Set("transientUiActiveCached", false);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            Require(menuVisibility.alpha == 0f && !(bool)Call("IsVrUiPointerActive"),
                "Finishing tutorial returns controls to their previous hidden state");
            string testSceneName = testScene.name;
            foreach (string menuSceneName in new[] { "Home", "Boot" })
            {
                testScene.name = menuSceneName;
                Call("ProcessVrUiGrips", false, false);
                Call("ProcessVrUiGrips", true, false);
                Call("AnchorScreenSpaceUiInWorld");
                Require(visibility.alpha == 1f && menuVisibility.alpha == 1f && menuVisibility.blocksRaycasts,
                    menuSceneName + " menu remains visible and interactive after grip");
                Require((bool)Call("IsVrUiPointerActive"), menuSceneName + " menu keeps controller pointers available");
            }
            testScene.name = testSceneName;
            Call("AnchorScreenSpaceUiInWorld");
            Set("vrUiManuallyVisible", true);
            GameObject tutorialRoot = new GameObject("TutorialCanvas", typeof(RectTransform));
            tutorialRoot.transform.SetParent(root.transform, false);
            GameObject overlay = new GameObject("TargetedOverlay", typeof(RectTransform));
            overlay.transform.SetParent(tutorialRoot.transform, false);
            Image siblingBlocker = MakeImage("AndroidContainer", tutorialRoot.transform, Vector3.zero, Vector2.one);
            Image highlight = MakeImage("Highlight", overlay.transform, Vector3.zero, Vector2.one);
            MethodInfo filter = typeof(PCVRFirstPersonController).GetMethod("IsTutorialDecoration", BindingFlags.NonPublic | BindingFlags.Static);
            Require((bool)filter.Invoke(null, new object[] { siblingBlocker, overlay.transform, tutorialRoot.transform, button.transform }),
                "Targeted tutorial's full-screen sibling cannot intercept Create");
            Require((bool)filter.Invoke(null, new object[] { highlight, overlay.transform, tutorialRoot.transform, button.transform }),
                "Tutorial highlight passes through to its target");
            Require(!(bool)filter.Invoke(null, new object[] { buttonImage, overlay.transform, tutorialRoot.transform, button.transform }),
                "Highlighted Create target remains hittable");
            Require(!(bool)filter.Invoke(null, new object[] { background, overlay.transform, tutorialRoot.transform, button.transform }),
                "Normal modal blockers outside the tutorial are preserved");
			CheckTutorialCalloutLayout(root.transform);
            GameObject shopRoot = new GameObject("Rotated store", typeof(RectTransform), typeof(Canvas));
            shopRoot.transform.SetParent(root.transform, false);
            shopRoot.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            RectTransform shopRect = (RectTransform)shopRoot.transform;
            shopRect.sizeDelta = new Vector2(500, 400);
            shopRect.position = new Vector3(5, 2, 8);
            shopRect.localScale = Vector3.one * 0.002f;
            Image shopItem = MakeImage("Store item", shopRect, new Vector3(200, -150, 0), new Vector2(60, 60));
            Image buyPanel = MakeImage("Buy confirmation", shopRect, Vector3.zero, new Vector2(240, 100));
            var placement = buyPanel.gameObject.AddComponent<ClubPenguin.UI.StoreItemConfirmationPlacement>();
            foreach (float yaw in new[] { 0f, 90f, 180f })
            {
                shopRect.rotation = Quaternion.Euler(0, yaw, 0);
                placement.PositionConfirmation(shopItem.rectTransform, shopRect);
                Vector3[] buyCorners = new Vector3[4];
                buyPanel.rectTransform.GetWorldCorners(buyCorners);
                foreach (Vector3 corner in buyCorners)
                {
                    Vector3 local = shopRect.InverseTransformPoint(corner);
                    Require(Mathf.Abs(local.x) <= 250.01f && Mathf.Abs(local.y) <= 200.01f,
                        "Purchase confirmation stays inside world-space store at yaw " + yaw);
                }
            }
            camera.transform.rotation = Quaternion.Euler(35f, 70f, 20f);
            Vector3 levelForward = (Vector3)Call("GetHorizontalViewForward", camera.transform);
            Require(Mathf.Abs(levelForward.y) < 0.0001f && Vector3.Dot(levelForward, Quaternion.Euler(0,70,0) * Vector3.forward) > 0.999f,
                "Panel spawn heading ignores headset pitch and roll");
            GameObject storefront = new GameObject("Storefront anchor");
            storefront.transform.SetParent(root.transform);
            storefront.transform.rotation = Quaternion.Euler(0, 90, 0);
            GameObject visitor = new GameObject("Visitor");
            visitor.transform.SetParent(root.transform);
            visitor.transform.position = new Vector3(3, 1.4f, 0);
            PCVRFirstPersonController.SetShopAnchor(storefront.transform, visitor.transform);
            Type anchorType = typeof(PCVRFirstPersonController).GetNestedType("WorldCanvasAnchor", BindingFlags.NonPublic);
            object shopAnchor = Activator.CreateInstance(anchorType);
            anchorType.GetField("DesignSize").SetValue(shopAnchor, new Vector2(1920,1080));
            object[] poseArgs = { shopAnchor };
            typeof(PCVRFirstPersonController).GetMethod("SetShowroomPose", Private).Invoke(controller, poseArgs);
            Vector3 firstPosition = (Vector3)anchorType.GetField("Position").GetValue(poseArgs[0]);
            Quaternion firstRotation = (Quaternion)anchorType.GetField("Rotation").GetValue(poseArgs[0]);
            camera.transform.SetPositionAndRotation(new Vector3(10,5,20), Quaternion.Euler(55,150,35));
            typeof(PCVRFirstPersonController).GetMethod("SetShowroomPose", Private).Invoke(controller, poseArgs);
            Require(Vector3.Distance(firstPosition, (Vector3)anchorType.GetField("Position").GetValue(poseArgs[0])) < 0.001f,
                "Store position remains fixed when the headset moves");
            Require(Quaternion.Angle(firstRotation, (Quaternion)anchorType.GetField("Rotation").GetValue(poseArgs[0])) < 0.001f &&
                Vector3.Dot(firstRotation * Vector3.up, Vector3.up) > 0.999f, "Store stays upright and aligned with its authored storefront");
            Require(Vector3.Dot(firstPosition - visitor.transform.position, -storefront.transform.forward) > 0f,
                "Store panel is in front of the visitor toward the shop");
            GameObject closetPenguin = new GameObject("InventoryPenguinPreview");
            closetPenguin.transform.SetParent(root.transform);
            closetPenguin.transform.position = new Vector3(2, 1, 4);
            Set("menuTrackingOriginPosition", new Vector3(2, 2, 1));
            Set("menuTrackingOriginRotation", Quaternion.identity);
            object[] closetArgs = { Activator.CreateInstance(anchorType) };
            anchorType.GetField("DesignSize").SetValue(closetArgs[0], new Vector2(1920,1080));
            typeof(PCVRFirstPersonController).GetMethod("SetClosetPose", Private).Invoke(controller, closetArgs);
            Vector3 closetPosition = (Vector3)anchorType.GetField("Position").GetValue(closetArgs[0]);
            Vector3 closetScale = (Vector3)anchorType.GetField("Scale").GetValue(closetArgs[0]);
            Quaternion closetRotation = (Quaternion)anchorType.GetField("Rotation").GetValue(closetArgs[0]);
            Vector3 closetUp = closetRotation * Vector3.up;
            Vector3 lowerEdge = closetPosition - closetUp * (1080f * closetScale.y * 0.5f);
            Vector3 upperEdge = closetPosition + closetUp * (1080f * closetScale.y * 0.5f);
            Require(Mathf.Abs(closetPosition.x - 2f) < 0.001f && Mathf.Abs(upperEdge.z - 3.88f) < 0.001f,
                "Tilted closet sits below the preview, extending toward the viewer from its feet");
            Require(Mathf.Abs(lowerEdge.y - 1.1f) < 0.001f && upperEdge.y > lowerEdge.y,
                "Lowest closet edge clears the penguin's floor by ten centimeters");
            Require(Mathf.Abs(1920f * closetScale.x - 1.25f * 0.85f) < 0.001f &&
                !(bool)anchorType.GetField("FollowView").GetValue(closetArgs[0]),
                "Closet is fifteen percent smaller and does not follow head motion");
            GameObject startupNav = new GameObject("Cold-start navigation", typeof(RectTransform));
            startupNav.transform.SetParent(panel.transform, false);
            startupNav.SetActive(false); // Avoid service-dependent Awake; exercise lazy state initialization.
            var startupHandler = startupNav.AddComponent<ClubPenguin.UI.MainNavStateHandler>();
            GameObject startupButtons = new GameObject("Startup buttons", typeof(RectTransform));
            startupButtons.transform.SetParent(startupNav.transform, false);
            startupHandler.ButtonParent = startupButtons;
            Image startupBackground = MakeImage("BGImage", startupNav.transform, Vector3.zero, new Vector2(500, 100));
            Image startupSnow = MakeImage("SnowLeftImage", startupBackground.transform, Vector3.zero, new Vector2(80, 20));
            Set("vrUiManuallyVisible", false);
            Set("nextMenuVisibilityScan", 0f);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            CanvasGroup startupVisibility = startupButtons.GetComponent<CanvasGroup>();
            Require(startupVisibility != null && startupNav.GetComponent<CanvasGroup>() == null,
                "Cold-start navigation keeps authored and VR visibility separate");
            CanvasGroup backgroundVisibility = startupBackground.GetComponent<CanvasGroup>();
            Require(backgroundVisibility != null && backgroundVisibility.alpha == 0f && !backgroundVisibility.blocksRaycasts &&
                startupSnow.GetComponentInParent<CanvasGroup>(true) == backgroundVisibility,
                "Grip hides the navigation background and its snow decorations along with the buttons");
            MethodInfo authoredVisibility = typeof(ClubPenguin.UI.MainNavStateHandler).GetMethod("setVisible", Private);
            authoredVisibility.Invoke(startupHandler, new object[] { false });
            authoredVisibility.Invoke(startupHandler, new object[] { true });
            Require(startupNav.GetComponent<CanvasGroup>().alpha == 1f && startupVisibility.alpha == 0f && !startupVisibility.blocksRaycasts,
                "First-load navigation initialization cannot undo grip-hide");
            Require(backgroundVisibility.alpha == 0f, "Authored navigation visibility cannot reveal the hidden blue bar");
            Set("vrUiManuallyVisible", true);
            Call("UpdateMenuVisibility", new object[] { new[] { canvas } });
            Require(startupVisibility.alpha == 1f && startupVisibility.blocksRaycasts,
                "Navigation can be shown again without changing rooms");
            Require(backgroundVisibility.alpha == 1f && backgroundVisibility.blocksRaycasts,
                "Showing navigation restores its background");
            foreach (string prefabName in new[] { "MainNavBar", "MainNavBar_PC" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/UI/WorldSceneUI/Prefabs/" + prefabName + ".prefab");
                Transform authoredBackground = prefab.transform.Find("BGImage");
                Require(authoredBackground != null && (bool)typeof(PCVRFirstPersonController)
                    .GetMethod("IsMainNavigationBranch", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { authoredBackground }),
                    prefabName + " snowy background is included in grip visibility");
            }
            CheckRoomCanvasSizing(root.transform);
			Debug.Log($"PCVR interaction checks PASSED ({checks}).");
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(root);
			if (previousScene.IsValid() && previousScene.isLoaded)
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(testScene, true);
            }
		}
	}

    private static void CheckRoomCanvasSizing(Transform parent)
    {
        float expectedScale = Mathf.Min(1.1f / 1080f, 2f / 1920f);
        Type anchorType = typeof(PCVRFirstPersonController).GetNestedType("WorldCanvasAnchor", BindingFlags.NonPublic);
        var anchors = (System.Collections.IDictionary)Get("worldAnchoredMenuCanvases");
        foreach (float screenScale in new[] { 0.4f, 1f, 2.5f })
        {
            GameObject roomUi = new GameObject("TopCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            roomUi.transform.SetParent(parent, false);
            Canvas canvas = roomUi.GetComponent<Canvas>();
            CanvasScaler scaler = roomUi.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = screenScale;
            Image control = MakeImage("Room button", roomUi.transform, Vector3.zero, new Vector2(100, 100));
            Canvas.ForceUpdateCanvases();
            Set("cachedCanvases", new[] { canvas });
            Set("nextCanvasScanTime", float.MaxValue);
            Set("lastUiVisibilityScene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            roomUi.SetActive(false);
            Call("AnchorScreenSpaceUiInWorld");
            Require(!anchors.Contains(canvas.GetInstanceID()), "Inactive room HUD waits for platform settings before conversion");
            roomUi.SetActive(true);
            Call("AnchorScreenSpaceUiInWorld");
            Require(Mathf.Abs(roomUi.transform.lossyScale.x - expectedScale) < 0.000001f,
                "New room HUD uses stable authored scale despite screen scale " + screenScale);
            Require(Vector2.Distance(((RectTransform)roomUi.transform).sizeDelta, new Vector2(1920 * 0.74f, 1080 * 0.76f)) < 0.01f,
                "New room keeps the intended inset HUD layout");
            object anchor = anchors[canvas.GetInstanceID()];
            object[] scaleArgs = { anchor, true };
            MethodInfo presentationScale = typeof(PCVRFirstPersonController).GetMethod("UpdateCanvasPresentationScale", BindingFlags.Static | BindingFlags.NonPublic);
            presentationScale.Invoke(null, scaleArgs);
            Require(((Vector3)anchorType.GetField("Scale").GetValue(scaleArgs[0])).x < expectedScale,
                "Shop presentation applies its smaller panel size");
            anchors[canvas.GetInstanceID()] = scaleArgs[0];
            // The next room/presentation update must restore the normal scale,
            // including when a persistent HUD has acquired a scaled parent.
            parent.localScale = Vector3.one * 3f;
            Call("AnchorScreenSpaceUiInWorld");
            Call("AnchorScreenSpaceUiInWorld");
            Require(Mathf.Abs(control.rectTransform.rect.width * control.transform.lossyScale.x - 100 * expectedScale) < 0.00001f,
                "Leaving a shop restores button size without cumulative shrinkage or inherited parent scale");
            parent.localScale = Vector3.one;
            UnityEngine.Object.DestroyImmediate(roomUi);
        }
    }

    private static void CheckPuzzleRayDrag(Transform parent)
    {
        var puzzleObject = new GameObject("Rotated map", typeof(RectTransform));
        puzzleObject.transform.SetParent(parent, false);
        puzzleObject.transform.position = new Vector3(5, 2, 4);
        puzzleObject.transform.rotation = Quaternion.Euler(15, 40, 0);
        puzzleObject.transform.localScale = Vector3.one * 0.02f;
        var puzzle = puzzleObject.AddComponent<ClubPenguin.MiniGames.Jigsaw.JigsawController>();
        var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
        board.transform.SetParent(puzzle.transform, false);
        board.transform.localPosition = new Vector3(0, 0, 1);
        board.transform.localScale = new Vector3(4, 4, 1);
        board.layer = LayerMask.NameToLayer("UI");
        var pieceObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        pieceObject.name = "Map piece";
        pieceObject.transform.SetParent(puzzle.transform, false);
        pieceObject.layer = LayerMask.NameToLayer("UI");
        var piece = pieceObject.AddComponent<ClubPenguin.MiniGames.Jigsaw.JigsawPiece>();
        var start = new GameObject("Start").transform;
        start.SetParent(puzzle.transform, false);
        var solve = new GameObject("Solve").transform;
        solve.SetParent(puzzle.transform, false);
        var divider = new GameObject("Divider");
        divider.transform.SetParent(puzzle.transform, false);
        divider.transform.localPosition = Vector3.down;
        Action<object, string, object> field = (obj, name, value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
        field(piece, "rend", pieceObject.GetComponent<Renderer>());
        field(puzzle, "bkgArtworkObj", board);
        field(puzzle, "bkgArtworkColl", board.GetComponent<Collider>());
        field(puzzle, "areaDividerObj", divider);
        field(puzzle, "startContainer", start);
        field(puzzle, "solveContainer", solve);
        field(puzzle, "hasInitalizedPuzzle", true);
        field(puzzle, "xMin", -2f);
        field(puzzle, "xMax", 2f);
        field(puzzle, "yMin", -2f);
        field(puzzle, "yMax", 2f);
        puzzle.IsMeshPuzzle = true;
        puzzle.SolvePositions.Add(pieceObject.name, new Vector3(10, 10, 0));
        ((System.Collections.Generic.List<string>)puzzle.GetType().GetField("layers", Private).GetValue(puzzle)).Add(pieceObject.name);
        ((System.Collections.Generic.Dictionary<string, GameObject>)puzzle.GetType().GetField("layerObjects", Private).GetValue(puzzle)).Add(pieceObject.name, pieceObject);
        Func<float, Ray> ray = x => new Ray(puzzle.transform.TransformPoint(new Vector3(x, 0, -3)), puzzle.transform.forward);
        Physics.SyncTransforms();
        Require(puzzle.ProcessVrPointer(-101, ray(0), true, true, false, false, out _), "Map consumes the laser press on its mesh board");
        Require(!puzzle.ProcessVrPointer(-102, ray(0), true, true, false, false, out _), "Second hand cannot steal a held puzzle piece");
        puzzle.ProcessVrPointer(-101, ray(0.6f), false, true, false, true, out _);
        Require(Mathf.Abs(puzzle.transform.InverseTransformPoint(piece.transform.position).x - 0.6f) < 0.001f,
            "Captured map piece follows the hand ray even over another UI control on a tilted scaled panel");
        puzzle.ProcessVrPointer(-101, ray(0.6f), false, false, true, true, out _);
        Require(puzzle.GetType().GetField("vrDragOwner", Private).GetValue(puzzle) == null, "Trigger release frees puzzle drag capture");
        Require(!puzzle.ProcessVrPointer(-101, ray(0), true, true, false, true, out _), "Map leaves the close button to the UI pointer");
        Vector3 localBeforeLock = puzzle.transform.InverseTransformPoint(piece.transform.position);
        piece.Lock(new Vector3(-0.4f, 0.7f, 0));
        Vector3 locked = puzzle.transform.InverseTransformPoint(piece.transform.position);
        Require(Vector2.Distance(locked, new Vector2(-0.4f, 0.7f)) < 0.001f && Mathf.Abs(locked.z - localBeforeLock.z) < 0.001f,
            "Map snap preserves panel depth and uses panel coordinates instead of world XY");
        UnityEngine.Object.DestroyImmediate(puzzleObject);
    }

	private static void CheckTutorialCalloutLayout(Transform root)
	{
		GameObject panel = new GameObject("Tutorial layout", typeof(RectTransform), typeof(Canvas));
		panel.transform.SetParent(root, false);
		panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
		RectTransform canvasRect = (RectTransform)panel.transform;
		canvasRect.sizeDelta = new Vector2(1920, 1080);
		canvasRect.position = new Vector3(7, 2, 4);
		canvasRect.rotation = Quaternion.Euler(0, 125, 0);
		canvasRect.localScale = Vector3.one * 0.001f;
		var type = typeof(ClubPenguin.TutorialUI.TutorialOverlay);
		var positionMethod = type.GetMethod("GetWorldCanvasHighlightPosition", BindingFlags.NonPublic | BindingFlags.Static);
		Vector2 position = (Vector2)positionMethod.Invoke(null, new object[] { canvasRect, null, new Vector2(0.5f, 0.65f) });
		Require(Vector2.Distance(position, new Vector2(960, 702)) < 0.01f,
			"Marketplace instructions use canvas design units independent of headset pixels");
		Image target = MakeImage("Target", canvasRect, new Vector3(300, -100, 0), new Vector2(100, 80));
		position = (Vector2)positionMethod.Invoke(null, new object[] { canvasRect, target.gameObject, new Vector2(10, 20) });
		Require(Vector2.Distance(position, new Vector2(1270, 460)) < 0.01f,
			"Guided tutorial maps its target into a rotated scaled VR canvas");
		RectTransform textBox = MakeImage("Callout", canvasRect, new Vector3(1100, 800, 0), new Vector2(350, 180)).rectTransform;
		textBox.pivot = new Vector2(0.5f, 0);
		type.GetMethod("ClampTextBoxToCanvas", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { textBox, canvasRect });
		Vector3[] corners = new Vector3[4];
		textBox.GetWorldCorners(corners);
		foreach (Vector3 corner in corners)
		{
			Vector3 local = canvasRect.InverseTransformPoint(corner);
			Require(local.x >= -936.01f && local.x <= 936.01f && local.y >= -516.01f && local.y <= 516.01f,
				"Tutorial text remains inside the visible VR panel");
		}
		UnityEngine.Object.DestroyImmediate(panel);
	}

	private static Image MakeImage(string name, Transform parent, Vector3 position, Vector2 size)
	{
		GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
		go.transform.SetParent(parent, false);
		go.transform.localPosition = position;
		((RectTransform)go.transform).sizeDelta = size;
		return go.GetComponent<Image>();
	}

	private static RaycastResult Hit(Vector3 origin, Vector3 direction)
	{
		object[] args = { origin, direction, null, null, null, null };
		Require((bool)typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, args), "Ray hits UI");
		return (RaycastResult)args[5];
	}

	private static object Call(string method, params object[] args) => typeof(PCVRFirstPersonController).GetMethod(method, Private).Invoke(controller, args);
	private static void Set(string field, object value) => typeof(PCVRFirstPersonController).GetField(field, Private).SetValue(controller, value);
	private static object Get(string field) => typeof(PCVRFirstPersonController).GetField(field, Private).GetValue(controller);
	private static void Require(bool condition, string message)
	{
		if (!condition) throw new Exception("PCVR check failed: " + message);
		checks++;
		Debug.Log("PCVR check passed: " + message);
	}
}

public sealed class PCVRCursorEventProbe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public int Begins, Drags, Ends, Drops;
    public void OnBeginDrag(PointerEventData data) { Begins++; }
    public void OnDrag(PointerEventData data) { Drags++; }
    public void OnEndDrag(PointerEventData data) { Ends++; }
    public void OnDrop(PointerEventData data) { Drops++; }
}
