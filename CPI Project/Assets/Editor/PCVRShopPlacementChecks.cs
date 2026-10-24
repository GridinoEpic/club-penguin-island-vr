using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ClubPenguin;
using UnityEditor;
using UnityEngine;

internal static class PCVRShopPlacementChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private const string RequestPath = "Library/PCVR_SHOP_CHECK_REQUEST.txt";
    private const string ResultPath = "Library/PCVR_SHOP_CHECK_RESULT.txt";

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.update += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            try
            {
                Run();
                File.WriteAllText(ResultPath, "PASSED: 18 checks covering store switching, wall clearance, headset leaning, fixed pose, and missing-trigger cleanup");
            }
            catch (Exception error)
            {
                File.WriteAllText(ResultPath, error.ToString());
                Debug.LogException(error);
            }
        };
    }

    [MenuItem("CPI VR/Validate store placement")]
    public static void Run()
    {
        Type type = typeof(PCVRFirstPersonController);
        FieldInfo activeShop = type.GetField("activeShopAnchor", StaticPrivate);
        FieldInfo approach = type.GetField("shopApproachPosition", StaticPrivate);
        object previousShop = activeShop.GetValue(null);
        object previousApproach = approach.GetValue(null);
        FieldInfo panelPosition = type.GetField("shopPanelPosition", StaticPrivate);
        FieldInfo panelRotation = type.GetField("shopPanelRotation", StaticPrivate);
        FieldInfo instance = type.GetField("instance", StaticPrivate);
        object previousPositionState = panelPosition.GetValue(null);
        object previousRotationState = panelRotation.GetValue(null);
        object previousInstance = instance.GetValue(null);
        ActionSequencer previousSequencer = SceneRefs.ActionSequencer;
        GameObject root = new GameObject("Store placement checks") { hideFlags = HideFlags.HideAndDontSave };
        GameObject physicsRoot = new GameObject("Store clearance checks") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            instance.SetValue(null, null);
            var controller = root.AddComponent<PCVRFirstPersonController>();
            var sequencer = root.AddComponent<ActionSequencer>();
            var sequences = new Dictionary<int, ActionSequencer.State>();
            typeof(ActionSequencer).GetField("sequencerDict", Private).SetValue(sequencer, sequences);
            SceneRefs.SetActionSequencer(sequencer);
            Transform first = Child(root, "Marketplace", Vector3.zero, 90f);
            Transform second = Child(root, "Disney shop", new Vector3(20, 0, 30), 180f);
            Transform third = Child(root, "Furniture shop", new Vector3(-15, 0, 12), 270f);
            Transform visitor = Child(root, "Visitor", new Vector3(3, 1.4f, 0), 0f);
            Type anchorType = type.GetNestedType("WorldCanvasAnchor", BindingFlags.NonPublic);
            object[] pose = { Activator.CreateInstance(anchorType) };
            MethodInfo setPose = type.GetMethod("SetShowroomPose", Private);
            FieldInfo position = anchorType.GetField("Position");
            FieldInfo rotation = anchorType.GetField("Rotation");

            PCVRFirstPersonController.SetShopAnchor(first, visitor);
            setPose.Invoke(controller, pose);
            Vector3 previousPosition = (Vector3)position.GetValue(pose[0]);
            foreach (Transform shop in new[] { second, third })
            {
                visitor.position = shop.position + shop.forward * 3f + Vector3.up * 1.4f;
                sequences[visitor.gameObject.GetInstanceID()] = new ActionSequencer.State(visitor.gameObject, shop.gameObject);
                PCVRFirstPersonController.SetShopAnchorFromInteraction(visitor.gameObject);
                // Completing the action releases its trigger before the popup finishes loading.
                sequences.Clear();
                setPose.Invoke(controller, pose);
                Vector3 currentPosition = (Vector3)position.GetValue(pose[0]);
                Quaternion currentRotation = (Quaternion)rotation.GetValue(pose[0]);
                Require(Vector3.Distance(currentPosition, previousPosition) > 10f, "Switching stores must replace the previous position.");
                Vector3 expected = visitor.position - shop.forward * 0.9f;
                Require(Vector3.ProjectOnPlane(currentPosition - expected, Vector3.up).sqrMagnitude < 0.0001f,
                    "Each popup must use its own interaction trigger.");
                Require(Vector3.Dot(currentRotation * Vector3.forward, -shop.forward) > 0.999f,
                    "Each popup must face its visitor.");
                visitor.position += new Vector3(5, 2, 8);
                visitor.rotation = Quaternion.Euler(40, 20, 30);
                setPose.Invoke(controller, pose);
                Require(Vector3.Distance(currentPosition, (Vector3)position.GetValue(pose[0])) < 0.001f,
                    "The open store must stay fixed after the visitor moves.");
                previousPosition = currentPosition;
            }
            PCVRFirstPersonController.SetShopAnchorFromInteraction(visitor.gameObject);
            Require(activeShop.GetValue(null) == null, "An interaction without a trigger must not reuse the last shop.");
            PCVRFirstPersonController.SetShopAnchor(first, null);
            Require(activeShop.GetValue(null) == null, "A missing visitor must not retain a shop anchor.");
            MethodInfo clearPosition = type.GetMethod("GetClearShopPanelPosition", StaticPrivate);
            Vector3 head = new Vector3(4000, 4000, 4000);
            Vector3 desired = head + Vector3.forward * 0.9f;
            Transform body = Child(physicsRoot, "Visitor body", head + Vector3.left * 0.5f, 0f);
            Func<Vector3> resolve = () => (Vector3)clearPosition.Invoke(null, new object[] { head, desired, body });
            Require(Vector3.Distance(resolve(), desired) < 0.001f, "An unobstructed panel must keep its usual distance.");
            BoxCollider wall = Child(physicsRoot, "Wall", head + Vector3.forward * 0.55f, 0f).gameObject.AddComponent<BoxCollider>();
            wall.size = new Vector3(4, 4, 0.1f);
            Physics.SyncTransforms();
            Vector3 expectedClear = head + Vector3.forward * 0.38f;
            Require(Vector3.Distance(resolve(), expectedClear) < 0.002f, "The panel must sit 12 cm in front of the wall.");
            BoxCollider trigger = Child(physicsRoot, "Shop trigger", head + Vector3.forward * 0.15f, 0f).gameObject.AddComponent<BoxCollider>();
            trigger.size = Vector3.one * 0.1f;
            trigger.isTrigger = true;
            BoxCollider self = Child(body.gameObject, "Visitor collider", head + Vector3.forward * 0.25f, 0f).gameObject.AddComponent<BoxCollider>();
            self.size = Vector3.one * 0.1f;
            BoxCollider fartherWall = Child(physicsRoot, "Farther wall", head + Vector3.forward * 0.75f, 0f).gameObject.AddComponent<BoxCollider>();
            fartherWall.size = wall.size;
            Physics.SyncTransforms();
            Require(Vector3.Distance(resolve(), expectedClear) < 0.002f, "Use the nearest solid wall and ignore triggers and the visitor.");
            fartherWall.enabled = false;
            Vector3 diagonal = Quaternion.Euler(0, 60, 0) * Vector3.forward;
            desired = head + diagonal * 0.9f;
            wall.transform.SetPositionAndRotation(head + diagonal * 0.55f, Quaternion.Euler(0, 60, 0));
            Physics.SyncTransforms();
            Require(Vector3.Distance(resolve(), head + diagonal * 0.38f) < 0.002f, "Wall clearance must work for rotated storefronts.");
            desired = head + Vector3.forward * 0.9f;
            wall.transform.SetPositionAndRotation(head + Vector3.forward * 0.09f, Quaternion.identity);
            Physics.SyncTransforms();
            float closeDistance = Vector3.Dot(resolve() - head, Vector3.forward);
            Require(closeDistance > 0f && closeDistance < 0.04f, "A very close wall must not push the panel through the wall or behind the head.");
            wall.transform.position = head + Vector3.forward * 2f;
            Physics.SyncTransforms();
            Require(Vector3.Distance(resolve(), desired) < 0.001f, "A wall beyond the panel must not pull it farther away.");

            wall.transform.position = head + Vector3.forward * 0.55f;
            Physics.SyncTransforms();
            Camera camera = Child(root, "Headset", head, 0f).gameObject.AddComponent<Camera>();
            type.GetField("playerCamera", Private).SetValue(controller, camera);
            instance.SetValue(null, controller);
            first.SetPositionAndRotation(head + Vector3.forward * 2f, Quaternion.identity);
            PCVRFirstPersonController.SetShopAnchor(first, body);
            setPose.Invoke(controller, pose);
            Require(Vector3.Distance((Vector3)position.GetValue(pose[0]), expectedClear) < 0.002f,
                "The clearance ray must start at the headset, including its offset from the visitor body.");
            camera.transform.position += Vector3.left;
            wall.transform.position += Vector3.forward;
            Physics.SyncTransforms();
            setPose.Invoke(controller, pose);
            Require(Vector3.Distance((Vector3)position.GetValue(pose[0]), expectedClear) < 0.002f,
                "The collision-adjusted panel must remain fixed after opening.");
            Debug.Log("PCVR store placement checks PASSED (18 checks).");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(physicsRoot);
            SceneRefs.SetActionSequencer(previousSequencer);
            activeShop.SetValue(null, previousShop);
            approach.SetValue(null, previousApproach);
            panelPosition.SetValue(null, previousPositionState);
            panelRotation.SetValue(null, previousRotationState);
            instance.SetValue(null, previousInstance);
        }
    }

    private static Transform Child(GameObject root, string name, Vector3 position, float yaw)
    {
        Transform child = new GameObject(name).transform;
        child.SetParent(root.transform);
        child.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        return child;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
