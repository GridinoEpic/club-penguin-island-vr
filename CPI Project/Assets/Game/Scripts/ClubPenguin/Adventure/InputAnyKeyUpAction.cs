using ClubPenguin.Input;
using HutongGames.PlayMaker;
using System;
using UnityEngine;

namespace ClubPenguin.Adventure
{
	[ActionCategory("GUI")]
	public class InputAnyKeyUpAction : FsmStateAction
	{
		public FsmEvent FinishEvent;

		private AnyKeyInputActionHandler inputHandler;

		private static uint vrReleaseSequence;
		private uint vrReleaseAtEnter;
		private static int waitingActions;
		public static bool IsWaitingForInput => waitingActions > 0;

		public static void NotifyVrTriggerRelease()
		{
			vrReleaseSequence++;
		}

		public override void OnEnter()
		{
			vrReleaseAtEnter = vrReleaseSequence;
			waitingActions++;
			inputHandler = base.Owner.AddComponent<AnyKeyInputActionHandler>();
			AnyKeyInputActionHandler anyKeyInputActionHandler = inputHandler;
			anyKeyInputActionHandler.OnInputHandled = (Action)Delegate.Combine(anyKeyInputActionHandler.OnInputHandled, new Action(onInputHandled));
		}

		public override void OnUpdate()
		{
			if (UnityEngine.XR.XRSettings.enabled && vrReleaseSequence != vrReleaseAtEnter)
			{
				vrReleaseAtEnter = vrReleaseSequence;
				onInputHandled();
			}
		}

		public override void OnExit()
		{
			waitingActions = Math.Max(0, waitingActions - 1);
			AnyKeyInputActionHandler anyKeyInputActionHandler = inputHandler;
			anyKeyInputActionHandler.OnInputHandled = (Action)Delegate.Remove(anyKeyInputActionHandler.OnInputHandled, new Action(onInputHandled));
			UnityEngine.Object.Destroy(inputHandler);
		}

		private void onInputHandled()
		{
			if (FinishEvent != null)
			{
				base.Fsm.Event(FinishEvent);
			}
			else
			{
				Finish();
			}
		}
	}
}
