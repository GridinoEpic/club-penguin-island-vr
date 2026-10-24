using UnityEngine;
using UnityEngine.EventSystems;

namespace ClubPenguin
{
	// A persistent pointer per hand. UI controls receive the same event lifecycle
	// as a mouse, including hover, release, drag, drop and scroll.
	public sealed class PCVRPointer
	{
		private readonly int id;
		private EventSystem eventSystem;
		private PointerEventData data;
        private Vector3? projectedDragWorldPosition;
		public bool HasPress => data != null && (data.pointerPress != null || data.pointerDrag != null);

		public PCVRPointer(int id) { this.id = id; }

		public Vector2 ProjectDragPosition(Ray ray, Vector2 fallback)
		{
			if (data == null || data.pointerDrag == null || data.pressEventCamera == null) return fallback;
			RaycastResult press = data.pointerPressRaycast;
			Plane plane = new Plane(press.worldNormal, press.worldPosition);
			if (!plane.Raycast(ray, out float distance) || distance < 0f) return fallback;
            projectedDragWorldPosition = ray.GetPoint(distance);
            return data.pressEventCamera.WorldToScreenPoint(projectedDragWorldPosition.Value);
		}

		public void Process(EventSystem system, RaycastResult hit, Vector2 position,
			bool pressed, bool released, Vector2 scroll, bool preserveSelection)
		{
			if (system == null) { Cancel(); return; }
			if (eventSystem != system || data == null)
			{
				Cancel();
				eventSystem = system;
				data = new PointerEventData(system) { pointerId = id, button = PointerEventData.InputButton.Left };
			}
			data.delta = position - data.position;
			data.position = position;
			data.pointerCurrentRaycast = hit;
			SetHover(hit.gameObject);
			if (pressed)
			{
				projectedDragWorldPosition = null;
                data.eligibleForClick = true;
				data.dragging = false;
				data.useDragThreshold = true;
				data.pressPosition = position;
				data.pointerPressRaycast = hit;
				data.clickCount = 1;
				data.clickTime = Time.unscaledTime;
				if (!preserveSelection && ExecuteEvents.GetEventHandler<ISelectHandler>(hit.gameObject) != system.currentSelectedGameObject)
					system.SetSelectedGameObject(null, data);
				GameObject down = ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerDownHandler);
				data.pointerClick = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
				data.pointerPress = down != null ? down : data.pointerClick;
				data.rawPointerPress = hit.gameObject;
				data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject);
				ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
			}
			if (!pressed && !released && data.pointerDrag != null && data.delta.sqrMagnitude > 0f)
			{
				if (!data.dragging && (!data.useDragThreshold ||
					(projectedDragWorldPosition.HasValue
                        ? (projectedDragWorldPosition.Value - data.pointerPressRaycast.worldPosition).sqrMagnitude >= 0.02f * 0.02f
                        : (position - data.pressPosition).sqrMagnitude >= Mathf.Pow(Mathf.Max(24, system.pixelDragThreshold), 2))))
				{
					ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
					data.dragging = true;
					data.eligibleForClick = false;
				}
				if (data.dragging)
				{
					if (data.pointerPress != data.pointerDrag)
					{
						ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
						data.pointerPress = null;
					}
					ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
				}
			}
			if (released) Release(hit.gameObject, true);
			if (scroll.sqrMagnitude > 0f)
			{
				data.scrollDelta = scroll;
				ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.scrollHandler);
			}
		}

		private void Release(GameObject target, bool allowClick)
		{
			ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
			if (allowClick && data.eligibleForClick && data.pointerClick != null &&
				data.pointerClick == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target))
				ExecuteEvents.Execute(data.pointerClick, data, ExecuteEvents.pointerClickHandler);
			else if (allowClick && data.dragging)
				ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.dropHandler);
			if (data.dragging) ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
			data.eligibleForClick = false;
			data.dragging = false;
			data.pointerPress = null;
			data.rawPointerPress = null;
			data.pointerClick = null;
			data.pointerDrag = null;
		}

		private void SetHover(GameObject target)
		{
			if (data.pointerEnter == target && target != null) return;
			Transform common = FindCommonParent(data.pointerEnter, target);
			foreach (GameObject hovered in data.hovered.ToArray())
			{
				if (hovered != null && common != null && (hovered.transform == common || common.IsChildOf(hovered.transform))) continue;
				ExecuteEvents.Execute(hovered, data, ExecuteEvents.pointerExitHandler);
				data.hovered.Remove(hovered);
			}
			data.pointerEnter = target;
			for (Transform current = target != null ? target.transform : null; current != null && current != common; current = current.parent)
			{
				ExecuteEvents.Execute(current.gameObject, data, ExecuteEvents.pointerEnterHandler);
				data.hovered.Add(current.gameObject);
			}
		}

		private static Transform FindCommonParent(GameObject first, GameObject second)
		{
			if (first == null || second == null) return null;
			for (Transform parent = first.transform; parent != null; parent = parent.parent)
				if (second.transform == parent || second.transform.IsChildOf(parent)) return parent;
			return null;
		}

		public void Cancel()
		{
			if (data == null) return;
			Release(null, false);
			SetHover(null);
			data = null;
			eventSystem = null;
		}
	}
}
