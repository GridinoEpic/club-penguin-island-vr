using ClubPenguin.Breadcrumbs;
using ClubPenguin.ClothingDesigner.Inventory;
using ClubPenguin.ClothingDesigner.ItemCustomizer;
using ClubPenguin.Core;
using Disney.Kelowna.Common;
using System.Collections;
using UnityEngine;

namespace ClubPenguin.ClothingDesigner
{
	public class DragAreaController : MonoBehaviour
	{
		public Vector2 DecalDragIconOffset = new Vector2(0f, 65f);

		public Vector2 FabricDragIconOffset = new Vector2(0f, 65f);

		public PersistentBreadcrumbTypeDefinitionKey TemplateBreadcrumbType;

		public PersistentBreadcrumbTypeDefinitionKey FabricBreadcrumbType;

		public PersistentBreadcrumbTypeDefinitionKey DecalBreadcrumbType;

		private readonly PrefabContentKey dragIconPrefabKey = new PrefabContentKey("Prefabs/InventoryDragIcon");

		private readonly PrefabContentKey dragFabricPrefabKey = new PrefabContentKey("Prefabs/ClothingDesigner/CustomizerDragIcon");

		private Camera mainCamera;

		private Camera guiCamera;

		private Transform penguinPreview;

		private GameObject penguinMannequin;

		private DragAreaState currentState;

		private DragContainer dragContainer;

		private DragContainer dragFabricContainer;

		private NoSelectionState noSelectionState;

		private DragTemplateState dragTemplateState;

		private DragDecalState dragDecalState;

		private DragDecalButtonState dragDecalButtonState;

		private DragFabricState dragFabricState;

		private DragFabricButtonState dragFabricButtonState;

		private RotatePenguinState rotatePenguinState;

		private EventChannel clothingDesignerEventChannel;

		private EventChannel inventoryEventChannel;

		private EventChannel customizerEventChannel;

		private CustomizerState customizerState;

		private OriginalFabricData fabricChannelData;

		private OriginalDecalData decalChannelData;

		private NoInventorySelectionState noInventorySelectionState;

		private DragInventoryButtonState dragInventoryButtonState;

		private RotatePenguinPreviewState rotatePenguinPreviewState;

		private bool isDragContainerLoaded;

		private bool isFabricDragContainerLoaded;

		private bool isUpdateEnabled;

		private void Awake()
		{
			setupClothingDesignerListeners();
			setupCustomizerListeners();
			setupInventoryListeners();
			isUpdateEnabled = true;
		}

		private void Start()
		{
			init();
		}

		private void setupClothingDesignerListeners()
		{
			clothingDesignerEventChannel = new EventChannel(ClothingDesignerContext.EventBus);
			clothingDesignerEventChannel.AddListener<ClothingDesignerUIEvents.ChangeStateInventory>(onSwitchToInventory);
			clothingDesignerEventChannel.AddListener<ClothingDesignerUIEvents.ChangeStateCustomizer>(onSwitchToCustomizer);
			clothingDesignerEventChannel.AddListener<ClothingDesignerUIEvents.EnableDragAreaControllerUpdates>(onEnableDragAreaControllerUpdates);
			clothingDesignerEventChannel.AddListener<ClothingDesignerUIEvents.DisableDragAreaControllerUpdates>(onDisableDragAreaControllerUpdates);
		}

		private void setupCustomizerListeners()
		{
			customizerEventChannel = new EventChannel(CustomizationContext.EventBus);
			customizerEventChannel.AddListener<CustomizerDragEvents.DragDecalButton>(onDragDecalButton);
			customizerEventChannel.AddListener<CustomizerDragEvents.DragOffChannel>(onDragFabric);
			customizerEventChannel.AddListener<CustomizerDragEvents.DragFabricButton>(onDragFabricButton);
			customizerEventChannel.AddListener<CustomizerDragEvents.RotatePenguin>(onRotatePenguin);
			customizerEventChannel.AddListener<CustomizerDragEvents.GestureComplete>(onGestureComplete);
			customizerEventChannel.AddListener<CustomizerModelEvents.ResetItemModelEvent>(onItemModelReset);
			customizerEventChannel.AddListener<CustomizerModelEvents.CustomizerStateChangedEvent>(onStateChange);
		}

		private void setupInventoryListeners()
		{
			inventoryEventChannel = new EventChannel(InventoryContext.EventBus);
			inventoryEventChannel.AddListener<InventoryDragEvents.GestureComplete>(onInventoryGestureComplete);
			inventoryEventChannel.AddListener<InventoryDragEvents.RotatePenguinPreview>(onRotatePenguinPreview);
		}

		private void init()
		{
			penguinPreview = GameObject.Find("InventoryPenguinPreview").transform;
			penguinMannequin = GameObject.Find("CustomizerPenguinPreview");
			isDragContainerLoaded = false;
			isFabricDragContainerLoaded = false;
			mainCamera = Camera.main;
			guiCamera = GameObject.FindGameObjectWithTag(UIConstants.Tags.GUI_CAMERA).GetComponent<Camera>();
			fabricChannelData = new OriginalFabricData();
			fabricChannelData.Clear();
			decalChannelData = new OriginalDecalData();
			decalChannelData.Clear();
			CoroutineRunner.Start(loadDragContainer(), this, "loadDragContainer");
			CoroutineRunner.Start(loadDragFabricContainer(), this, "loadDragFabricContainer");
		}

