using System;
using UnityEngine;
using UnityEditor;
using UnityObject = UnityEngine.Object;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Instantiates the InputVisualizationCanvas prefab and manages its lifecycle.
    /// <summary>
    /// Provides Overlay Canvas Factory operations for its owning module.
    /// </summary>
    internal sealed class OverlayCanvasFactoryService
    {
        internal const string CANVAS_PREFAB_PATH = "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";
        private const string ExistingCanvasSource = "existing InputVisualizationCanvas instance";

        private readonly string _canvasPrefabPath;
        private InputVisualizationCanvas _instance;

        public OverlayCanvasFactoryService(string canvasPrefabPath)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(canvasPrefabPath), "canvasPrefabPath must not be null or whitespace");
            _canvasPrefabPath = canvasPrefabPath;
        }

        public InputVisualizationCanvas VisualizationCanvas
        {
            get
            {
                EnsureExists();
                Debug.Assert(_instance != null, "InputVisualizationCanvas instance must exist after EnsureExists");
                return _instance!;
            }
        }

        public void Reset()
        {
            _instance = null;
        }

        public void EnsureExists()
        {
            if (_instance != null)
            {
                return;
            }

            // Domain Reload resets _instance but DontDestroyOnLoad objects survive; reclaim one and destroy duplicates
            InputVisualizationCanvas[] existing =
                UnityObject.FindObjectsByType<InputVisualizationCanvas>(FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                ValidateMissingScripts(existing[i].gameObject, ExistingCanvasSource);

                if (_instance == null)
                {
                    _instance = existing[i];
                }
                else
                {
                    UnityObject.DestroyImmediate(existing[i].gameObject);
                }
            }
            if (_instance != null)
            {
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_canvasPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException($"InputVisualizationCanvas prefab not found at {_canvasPrefabPath}");
            }

            ValidateMissingScripts(prefab, _canvasPrefabPath);

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ValidateMissingScripts(go, _canvasPrefabPath);
            UnityObject.DontDestroyOnLoad(go);
            _instance = go.GetComponent<InputVisualizationCanvas>();
            if (_instance == null)
            {
                UnityObject.DestroyImmediate(go);
                throw new InvalidOperationException(
                    $"InputVisualizationCanvas component not found on prefab at {_canvasPrefabPath}");
            }
        }

        private static void ValidateMissingScripts(GameObject root, string sourcePath)
        {
            Debug.Assert(root != null, "root must not be null");
            int missingScriptCount = CountMissingScripts(root);
            if (missingScriptCount == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"InputVisualizationCanvas prefab contains {missingScriptCount} missing script reference(s): {sourcePath}");
        }

        private static int CountMissingScripts(GameObject root)
        {
            int missingScriptCount = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[i].gameObject);
            }

            return missingScriptCount;
        }
    }

    /// <summary>
    /// Creates Overlay Canvas instances with the dependencies required by this module.
    /// </summary>
    internal static class OverlayCanvasFactory
    {
        private static readonly OverlayCanvasFactoryService ServiceValue =
            new OverlayCanvasFactoryService(OverlayCanvasFactoryService.CANVAS_PREFAB_PATH);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            ServiceValue.Reset();
        }

        public static InputVisualizationCanvas VisualizationCanvas => ServiceValue.VisualizationCanvas;

        public static void EnsureExists()
        {
            ServiceValue.EnsureExists();
        }
    }
}
