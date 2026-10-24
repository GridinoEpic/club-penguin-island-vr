using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using ClubPenguin.ClothingDesigner;
using ClubPenguin.ClothingDesigner.ItemCustomizer;
using ClubPenguin.UI;
using ClubPenguin;
using CpRemixShaders;
using Disney.LaunchPadFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class PCVRCustomizerChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static PCVRCustomizerChecks()
    {
        EditorApplication.update += () =>
        {
            const string request = "Library/PCVR_CUSTOMIZER_CHECK_REQUEST.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            File.Delete(request);
            try { Run(); CheckSceneTransition(); File.WriteAllText("Library/PCVR_CUSTOMIZER_CHECK_RESULT.txt", "PASS: actual ClothingDesigner scene mannequin and camera transition, VR tracking-origin transition, swatch selection, animated clothing collider pose, scenery/UI filtering, pattern placement, slider enable/disable, pointer release."); }
            catch (Exception ex) { File.WriteAllText("Library/PCVR_CUSTOMIZER_CHECK_RESULT.txt", ex.ToString()); Debug.LogException(ex); }
        };
    }

    private static void Set(object instance, string field, object value) => instance.GetType().GetField(field, Private).SetValue(instance, value);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("CPI VR/Validate clothing customization interaction")]
    public static void Run()
    {
        var root = new GameObject("Customizer regression checks");
        root.SetActive(false);
        var texture = new Texture2D(2, 2) { name = "VR check pattern" };
        var mask = new Texture2D(2, 2);
        mask.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); mask.Apply();
        var material = new Material(Shader.Find("CpRemix/Equipment Preview"));
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
        var mesh = new Mesh();
        GameObject garment = null;
        bool sliderEnabled = false;
        var item = (DItemCustomization)FormatterServices.GetUninitializedObject(typeof(DItemCustomization));
        item.ResetChannels();
        EventHandlerDelegate<CustomizerWidgetEvents.SetIsSliderWidgetInteractable> onSlider = evt => { sliderEnabled = evt.Value; return false; };
        EventHandlerDelegate<CustomizerUIEvents.UpdateChannelFabric> onFabric = evt =>
        {
            var channel = evt.ChannelToUpdate == CustomizationChannel.RED ? item.RedChannel : evt.ChannelToUpdate == CustomizationChannel.GREEN ? item.GreenChannel : item.BlueChannel;
            item.SetChannelFabric(channel, evt.NewFabric, evt.UVOffset);
            if (evt.ChannelToUpdate == CustomizationChannel.RED) item.SelectChannel(channel);
            return false;
        };
        CustomizationContext.EventBus.AddListener(onSlider);
        CustomizationContext.EventBus.AddListener(onFabric);
        try
        {
            var camObject = new GameObject("Camera", typeof(Camera)); camObject.transform.SetParent(root.transform);
            var camera = camObject.GetComponent<Camera>(); camera.rect = new Rect(0, 0, 1, 1);
            var buttonObject = new GameObject("Swatch", typeof(RectTransform)); buttonObject.transform.SetParent(root.transform);
            var button = buttonObject.AddComponent<CustomizationButton>();
            var imageObject = new GameObject("Swatch image", typeof(RectTransform), typeof(Image)); imageObject.transform.SetParent(buttonObject.transform);
            imageObject.GetComponent<Image>().sprite = sprite;
            typeof(CustomizationButton).GetField("IconImage", Private).SetValue(button, imageObject.GetComponent<Image>());
            button.DraggableButtonType = DraggableButtonType.FABRIC;
            typeof(CustomizationButton).GetProperty("CanDrag").SetValue(button, true);
            // Classification needs no runtime services; skip its breadcrumb constructor.
            var selection = (NoSelectionState)FormatterServices.GetUninitializedObject(typeof(NoSelectionState));
            Set(selection, "mainCam", camera);
            var touch = new DragAreaState.ITouch { worldRay = new Ray(new Vector3(1000, 0, 0), Vector3.forward), uiTarget = imageObject, position = new Vector2(400, 300) };
            var classify = typeof(NoSelectionState).GetMethod("processGesture", Private);
            var gesture = (CustomizerGestureModel)classify.Invoke(selection, new object[] { touch, new CustomizerGestureModel() });
            Require(gesture.TouchDownStartArea == AreaTouchedEnum.FABRIC_BUTTON && gesture.DragIconTexture == texture, $"World-space swatch must resolve through its child graphic with full-screen camera. Area={gesture.TouchDownStartArea}, texture={gesture.DragIconTexture}, draggable={button.CanDrag}, parent={imageObject.GetComponentInParent<CustomizationButton>(true)}, ray={touch.worldRay.HasValue}.");

            garment = new GameObject("Clothing ray target", typeof(SkinnedMeshRenderer), typeof(MeshCollider));
            garment.transform.SetParent(root.transform); garment.transform.position = new Vector3(1000, 0, 2);
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(0, 1, 0), new Vector3(1, -1, 0) };
            mesh.triangles = new[] { 0, 1, 2 }; mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.right }; mesh.RecalculateBounds();
            var boneObject = new GameObject("Animated clothing bone"); boneObject.transform.SetParent(garment.transform, false);
            mesh.bindposes = new[] { Matrix4x4.identity };
            var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            mesh.boneWeights = new[] { weight, weight, weight };
            var skin = garment.GetComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh; skin.bones = new[] { boneObject.transform }; skin.rootBone = boneObject.transform;
            boneObject.transform.localPosition = Vector3.right * 2f;
            garment.GetComponent<MeshCollider>().sharedMesh = mesh;
            garment.GetComponent<SkinnedMeshRenderer>().sharedMaterial = material;
            material.SetTexture(EquipmentShaderParams.DECALS_123_OPACITY_TEX, mask);
            var child = garment.AddComponent<CustomizerChild>(); child.enabled = false;
            Set(child, "smr", skin); Set(child, "meshCollider", garment.GetComponent<MeshCollider>());
            touch.worldRay = new Ray(new Vector3(1002, 0, 0), Vector3.forward);
            // Enable only the collider target, leaving test behaviours inactive.
            garment.transform.SetParent(null); Physics.SyncTransforms();
            Require(!Physics.Raycast(touch.worldRay.Value, out _), "Original bind-pose collider must miss the animated clothing ray in this regression.");
            child.RefreshColliderPose(true); Physics.SyncTransforms();
            Require(DragAreaState.RaycastClothing(touch.worldRay.Value, out _), "Baked collider must match the animated clothing pose.");
            int bakes = child.ColliderPoseBakeCount;
            for (int i = 0; i < 120; i++) DragAreaState.RaycastClothing(touch.worldRay.Value, out _);
            Require(child.ColliderPoseBakeCount == bakes, "Repeated pointer queries must not cook a collider every frame.");
            Set(child, "nextColliderPoseCheck", -1f); child.RefreshColliderPose();
            Require(child.ColliderPoseBakeCount == bakes, "Unchanged bone pose must not rebuild collision mesh even after the rate limit expires.");
            boneObject.transform.localPosition += Vector3.right * 0.1f;
            Set(child, "nextColliderPoseCheck", -1f); child.RefreshColliderPose(); Physics.SyncTransforms();
            Require(child.ColliderPoseBakeCount == bakes + 1, "Changed clothing pose must refresh on the next permitted pointer query.");
            var obstacle = new GameObject("Non clothing obstacle", typeof(BoxCollider));
            obstacle.transform.SetParent(garment.transform, false); obstacle.transform.position = new Vector3(1002, 0, 1);
            Physics.SyncTransforms();
            Require(DragAreaState.RaycastClothing(touch.worldRay.Value, out RaycastHit clothingHit) && clothingHit.collider.gameObject == garment, "Non clothing collider must not intercept clothing drop.");
            touch.uiTarget = null;
            gesture = (CustomizerGestureModel)classify.Invoke(selection, new object[] { touch, new CustomizerGestureModel() });
            Require(gesture.TouchDownStartArea == AreaTouchedEnum.RED_CHANNEL, "Clothing selection must use controller ray rather than camera screen ray.");

            var iconObject = new GameObject("Drag icon", typeof(RectTransform)); iconObject.transform.SetParent(root.transform);
            var container = iconObject.AddComponent<DragContainer>();
            var iconImage = new GameObject("Carried graphic", typeof(RectTransform), typeof(Image)); iconImage.transform.SetParent(iconObject.transform);
            iconObject.AddComponent<CanvasGroup>();
            typeof(DragContainer).GetMethod("Awake", Private).Invoke(container, null);
            Require(!iconImage.GetComponent<Image>().raycastTarget && !iconObject.GetComponent<CanvasGroup>().blocksRaycasts, "Drag visual must disable graphic and group ray blocking.");
            Require(DragAreaController.GetVrCustomizationUiTarget(iconImage) == null, "Carried drag icon must not block its own drop ray.");
            var decoration = new GameObject("Decorative panel", typeof(RectTransform), typeof(Image)); decoration.transform.SetParent(root.transform);
            Require(DragAreaController.GetVrCustomizationUiTarget(decoration) == null, "Decorative canvas graphics must not block clothing drops.");
            decoration.AddComponent<Button>();
            Require(DragAreaController.GetVrCustomizationUiTarget(decoration) == decoration, "Actual UI controls must still block clothing drops.");
            Type dragType = typeof(NoSelectionState).Assembly.GetType("ClubPenguin.ClothingDesigner.ItemCustomizer.DragFabricButtonState");
            object drag = Activator.CreateInstance(dragType, new object[] { root, camera, camera, container });
            Set(drag, "dragTexture", texture);
            dragType.GetMethod("SetPersistentChannelData").Invoke(drag, new object[] { new OriginalFabricData() });
            dragType.GetMethod("onDrag", Private).Invoke(drag, new object[] { Vector2.zero, touch.worldRay, false });
            Require(item.RedChannel.Fabric == texture, "Controller-ray drag must apply pattern to red clothing channel even when screen point misses.");

            var properties = root.AddComponent<PropertyCustomizationController>();
            Set(properties, "itemModel", item); Set(properties, "currentState", CustomizerState.FABRIC);
            typeof(PropertyCustomizationController).GetMethod("updateToolSettings", Private).Invoke(properties, null);
            Require(sliderEnabled, "Applied pattern must enable scale slider.");
            Set(properties, "_isInputActive", true);
            typeof(PropertyCustomizationController).GetMethod("updateToolSettings", Private).Invoke(properties, null);
            Require(!sliderEnabled, "Scale slider must stay disabled during a drag.");

            var area = root.AddComponent<DragAreaController>();
            var capture = new CaptureState(); Set(area, "currentState", capture); Set(area, "isUpdateEnabled", true); Set(area, "vrPointerOwner", (int?)1);
            Require(area.ProcessVrPointer(2, touch.worldRay.Value, null, Vector2.zero, true, true, false) && capture.Count == 0, "Second controller must not interrupt captured drag.");
            area.ProcessVrPointer(1, touch.worldRay.Value, null, Vector2.one, false, false, true);
            Require(capture.Last.phase == TouchPhase.Ended && capture.Last.worldRay.HasValue && typeof(DragAreaController).GetField("vrPointerOwner", Private).GetValue(area) == null, "Trigger release must reach clothing drag state and release ownership.");
        }
        finally
        {
            CustomizationContext.EventBus.RemoveListener(onSlider); CustomizationContext.EventBus.RemoveListener(onFabric);
            UnityEngine.Object.DestroyImmediate(garment);
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(mask); UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private sealed class CaptureState : DragAreaState
    {
        public int Count;
        public ITouch Last;
        public override void EnterState(CustomizerGestureModel gesture) { }
        public override void ExitState() { }
        protected override void ProcessOneTouch(ITouch touch) { Count++; Last = touch; }
    }

    private static void CheckSceneTransition()
    {
        var downAction = new ClubPenguin.Adventure.PressAnywhereAction { TouchType = ClubPenguin.Adventure.PressAnywhereAction.ETouchType.DOWN };
        var upAction = new ClubPenguin.Adventure.PressAnywhereAction { TouchType = ClubPenguin.Adventure.PressAnywhereAction.ETouchType.UP };
        var hasVrPress = typeof(ClubPenguin.Adventure.PressAnywhereAction).GetMethod("HasVrPress", Private);
        Require(!(bool)hasVrPress.Invoke(downAction, null) && !(bool)hasVrPress.Invoke(upAction, null), "Press-anywhere action must not consume stale VR input.");
        ClubPenguin.Adventure.PressAnywhereAction.NotifyVrPointerPress(true, false);
        Require((bool)hasVrPress.Invoke(downAction, null) && !(bool)hasVrPress.Invoke(upAction, null), "VR trigger press must advance DOWN tutorials only.");
        ClubPenguin.Adventure.PressAnywhereAction.NotifyVrPointerPress(false, true);
        Require((bool)hasVrPress.Invoke(upAction, null), "VR trigger release must advance the design tutorial's press-anywhere UP action.");
        var nextAction = new ClubPenguin.Adventure.PressAnywhereAction();
        Require(!(bool)hasVrPress.Invoke(nextAction, null), "The next tutorial must wait for a new trigger gesture.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            "Assets/Game/ChangeRoom/Scenes/ClothingDesigner.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
        var host = new GameObject("Inactive tracking transition check"); host.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
        try
        {
            var controller = host.AddComponent<PCVRFirstPersonController>();
            var camera = host.AddComponent<Camera>();
            var inventory = AssetDatabase.LoadAssetAtPath<ClothingDesignerCameraData>("Assets/MonoBehaviour/InventoryCameraData.asset");
            var customize = AssetDatabase.LoadAssetAtPath<ClothingDesignerCameraData>("Assets/MonoBehaviour/CustomizerCameraData.asset");
            Transform inventoryPenguin = null, customizerPenguin = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "InventoryPenguinPreview") inventoryPenguin = child;
                    if (child.name == "CustomizerPenguinPreview") customizerPenguin = child;
                }
            Require(inventoryPenguin != null && customizerPenguin != null && Vector3.Distance(inventoryPenguin.position, customizerPenguin.position) > 3f,
                "Actual scene must contain distinct inventory and creation mannequins for this regression.");
            var transition = typeof(PCVRFirstPersonController).GetMethod("SetMenuTrackingView", Private);
            transition.Invoke(controller, new object[] { camera, inventory.Position, Quaternion.Euler(inventory.Rotation) });
            Vector3 headOrigin = new Vector3(0.2f, 1.6f, 0.1f); Set(controller, "menuHeadPositionOrigin", headOrigin);
            transition.Invoke(controller, new object[] { camera, customize.Position, Quaternion.Euler(customize.Rotation) });
            Require((Vector3)typeof(PCVRFirstPersonController).GetField("menuTrackingOriginPosition", Private).GetValue(controller) == customize.Position,
                "Create transition must move the VR tracking origin to the actual creation camera.");
            Require((Vector3)typeof(PCVRFirstPersonController).GetField("menuHeadPositionOrigin", Private).GetValue(controller) == headOrigin,
                "Changing mannequins must preserve physical headset movement origin.");
            Require(Vector3.Distance(camera.transform.position, customize.Position) < 0.001f &&
                Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(customize.Rotation)) < 0.001f,
                "Create viewpoint must reach the authored camera pose instead of staying at the inventory penguin.");
            // Use the real scene controller state to check tray selection too.
            var designer = UnityEngine.Object.FindFirstObjectByType<ClothingDesignerCameraController>();
            typeof(ClothingDesignerCameraController).GetProperty("CurrentCameraState").SetValue(designer, ClothingDesignerCameraState.Customizer);
            Type anchorType = typeof(PCVRFirstPersonController).GetNestedType("WorldCanvasAnchor", BindingFlags.NonPublic);
            object anchor = Activator.CreateInstance(anchorType);
            object[] arguments = { anchor };
            typeof(PCVRFirstPersonController).GetMethod("SetClosetPose", Private).Invoke(controller, arguments);
            Require((Transform)typeof(PCVRFirstPersonController).GetField("closetPreview", Private).GetValue(controller) == customizerPenguin,
                "Create tray must select the creation mannequin even while inventory mannequin remains active.");
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
    }
}