		private IEnumerator loadDragContainer()
		{
			AssetRequest<GameObject> assetRequest = Content.LoadAsync(dragIconPrefabKey);
			yield return assetRequest;
			GameObject dragContainerInstance = Object.Instantiate(assetRequest.Asset);
			dragContainerInstance.transform.SetParent(base.gameObject.transform, false);
			dragContainerInstance.name = "DragIcon";
			dragContainerInstance.SetActive(false);
			dragContainer = dragContainerInstance.GetComponent<DragContainer>();
			dragContainer.ImageOffset = DecalDragIconOffset;
			isDragContainerLoaded = true;
			setupStates();
		}

		private IEnumerator loadDragFabricContainer()
		{
			AssetRequest<GameObject> assetRequest = Content.LoadAsync(dragFabricPrefabKey);
			yield return assetRequest;
			GameObject dragFabricContainerInstance = Object.Instantiate(assetRequest.Asset);
			dragFabricContainerInstance.transform.SetParent(base.gameObject.transform, false);
			dragFabricContainerInstance.name = "DragFabricIcon";
			dragFabricContainerInstance.SetActive(false);
			dragFabricContainer = dragFabricContainerInstance.GetComponent<DragContainer>();
			dragFabricContainer.ImageOffset = FabricDragIconOffset;
			isFabricDragContainerLoaded = true;
			setupStates();
		}

		private void setupStates()
		{
			if (isDragContainerLoaded && isFabricDragContainerLoaded)
			{
				noInventorySelectionState = new NoInventorySelectionState(mainCamera);
				dragInventoryButtonState = new DragInventoryButtonState(dragContainer, mainCamera, guiCamera);
				rotatePenguinPreviewState = new RotatePenguinPreviewState(penguinPreview);
				noSelectionState = new NoSelectionState(mainCamera, fabricChannelData, decalChannelData, TemplateBreadcrumbType, FabricBreadcrumbType, DecalBreadcrumbType);
				noSelectionState.CurrentState = CustomizerState.FABRIC;
				dragTemplateState = new DragTemplateState(mainCamera, guiCamera, dragContainer);
				dragDecalState = new DragDecalState(base.gameObject, mainCamera, guiCamera, dragContainer);
				dragDecalState.DragIconOffset = DecalDragIconOffset;
				dragDecalState.SetPersistentChannelData(decalChannelData);
				dragDecalButtonState = new DragDecalButtonState(base.gameObject, mainCamera, guiCamera, dragContainer);
				dragDecalButtonState.DragIconOffset = DecalDragIconOffset;
				dragDecalButtonState.SetPersistentChannelData(decalChannelData);
				dragFabricState = new DragFabricState(base.gameObject, mainCamera, guiCamera, dragFabricContainer);
				dragFabricState.DragIconOffset = FabricDragIconOffset;
				dragFabricState.SetPersistentChannelData(fabricChannelData);
				dragFabricButtonState = new DragFabricButtonState(base.gameObject, mainCamera, guiCamera, dragFabricContainer);
				dragFabricButtonState.DragIconOffset = FabricDragIconOffset;
				dragFabricButtonState.SetPersistentChannelData(fabricChannelData);
				rotatePenguinState = new RotatePenguinState(penguinMannequin);
				currentState = noInventorySelectionState;
				currentState.EnterState(new CustomizerGestureModel());
			}
		}

		public void Update()
		{
			if (currentState != null && isUpdateEnabled && !UnityEngine.XR.XRSettings.enabled)
			{
				currentState.UpdateState();
			}
		}

        private int? vrPointerOwner;
        private Vector2 vrPointerPosition;

        public bool ProcessVrPointer(int pointerId, Ray ray, GameObject uiTarget,
            Vector2 screenPosition, bool pressed, bool held, bool released)
        {
            uiTarget = GetVrCustomizationUiTarget(uiTarget);
            if (currentState == null || !isUpdateEnabled) { vrPointerOwner = null; return false; }
            if (!vrPointerOwner.HasValue)
            {
                // Standard UI controls keep their normal EventSystem handling.
                if (!pressed || currentState != noSelectionState) return false;
                var button = uiTarget != null ? uiTarget.GetComponentInParent<CustomizationButton>() : null;
                bool clothingHit = uiTarget == null && DragAreaState.RaycastClothing(ray, out RaycastHit hit);
                if ((button == null || !button.CanDrag || button.GetTexture == null) && !clothingHit) return false;
                vrPointerOwner = pointerId;
                vrPointerPosition = screenPosition;
            }
            if (vrPointerOwner.Value != pointerId) return true;
            var touch = new DragAreaState.ITouch
            {
                phase = pressed ? TouchPhase.Began : released ? TouchPhase.Ended : held ? TouchPhase.Moved : TouchPhase.Canceled,
                position = screenPosition,
                deltaPosition = screenPosition - vrPointerPosition,
                worldRay = ray,
                uiTarget = uiTarget
            };
            vrPointerPosition = screenPosition;
            var previousState = currentState;
            currentState.ProcessPointerTouch(touch);
            // The first movement switches states; also preview the drop in that frame.
            if (touch.phase == TouchPhase.Moved && currentState != previousState)
                currentState.ProcessPointerTouch(touch);
            if (released || !held) vrPointerOwner = null;
            return true;
        }

