using System;
using System.IO;
using System.Reflection;
using System.Linq;
using ClubPenguin;
using ClubPenguin.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class PCVRClosetClickChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Request = "Library/PCVR_CLOSET_CHECK_REQUEST.txt";
    private const string Result = "Library/PCVR_CLOSET_CHECK_RESULT.txt";

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || !File.Exists(Request)) return;
            File.Delete(Request);
            try { Run(); File.WriteAllText(Result, "PASSED"); }
            catch (Exception e) { File.WriteAllText(Result, e.ToString()); Debug.LogException(e); }
        };
    }

    [MenuItem("CPI VR/Validate closet Create click")]
    public static void Run()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Closet click checks");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var host = new GameObject("Inactive controller");
            host.transform.SetParent(root.transform);
            host.SetActive(false);
            var controller = host.AddComponent<PCVRFirstPersonController>();
            var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.pixelRect = new Rect(0, 0, 800, 600);
            var events = new GameObject("Events", typeof(EventSystem)).GetComponent<EventSystem>();
            events.transform.SetParent(root.transform);
            var canvas = new GameObject("Closet", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.transform.SetParent(root.transform);
            canvas.renderMode = RenderMode.WorldSpace;
            // Production popup roots use a nonzero order. Their ordinary nested
            // canvases inherit it; they must not sort underneath the root background.
            canvas.sortingOrder = 50;
            canvas.worldCamera = camera;
            canvas.transform.position = new Vector3(0, 0, 3);
            canvas.transform.localScale = Vector3.one * 0.003f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
            var background = new GameObject("Popup background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(canvas.transform, false);
            ((RectTransform)background.transform).sizeDelta = new Vector2(1920, 1080);
            var screenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/GUI/Resources/Prefabs/ClothingDesigner/Screens/InventoryEquipmentScreen.prefab");
            var screen = CloneUi(screenPrefab.transform, canvas.transform);
            var content = screen.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "ScrollContent");
            var slot = new GameObject("First inventory slot", typeof(RectTransform)).GetComponent<RectTransform>();
            slot.SetParent(content, false);
            slot.sizeDelta = new Vector2(128, 128);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/GUI/Resources/Prefabs/EquipmentIconPrefab.prefab");
            var icon = CloneUi(prefab.transform, slot);
            icon.Find("ItemContent").gameObject.SetActive(false);
            icon.Find("CustomizerButton").gameObject.SetActive(true);
            icon.Find("CatalogButtonContainer").gameObject.SetActive(false);
            var create = icon.Find("CustomizerButton/CreateButton").GetComponent<Button>();
            int clicks = 0;
            create.GetComponent<ButtonClickListener>().OnClick.AddListener(_ => clicks++);
            // The live closet keeps PatternToggle enabled and hides it with scale
            // zero. Its child cover must not become a full-screen ray blocker.
            var hiddenWidget = new GameObject("Collapsed PatternToggle", typeof(RectTransform));
            hiddenWidget.transform.SetParent(canvas.transform, false);
            hiddenWidget.transform.localScale = Vector3.zero;
            var hiddenCover = new GameObject("Hidden widget cover", typeof(RectTransform), typeof(Image));
            hiddenCover.transform.SetParent(hiddenWidget.transform, false);
            ((RectTransform)hiddenCover.transform).sizeDelta = new Vector2(154, 128);
            var tutorialCanvas = new GameObject("TutorialOverlayCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            tutorialCanvas.transform.SetParent(canvas.transform, false);
            ((RectTransform)tutorialCanvas.transform).sizeDelta = new Vector2(1920, 1080);
            tutorialCanvas.overrideSorting = true;
            tutorialCanvas.sortingOrder = 100;
            var blocker = new GameObject("Tutorial full-screen sibling", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(tutorialCanvas.transform, false);
            ((RectTransform)blocker.transform).sizeDelta = new Vector2(1920, 1080);
            var overlay = new GameObject("TutorialOverlay", typeof(RectTransform)).AddComponent<PCVRClosetTestOverlay>();
            overlay.transform.SetParent(tutorialCanvas.transform, false);
            typeof(ClubPenguin.TutorialUI.TutorialOverlay).GetField("data", Private).SetValue(overlay,
                new ClubPenguin.TutorialUI.DTutorialOverlay { Target = create.gameObject, EnableTarget = true, DisableUI = false });
            typeof(ClubPenguin.TutorialUI.TutorialOverlay).GetField("isShowing", Private).SetValue(overlay, true);
            typeof(PCVRFirstPersonController).GetField("playerCamera", Private).SetValue(controller, camera);
            foreach (Canvas childCanvas in canvas.GetComponentsInChildren<Canvas>(true))
            {
                childCanvas.worldCamera = camera;
                if (childCanvas.GetComponent<GraphicRaycaster>() == null) childCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }
            typeof(PCVRFirstPersonController).GetField("cachedCanvases", Private).SetValue(controller, canvas.GetComponentsInChildren<Canvas>(true));
            typeof(PCVRFirstPersonController).GetField("nextCanvasScanTime", Private).SetValue(controller, float.MaxValue);
            typeof(PCVRFirstPersonController).GetField("vrUiManuallyVisible", Private).SetValue(controller, true);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var pointer = new PCVRPointer(-101);
            // A popup loaded after root conversion can still reference the original
            // GUI camera. The visible world-space screen must use the VR event camera.
            var obsoleteCamera = new GameObject("Original GUI camera", typeof(Camera)).GetComponent<Camera>();
            obsoleteCamera.transform.SetParent(root.transform);
            obsoleteCamera.transform.rotation = Quaternion.Euler(0, 180, 0);
            obsoleteCamera.enabled = false;
            foreach (Canvas childCanvas in screen.GetComponentsInChildren<Canvas>(true))
                childCanvas.worldCamera = obsoleteCamera;
            if (!(bool)typeof(PCVRFirstPersonController).GetMethod("IsVrUiPointerActive", Private).Invoke(controller, null))
                throw new Exception("Visible closet must enable controller pointers");
            foreach (bool nestedTutorial in new[] { true, false })
            {
                if (!nestedTutorial)
                {
                    tutorialCanvas.transform.SetParent(root.transform, true);
                    tutorialCanvas.renderMode = RenderMode.WorldSpace;
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                }
                foreach (Vector2 sample in new[] { Vector2.zero, new Vector2(0, -30), new Vector2(30, 0), new Vector2(-30, 0) })
                {
                    Vector3 origin = new Vector3(0.3f, -0.1f, 0);
                    Vector3 point = create.transform.TransformPoint(sample);
                    object[] args = { origin, (point - origin).normalized, null, null, null, null };
                    bool found = (bool)typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, args);
                    var hit = (RaycastResult)args[5];
                    if (!found || ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject) != create.gameObject)
                        throw new Exception("Create hit failed at " + sample + ": " + (hit.gameObject != null ? hit.gameObject.name : "no hit"));
                    pointer.Process(events, hit, hit.screenPosition, true, false, Vector2.zero, false);
                    pointer.Process(events, hit, hit.screenPosition, false, true, Vector2.zero, false);
                }
            }
            if (clicks != 8) throw new Exception("Expected eight Create clicks; got " + clicks);
            tutorialCanvas.gameObject.SetActive(false);
            foreach (Vector3 collapsedScale in new[] { new Vector3(0, 1, 1), new Vector3(1, 0, 1) })
            {
                hiddenWidget.transform.localScale = collapsedScale;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                object[] collapsedArgs = { Vector3.zero, create.transform.position.normalized, null, null, null, null };
                typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, collapsedArgs);
                var collapsedHit = (RaycastResult)collapsedArgs[5];
                if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(collapsedHit.gameObject) != create.gameObject)
                    throw new Exception("Collapsed axis intercepted Create: " + collapsedScale);
            }
            // Restoring the same widget must restore normal ray blocking.
            ((RectTransform)hiddenCover.transform).sizeDelta = new Vector2(1920, 1080);
            hiddenWidget.transform.localScale = Vector3.one;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            object[] restoredArgs = { Vector3.zero, create.transform.position.normalized, null, null, null, null };
            typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, restoredArgs);
            if (((RaycastResult)restoredArgs[5]).gameObject != hiddenCover)
                throw new Exception("Visible widget cover must continue to block rays");
            hiddenWidget.transform.localScale = Vector3.zero;
            // A separate modal is still allowed to block Create.
            var modal = new GameObject("Unrelated modal", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            modal.transform.SetParent(canvas.transform, false);
            ((RectTransform)modal.transform).sizeDelta = new Vector2(1920, 1080);
            modal.GetComponent<Canvas>().overrideSorting = true;
            modal.GetComponent<Canvas>().sortingOrder = 200;
            modal.GetComponent<Canvas>().worldCamera = camera;
            typeof(PCVRFirstPersonController).GetField("cachedCanvases", Private).SetValue(controller, root.GetComponentsInChildren<Canvas>(true));
            Canvas.ForceUpdateCanvases();
            camera.Render();
            object[] modalArgs = { Vector3.zero, create.transform.position.normalized, null, null, null, null };
            typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, modalArgs);
            if (((RaycastResult)modalArgs[5]).gameObject != modal) throw new Exception("Unrelated modal must continue to block Create");
            modal.SetActive(false);
            tutorialCanvas.gameObject.SetActive(false);
            icon.Find("CustomizerButton").gameObject.SetActive(false);
            icon.Find("ItemContent").gameObject.SetActive(true);
            foreach (Transform child in icon.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Item") child.gameObject.SetActive(true);
                if (child.name == "LoadingSpinner" || child.name == "MemberLock") child.gameObject.SetActive(false);
            }
            int equipmentClicks = 0;
            icon.GetComponent<ButtonClickListener>().OnClick.AddListener(_ => equipmentClicks++);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            Vector3 equipmentPoint = icon.GetComponentInChildren<RawImage>().rectTransform.position;
            object[] equipmentArgs = { Vector3.zero, equipmentPoint.normalized, null, null, null, null };
            typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, equipmentArgs);
            var equipmentHit = (RaycastResult)equipmentArgs[5];
            if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(equipmentHit.gameObject) != icon.gameObject)
                throw new Exception("Equipment hit failed: " + equipmentHit.gameObject);
            pointer.Process(events, equipmentHit, equipmentHit.screenPosition, true, false, Vector2.zero, false);
            pointer.Process(events, equipmentHit, equipmentHit.screenPosition, false, true, Vector2.zero, false);
            if (equipmentClicks != 1) throw new Exception("Equipment ButtonClickListener did not receive its click");
            screen.gameObject.SetActive(false);
            var storePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/World/Resources/Prefabs/MarketplaceItemPrefab.prefab");
            var storeItem = CloneUi(storePrefab.transform, canvas.transform);
            // MarketplaceItems are sized by the runtime inventory grid; the asset
            // itself deliberately has a zero-size root.
            var storeRect = (RectTransform)storeItem;
            storeRect.anchorMin = storeRect.anchorMax = new Vector2(0.5f, 0.5f);
            storeRect.sizeDelta = new Vector2(256, 300);
            storeRect.anchoredPosition = Vector2.zero;
            var storeButton = storeItem.GetComponentInChildren<Button>();
            int storeClicks = 0;
            storeButton.GetComponent<ButtonClickListener>().OnClick.AddListener(_ => storeClicks++);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            // Popup managers may reset an already converted canvas to their GUI camera.
            canvas.worldCamera = obsoleteCamera;
            Vector3 storePoint = ((RectTransform)storeButton.transform).TransformPoint(((RectTransform)storeButton.transform).rect.center);
            object[] storeArgs = { Vector3.zero, storePoint.normalized, null, null, null, null };
            typeof(PCVRFirstPersonController).GetMethod("TryGetWorldMenuGraphicHit", Private).Invoke(controller, storeArgs);
            var storeHit = (RaycastResult)storeArgs[5];
            if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(storeHit.gameObject) != storeButton.gameObject)
                throw new Exception("Store item hit failed: " + storeHit.gameObject);
            pointer.Process(events, storeHit, storeHit.screenPosition, true, false, Vector2.zero, false);
            pointer.Process(events, storeHit, storeHit.screenPosition, false, true, Vector2.zero, false);
            if (storeClicks != 1) throw new Exception("Store item did not receive its click");
            Debug.Log("PCVR closet/store checks PASSED: collapsed widgets ignored; restored widgets and modals still block; stale GUI cameras repaired; eight Create clicks, one clothing-item click through ButtonClickListener, one store-item click.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // Retain the authored UI geometry, without starting unrelated game services.
    private static Transform CloneUi(Transform source, Transform parent)
    {
        var copy = source is RectTransform
            ? new GameObject(source.name, typeof(RectTransform)) : new GameObject(source.name);
        copy.SetActive(false);
        copy.transform.SetParent(parent, false);
        if (source is RectTransform sourceRect)
        {
            var copyRect = (RectTransform)copy.transform;
            copyRect.anchorMin = sourceRect.anchorMin;
            copyRect.anchorMax = sourceRect.anchorMax;
            copyRect.pivot = sourceRect.pivot;
            copyRect.sizeDelta = sourceRect.sizeDelta;
            copyRect.anchoredPosition3D = sourceRect.anchoredPosition3D;
        }
        else copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;
        foreach (Component component in source.GetComponents<Component>())
        {
            // Scrollbars retain serialized references to the source prefab's handle.
            // These checks only use buttons; avoid mutating authored scrollbar geometry.
            if (!(component is Graphic) && !(component is Button) && !(component is CanvasGroup) &&
                !(component is Canvas) && !(component is LayoutGroup) && !(component is ContentSizeFitter) && !(component is AspectRatioFitter) &&
                !(component is LayoutElement) && !(component is Mask) && !(component is RectMask2D) &&
                !(component is ButtonClickListener)) continue;
            if (component is Graphic && copy.GetComponent<CanvasRenderer>() == null) copy.AddComponent<CanvasRenderer>();
            Component clone = copy.AddComponent(component.GetType());
            EditorUtility.CopySerialized(component, clone);
            if (clone is ButtonClickListener)
                typeof(ButtonClickListener).GetField("onClick", Private).SetValue(clone, new ButtonClickListener.ButtonClickListenerEvent());
            if (clone is Selectable selectable) { selectable.transition = Selectable.Transition.None; selectable.targetGraphic = copy.GetComponent<Graphic>(); }
        }
        foreach (Transform child in source) CloneUi(child, copy.transform);
        copy.SetActive(source.gameObject.activeSelf);
        // Preview-scene MonoBehaviours do not receive play-mode lifecycle callbacks.
        // Initialize the actual game's button bridge, rather than replacing it with
        // a test listener directly on Button.onClick.
        var listener = copy.GetComponent<ButtonClickListener>();
        if (listener != null)
        {
            typeof(ButtonClickListener).GetMethod("Awake", Private).Invoke(listener, null);
            typeof(ButtonClickListener).GetMethod("OnEnable", Private).Invoke(listener, null);
        }
        return copy.transform;
    }
}

// Exercise the production overlay's target state without its game-service teardown.
internal sealed class PCVRClosetTestOverlay : ClubPenguin.TutorialUI.TutorialOverlay
{
    private void OnDestroy() { }
}
