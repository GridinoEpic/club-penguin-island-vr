using System;
using System.IO;
using System.Reflection;
using ClubPenguin;
using ClubPenguin.Locomotion;
using UnityEditor;
using UnityEngine;

internal static class PCVRSwimmingChecks
{
    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.update += () =>
        {
            const string request = "Library/PCVR_SWIMMING_CHECK_REQUEST.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            File.Delete(request);
            try
            {
                Run();
                File.WriteAllText("Library/PCVR_SWIMMING_CHECK_RESULT.txt", "PASSED: head yaw/pitch/roll, vertical gaze, backward/downward strokes, recovery and idle hands.");
            }
            catch (Exception error)
            {
                File.WriteAllText("Library/PCVR_SWIMMING_CHECK_RESULT.txt", error.ToString());
                Debug.LogException(error);
            }
        };
    }

    [MenuItem("CPI VR/Validate head and flipper swimming")]
    public static void Run()
    {
        GameObject head = new GameObject("Swimming test head") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            MethodInfo direction = typeof(LocomotionUtils).GetMethod("GetHeadDirectedSwimDirection", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo stroke = typeof(PCVRFirstPersonController).GetMethod("CalculateSwimStroke", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (Vector3 angles in new[] { Vector3.zero, new Vector3(35, 90, 40), new Vector3(-60, 220, -30), new Vector3(90, 0, 0) })
            {
                head.transform.rotation = Quaternion.Euler(angles);
                Vector3 forward = (Vector3)direction.Invoke(null, new object[] { head.transform.rotation, Vector2.up });
                if (Vector3.Dot(forward, head.transform.forward) < 0.999f)
                    throw new Exception("Forward swimming did not follow the head at " + angles);
                Vector3 right = (Vector3)direction.Invoke(null, new object[] { head.transform.rotation, Vector2.right });
                if (Mathf.Abs(right.y) > 0.0001f || Mathf.Abs(right.magnitude - 1f) > 0.001f)
                    throw new Exception("Head roll/vertical gaze broke lateral swimming.");
            }
            foreach (Vector3 velocity in new[] { Vector3.back, Vector3.down })
                if ((float)stroke.Invoke(null, new object[] { velocity, Vector3.forward }) <= 0f)
                    throw new Exception("Power stroke produced no propulsion.");
            foreach (Vector3 velocity in new[] { Vector3.forward, Vector3.up, Vector3.zero, Vector3.back * 0.1f })
                if ((float)stroke.Invoke(null, new object[] { velocity, Vector3.forward }) != 0f)
                    throw new Exception("Recovery or idle hand produced phantom propulsion.");
            if ((float)stroke.Invoke(null, new object[] { Vector3.left, Vector3.right }) <= 0f)
                throw new Exception("Turning the head broke backward pull detection.");
        }
        finally { UnityEngine.Object.DestroyImmediate(head); }
    }
}