        public static GameObject GetVrCustomizationUiTarget(GameObject target)
        {
            if (target == null || target.GetComponentInParent<DragContainer>(true) != null) return null;
            if (target.GetComponentInParent<CustomizationButton>(true) != null ||
                target.GetComponentInParent<UnityEngine.UI.Selectable>(true) != null ||
                UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerClickHandler>(target) != null ||
                UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IPointerDownHandler>(target) != null ||
                UnityEngine.EventSystems.ExecuteEvents.GetEventHandler<UnityEngine.EventSystems.IDragHandler>(target) != null ||
                target.CompareTag("RaycastBlockingUI")) return target;
            return null;
        }

		private bool onItemModelReset(CustomizerModelEvents.ResetItemModelEvent evt)
		{
			fabricChannelData.Clear();
			decalChannelData.Clear();
			return false;
		}

		private bool onSwitchToInventory(ClothingDesignerUIEvents.ChangeStateInventory evt)
		{
			currentState.ExitState();
			currentState = noInventorySelectionState;
			noInventorySelectionState.EnterState(new CustomizerGestureModel());
			return false;
		}

		private bool onSwitchToCustomizer(ClothingDesignerUIEvents.ChangeStateCustomizer evt)
		{
			currentState.ExitState();
			currentState = noSelectionState;
			noSelectionState.EnterState(new CustomizerGestureModel());
			return false;
		}

		private bool onStateChange(CustomizerModelEvents.CustomizerStateChangedEvent evt)
		{
			CustomizerState customizerState = this.customizerState = evt.NewState;
			noSelectionState.CurrentState = customizerState;
			return false;
		}

		private bool onGestureComplete(CustomizerDragEvents.GestureComplete evt)
		{
			currentState.ExitState();
			currentState = noSelectionState;
			noSelectionState.EnterState(new CustomizerGestureModel());
			return false;
		}

		private bool onTemplateDrag(CustomizerDragEvents.DragTemplate evt)
		{
			currentState.ExitState();
			currentState = dragTemplateState;
			dragTemplateState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onDragDecalButton(CustomizerDragEvents.DragDecalButton evt)
		{
			currentState.ExitState();
			currentState = dragDecalButtonState;
			dragDecalButtonState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onDragFabric(CustomizerDragEvents.DragOffChannel evt)
		{
			currentState.ExitState();
			if (customizerState == CustomizerState.DECAL)
			{
				currentState = dragDecalState;
				dragDecalState.EnterState(evt.GestureModel);
			}
			else
			{
				currentState = dragFabricState;
				dragFabricState.EnterState(evt.GestureModel);
			}
			return false;
		}

		private bool onDragFabricButton(CustomizerDragEvents.DragFabricButton evt)
		{
			currentState.ExitState();
			currentState = dragFabricButtonState;
			dragFabricButtonState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onRotatePenguin(CustomizerDragEvents.RotatePenguin evt)
		{
			currentState.ExitState();
			currentState = rotatePenguinState;
			rotatePenguinState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onInventoryGestureComplete(InventoryDragEvents.GestureComplete evt)
		{
			currentState.ExitState();
			currentState = noInventorySelectionState;
			noInventorySelectionState.EnterState(new CustomizerGestureModel());
			return false;
		}

		private bool onDragInventoryButton(InventoryDragEvents.DragInventoryButton evt)
		{
			currentState.ExitState();
			currentState = dragInventoryButtonState;
			dragInventoryButtonState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onRotatePenguinPreview(InventoryDragEvents.RotatePenguinPreview evt)
		{
			currentState.ExitState();
			currentState = rotatePenguinPreviewState;
			rotatePenguinPreviewState.EnterState(evt.GestureModel);
			return false;
		}

		private bool onDisableDragAreaControllerUpdates(ClothingDesignerUIEvents.DisableDragAreaControllerUpdates evt)
		{
			isUpdateEnabled = false;
			return false;
		}

		private bool onEnableDragAreaControllerUpdates(ClothingDesignerUIEvents.EnableDragAreaControllerUpdates evt)
		{
			isUpdateEnabled = true;
			return false;
		}

		private void OnDestroy()
		{
			if (clothingDesignerEventChannel != null)
			{
				clothingDesignerEventChannel.RemoveAllListeners();
			}
			if (customizerEventChannel != null)
			{
				customizerEventChannel.RemoveAllListeners();
			}
			if (inventoryEventChannel != null)
			{
				inventoryEventChannel.RemoveAllListeners();
			}
		}
	}
}
