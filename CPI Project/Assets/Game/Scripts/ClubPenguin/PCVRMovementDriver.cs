using UnityEngine;

namespace ClubPenguin
{
	// Read input before locomotion simulates. The camera bridge still runs late
	// so animation and the original camera director cannot overwrite head tracking.
	[DefaultExecutionOrder(-10000)]
	[RequireComponent(typeof(PCVRFirstPersonController))]
	public sealed class PCVRMovementDriver : MonoBehaviour
	{
		private PCVRFirstPersonController controller;
		private void Awake() { controller = GetComponent<PCVRFirstPersonController>(); }
		private void Update()
		{
			if (controller.isActiveAndEnabled) controller.UpdateLocomotion();
		}
	}
}
