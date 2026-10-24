using System;
using System.IO;
using System.Reflection;
using ClubPenguin.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class PCVRQuestNavigationChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.update += () =>
        {
            const string request = "Library/PCVR_QUEST_CHECK_REQUEST.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            File.Delete(request);
            try
            {
                Run();
                File.WriteAllText("Library/PCVR_QUEST_CHECK_RESULT.txt", "PASSED: objective projection, head pitch/lean, moved targets, canvas transforms/pivots, edge directions, and behind-head targets.");
            }
            catch (Exception error)
            {
                File.WriteAllText("Library/PCVR_QUEST_CHECK_RESULT.txt", error.ToString());
                Debug.LogException(error);
            }
        };
    }

    [MenuItem("CPI VR/Validate objective arrow")]
    public static void Run()
    {
        GameObject root = new GameObject("Objective arrow checks") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            Camera camera = new GameObject("Eye", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            RectTransform panel = Rect(root.transform, "HUD");
            panel.sizeDelta = new Vector2(1000, 600);
            panel.position = new Vector3(2, 1, 4);
            panel.rotation = Quaternion.Euler(0, 35, 0);
            panel.localScale = Vector3.one * 0.003f;
            QuestNavigation navigation = panel.gameObject.AddComponent<QuestNavigation>();
            navigation.TransformPosition = Rect(panel, "Position");
            navigation.TransformPosition.anchorMin = navigation.TransformPosition.anchorMax = Vector2.zero;
            navigation.TransformRotation = Rect(navigation.TransformPosition, "Rotation");
            navigation.TransformDerotation = Rect(navigation.TransformRotation, "LabelRotation");
            navigation.OnScreenGO = Rect(navigation.TransformRotation, "Onscreen").gameObject;
            navigation.OffScreenGO = Rect(navigation.TransformRotation, "Offscreen").gameObject;
            navigation.OnscreenIndicatorOffset = Vector3.zero;
            navigation.OnScreenGO.AddComponent<CanvasRenderer>();
            navigation.OnScreenGO.AddComponent<Text>();
            navigation.OffScreenGO.AddComponent<CanvasRenderer>();
            navigation.OffScreenGO.AddComponent<Text>();
            Transform target = new GameObject("Objective").transform;
            target.SetParent(root.transform, false);
            Set(navigation, "worldContainerRect", panel);
            Set(navigation, "targetTransform", target);
            FieldInfo state = typeof(QuestNavigation).GetField("currentState", Private);
            state.SetValue(navigation, Enum.ToObject(state.FieldType, 2));
            Set(navigation, "isDisabled", true);

            for (int i = 0; i < 4; i++)
            {
                panel.pivot = i == 3 ? new Vector2(0.2f, 0.7f) : Vector2.one * 0.5f;
                camera.transform.position = panel.TransformPoint(new Vector3(40 * i, -20 * i, -600));
                camera.transform.rotation = panel.rotation * Quaternion.Euler(5 * i, 0, 0);
                Vector3 expected = panel.TransformPoint(new Vector3(panel.rect.center.x + 60, panel.rect.center.y + 30, 0));
                target.position = camera.transform.position + (expected - camera.transform.position) * (3 + i);
                Update(navigation, camera);
                if (Vector3.Distance(navigation.TransformPosition.position, expected) > 0.0001f)
                    throw new Exception("Arrow did not align with the target ray after head/canvas movement.");
                if (Quaternion.Angle(navigation.TransformRotation.localRotation, Quaternion.identity) > 0.01f)
                    throw new Exception("Onscreen arrow retained offscreen rotation.");
            }

            panel.pivot = Vector2.one * 0.5f;
            camera.transform.position = panel.TransformPoint(new Vector3(0, 0, -600));
            camera.transform.rotation = panel.rotation;
            Set(navigation, "showOnscreenIndicator", true);
            navigation.OnscreenIndicatorOffset = camera.transform.up * 2f;
            foreach (float distance in new[] { 7f, 5f, 3f, 1f, 0.25f })
            {
                target.position = camera.transform.position + camera.transform.forward * distance;
                Update(navigation, camera);
                if (Convert.ToInt32(state.GetValue(navigation)) != 2)
                    throw new Exception("Visible close objective became an offscreen dot at " + distance + "m.");
                Vector3 point = panel.InverseTransformPoint(navigation.TransformPosition.position);
                if (point.y > 210.01f || float.IsNaN(point.y))
                    throw new Exception("Close objective arrow escaped the HUD.");
            }
            navigation.OnscreenIndicatorOffset = Vector3.zero;
            Vector3[] directions = { new Vector3(2000, 0, 1200), new Vector3(-2000, 0, 1200),
                new Vector3(0, 2000, 1200), new Vector3(0, -2000, 1200),
                new Vector3(2000, 2000, 1200), new Vector3(1000, 0, -1200),
                new Vector3(0, 0, -1200), new Vector3(2000, 0, 0) };
            foreach (Vector3 direction in directions)
            {
                target.position = camera.transform.position + panel.TransformVector(direction);
                Update(navigation, camera);
                Vector3 point = panel.InverseTransformPoint(navigation.TransformPosition.position);
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || Mathf.Abs(point.x) > 430.01f || Mathf.Abs(point.y) > 210.01f)
                    throw new Exception("Offscreen arrow escaped panel bounds or produced invalid coordinates.");
                if (Mathf.Abs(Mathf.Abs(point.x) - 430) > 0.01f && Mathf.Abs(Mathf.Abs(point.y) - 210) > 0.01f)
                    throw new Exception("Offscreen arrow did not reach an edge.");
                Vector3 arrowUp = panel.InverseTransformDirection(navigation.TransformRotation.up);
                if (Vector2.Dot(new Vector2(point.x, point.y).normalized, new Vector2(arrowUp.x, arrowUp.y).normalized) < 0.999f)
                    throw new Exception("Arrow rotation did not point toward its edge position.");
            }
            Debug.Log("CPI PCVR objective arrow checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Set(object value, string field, object data)
    {
        typeof(QuestNavigation).GetField(field, Private).SetValue(value, data);
    }

    private static void Update(QuestNavigation navigation, Camera camera)
    {
        typeof(QuestNavigation).GetMethod("updateWorldSpaceIndicatorTransform", Private).Invoke(navigation, new object[] { camera });
    }
}
