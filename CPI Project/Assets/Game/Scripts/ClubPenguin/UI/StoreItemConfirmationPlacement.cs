using UnityEngine;
using UnityEngine.UI;

namespace ClubPenguin.UI
{
	[DisallowMultipleComponent]
	public class StoreItemConfirmationPlacement : MonoBehaviour
	{
		[SerializeField]
		private Image topPointer = null;

		[SerializeField]
		private Image bottomPointer = null;

		[SerializeField]
		private float yPadding = 15f;

		private RectTransform scrollRectTransform;

		public void PositionConfirmation(RectTransform shopItem, RectTransform scrollRectTransform)
		{
			this.scrollRectTransform = scrollRectTransform;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
                SetWorldCanvasPosition(shopItem);
            else setConfirmationPosition(shopItem);
		}

        private void SetWorldCanvasPosition(RectTransform shopItem)
        {
            // Shop geometry is expressed in canvas units, not screen pixels or
            // world X/Y. Keep the purchase button inside the rotated scroll panel.
            RectTransform parent = (RectTransform)transform.parent;
            RectTransform confirmation = (RectTransform)transform;
            Bounds item = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, shopItem);
            Vector3[] corners = new Vector3[4];
            scrollRectTransform.GetWorldCorners(corners);
            Vector3 min = parent.InverseTransformPoint(corners[0]);
            Vector3 max = min;
            foreach (Vector3 corner in corners)
            {
                Vector3 local = parent.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            float width = confirmation.rect.width * Mathf.Abs(confirmation.localScale.x);
            float height = confirmation.rect.height * Mathf.Abs(confirmation.localScale.y);
            float left = width * confirmation.pivot.x;
            float right = width - left;
            float bottom = height * confirmation.pivot.y;
            float top = height - bottom;
            float y = item.min.y - yPadding - top;
            if (y - bottom < min.y) y = item.max.y + yPadding + bottom;
            float x = Mathf.Clamp(item.center.x, min.x + left, Mathf.Max(min.x + left, max.x - right));
            y = Mathf.Clamp(y, min.y + bottom, Mathf.Max(min.y + bottom, max.y - top));
            confirmation.localPosition = new Vector3(x, y, parent.InverseTransformPoint(shopItem.position).z);
            bool below = y < item.center.y;
            if (topPointer != null) topPointer.enabled = below;
            if (bottomPointer != null) bottomPointer.enabled = !below;
            Image pointer = below ? topPointer : bottomPointer;
            if (pointer != null)
            {
                Vector3 position = pointer.transform.localPosition;
                position.x = (item.center.x - x) / Mathf.Max(0.0001f, Mathf.Abs(confirmation.localScale.x));
                pointer.transform.localPosition = position;
            }
        }

		private void setConfirmationPosition(RectTransform shopItem)
		{
			float num = calculateYOffset(shopItem);
			float num2 = shopItem.position.y + num;
			float num3 = calculateXOffset(shopItem, num2);
			float x = shopItem.position.x + num3;
			base.transform.position = new Vector3(x, num2, shopItem.position.z);
			showPointer(num3, num);
		}

		private float calculateYOffset(RectTransform shopItem)
		{
			float num = shopItem.rect.height * 0.5f;
			float num2 = ((RectTransform)base.transform).rect.height * 0.5f;
			float num3 = num2 * base.transform.lossyScale.y;
			float num4 = (0f - (num2 + num + yPadding)) * base.transform.lossyScale.y;
			float y = shopItem.position.y + num4 - num3;
			if (!RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, new Vector2(shopItem.position.x, y)))
			{
				num4 *= -1f;
			}
			return num4;
		}

		private float calculateXOffset(RectTransform shopItem, float yPosition)
		{
			float x = base.transform.lossyScale.x;
			float num = ((RectTransform)base.transform).rect.width * 0.5f;
			float num2 = 0f;
			float num3 = shopItem.position.x + num2;
			if (!RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, new Vector2(num3 - num * x, yPosition)) && !RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, new Vector2(num3 - num * x, shopItem.position.y)))
			{
				float num4 = shopItem.rect.width * 0.5f;
				num2 = (num - num4) * x;
			}
			else if (!RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, new Vector2(num3 + num * x, yPosition)) && !RectTransformUtility.RectangleContainsScreenPoint(scrollRectTransform, new Vector2(num3 + num * x, shopItem.position.y)))
			{
				float num4 = shopItem.rect.width * 0.5f;
				num2 = (0f - (num - num4)) * x;
			}
			return num2;
		}

		private void showPointer(float xOffset, float yOffset)
		{
			bool flag = yOffset <= 0f;
			float x = (0f - xOffset) / base.transform.lossyScale.x;
			if (topPointer != null)
			{
				topPointer.enabled = flag;
				if (topPointer.enabled)
				{
					topPointer.gameObject.transform.localPosition = new Vector2(x, topPointer.gameObject.transform.localPosition.y);
				}
			}
			if (bottomPointer != null)
			{
				bottomPointer.enabled = !flag;
				if (bottomPointer.enabled)
				{
					bottomPointer.gameObject.transform.localPosition = new Vector2(x, bottomPointer.gameObject.transform.localPosition.y);
				}
			}
		}
	}
}
