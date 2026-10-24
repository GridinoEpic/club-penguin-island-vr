using UnityEngine;

namespace ClubPenguin
{
	/// <summary>Two-bone solve with fixed lengths and a body-relative elbow pole.</summary>
	public static class PCVRArmIK
	{
		public static void Solve(Vector3 shoulder, Vector3 target, Vector3 pole, float upperLength,
			float lowerLength, out Vector3 elbow, out Vector3 wrist)
		{
			upperLength = Mathf.Max(0.001f, upperLength);
			lowerLength = Mathf.Max(0.001f, lowerLength);
			Vector3 delta = target - shoulder;
			Vector3 direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector3.down;
			float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.0001f,
				upperLength + lowerLength - 0.0001f);
			Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
			if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
			if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);
			float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
			float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
			elbow = shoulder + direction * along + bend.normalized * height;
			wrist = shoulder + direction * distance;
		}
	}
}
