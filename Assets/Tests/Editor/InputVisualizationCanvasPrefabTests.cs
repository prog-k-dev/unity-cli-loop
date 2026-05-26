using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the shared input visualization prefab contract.
    /// </summary>
    public sealed class InputVisualizationCanvasPrefabTests
    {
        private const string PrefabPath =
            "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";
        private const string RuntimeAssemblyDefinitionPath =
            "Packages/src/Runtime/uLoopMCP.Runtime.asmdef";
        private static readonly string[] RuntimeOverlayPrefabPaths =
        {
            PrefabPath,
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateKeyboard/SimulateKeyboardOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateMouseUi/SimulateMouseUiOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/SimulateMouseInput/SimulateMouseInputOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/RecordInput/RecordInputOverlay.prefab",
            "Packages/io.github.hatayama.uloopmcp/Runtime/ReplayInput/ReplayInputOverlay.prefab"
        };

        [Test]
        public void RuntimeAssemblyDefinition_WhenScanned_IsAttachableAndNotAutoReferenced()
        {
            // Verifies the overlay MonoBehaviours can attach to prefabs without becoming player auto-references.
            JObject asmdef = JObject.Parse(ReadText(RuntimeAssemblyDefinitionPath));
            JToken includePlatforms = asmdef["includePlatforms"];

            Assert.That(asmdef["autoReferenced"]?.Value<bool>(), Is.False);
            Assert.That(includePlatforms, Is.Not.Null);
            Assert.That(includePlatforms!.Type, Is.EqualTo(JTokenType.Array));
            Assert.That(includePlatforms!.HasValues, Is.False);
        }

        [Test]
        public void InputVisualizationCanvasPrefab_WhenLoaded_HasRuntimeOverlayReferences()
        {
            // Verifies that overlay tools can instantiate the shared visualization canvas.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            InputVisualizationCanvas canvas = prefab.GetComponent<InputVisualizationCanvas>();

            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.KeyboardOverlay, Is.Not.Null);
            Assert.That(canvas.MouseUiOverlay, Is.Not.Null);
            Assert.That(canvas.MouseInputOverlay, Is.Not.Null);
            Assert.That(canvas.RecordInputOverlayPresenter, Is.Not.Null);
            Assert.That(canvas.ReplayInputOverlay, Is.Not.Null);
        }

        [Test]
        public void InputVisualizationCanvasPrefab_WhenInstantiated_HasRuntimeOverlayReferences()
        {
            // Verifies that stale prefab import artifacts do not leave runtime overlay references unassigned.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                InputVisualizationCanvas canvas = instance.GetComponent<InputVisualizationCanvas>();

                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.KeyboardOverlay, Is.Not.Null);
                Assert.That(canvas.MouseUiOverlay, Is.Not.Null);
                Assert.That(canvas.MouseInputOverlay, Is.Not.Null);
                Assert.That(canvas.RecordInputOverlayPresenter, Is.Not.Null);
                Assert.That(canvas.ReplayInputOverlay, Is.Not.Null);

                AssertSerializedReference(canvas.KeyboardOverlay, "_container");
                AssertSerializedReference(canvas.KeyboardOverlay, "_containerImage");
                AssertSerializedReference(canvas.MouseUiOverlay, "_canvasGroup");
                AssertSerializedReference(canvas.MouseUiOverlay, "_cursorGroup");
                AssertSerializedReference(canvas.MouseUiOverlay, "_circleImage");
                AssertSerializedReference(canvas.MouseUiOverlay, "_crosshairH");
                AssertSerializedReference(canvas.MouseUiOverlay, "_crosshairV");
                AssertSerializedReference(canvas.MouseUiOverlay, "_longPressText");
                AssertSerializedReference(canvas.MouseUiOverlay, "_dragStartMarker");
                AssertSerializedReference(canvas.MouseUiOverlay, "_circleSprite");
                AssertSerializedReference(canvas.MouseInputOverlay, "_leftButton");
                AssertSerializedReference(canvas.MouseInputOverlay, "_rightButton");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollWheel");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollArrowTop");
                AssertSerializedReference(canvas.MouseInputOverlay, "_scrollArrowBottom");
                AssertSerializedReference(canvas.MouseInputOverlay, "_moveDirectionGroup");
                AssertSerializedReference(canvas.RecordInputOverlayPresenter, "_view");

                RecordInputOverlayView recordView =
                    canvas.RecordInputOverlayPresenter.GetComponent<RecordInputOverlayView>();
                Assert.That(recordView, Is.Not.Null);
                AssertSerializedReference(recordView, "_canvasGroup");
                AssertSerializedReference(recordView, "_countdownGroup");
                AssertSerializedReference(recordView, "_countdownText");
                AssertSerializedReference(recordView, "_recordingGroup");
                AssertSerializedReference(recordView, "_recDotText");
                AssertSerializedReference(recordView, "_statusText");
                AssertSerializedReference(canvas.ReplayInputOverlay, "_statusText");
                AssertSerializedReference(canvas.ReplayInputOverlay, "_progressBarFill");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RuntimeOverlayPrefabs_WhenScanned_DoNotReferenceAssetsScripts()
        {
            // Verifies runtime package prefabs do not depend on project-only scripts.
            List<string> assetScriptReferences = new List<string>();

            for (int i = 0; i < RuntimeOverlayPrefabPaths.Length; i++)
            {
                CollectAssetScriptReferences(RuntimeOverlayPrefabPaths[i], assetScriptReferences);
            }

            Assert.That(assetScriptReferences, Is.Empty);
        }

        [Test]
        public void OverlayCanvasFactoryService_WhenPrefabHasMissingScript_ThrowsInvalidOperationException()
        {
            // Verifies overlay creation fails fast when prefab import state contains missing scripts.
            string fixtureFolderPath;
            string fixturePrefabPath = CreateMissingScriptPrefab(out fixtureFolderPath);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixturePrefabPath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.EqualTo(1));

                OverlayCanvasFactoryService service = new OverlayCanvasFactoryService(fixturePrefabPath);

                InvalidOperationException exception =
                    Assert.Throws<InvalidOperationException>(() => service.EnsureExists());

                Assert.That(exception.Message, Does.Contain("missing script"));
                Assert.That(exception.Message, Does.Contain(fixturePrefabPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(fixtureFolderPath);
            }
        }

        private static void AssertSerializedReference(UnityEngine.Object target, string propertyName)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);

            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.Not.Null, propertyName);
        }

        private static string ReadText(string relativePath)
        {
            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);

            return File.ReadAllText(absolutePath);
        }

        private static void CollectAssetScriptReferences(string prefabPath, List<string> assetScriptReferences)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);

            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                MonoBehaviour[] behaviours = transforms[i].GetComponents<MonoBehaviour>();
                for (int j = 0; j < behaviours.Length; j++)
                {
                    MonoBehaviour behaviour = behaviours[j];
                    if (behaviour == null)
                    {
                        continue;
                    }

                    MonoScript script = MonoScript.FromMonoBehaviour(behaviour);
                    Assert.That(script, Is.Not.Null, transforms[i].name);

                    string scriptPath = AssetDatabase.GetAssetPath(script);
                    if (scriptPath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        assetScriptReferences.Add(prefabPath + " -> " + transforms[i].name + " -> " + scriptPath);
                    }
                }
            }
        }

        private static string CreateMissingScriptPrefab(out string fixtureFolderPath)
        {
            string folderName = "GeneratedOverlayMissingScriptTest_" + Guid.NewGuid().ToString("N");
            fixtureFolderPath = "Assets/Tests/Editor/" + folderName;
            AssetDatabase.CreateFolder("Assets/Tests/Editor", folderName);

            string prefabPath = fixtureFolderPath + "/InputVisualizationCanvasMissingScript.prefab";
            File.WriteAllText(prefabPath, MissingScriptPrefabYaml);
            AssetDatabase.ImportAsset(prefabPath);
            return prefabPath;
        }

        private const string MissingScriptPrefabYaml =
            @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 200000}
  - component: {fileID: 300000}
  m_Layer: 0
  m_Name: InputVisualizationCanvasMissingScript
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &200000
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100000}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &300000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 100000}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}
  m_Name:
  m_EditorClassIdentifier:
";
    }
}
