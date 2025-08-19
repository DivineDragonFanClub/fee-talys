using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CustomMap.Editor
{
    public static class FBXifyMeshes
    {
        private const string MENU_ROOT = "Tools/FBXify Meshes/";
        private const string FBX_OUTPUT_ROOT = "FBXify";
        private const string LOG_PREFIX = "[FBXify]";
        
        // Simple logging helpers
        internal static void Log(string message) => Debug.Log($"{LOG_PREFIX} {message}");
        internal static void LogWarning(string message) => Debug.LogWarning($"{LOG_PREFIX} {message}");
        internal static void LogError(string message) => Debug.LogError($"{LOG_PREFIX} {message}");
        
        [MenuItem(MENU_ROOT + "Process Selected Prefabs")]
        public static void ProcessSelectedPrefabs()
        {
            var selectedPrefabs = GetSelectedPrefabs();
            
            if (selectedPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("FBXify Meshes", 
                    "No prefabs selected. Please select one or more prefabs in the Project window.", 
                    "OK");
                return;
            }
            
            if (!EditorUtility.DisplayDialog("FBXify Meshes", 
                $"Process {selectedPrefabs.Count} selected prefab(s)?", 
                "Process", "Cancel"))
            {
                return;
            }
            
            ProcessPrefabs(selectedPrefabs);
        }
        
        [MenuItem(MENU_ROOT + "Process All Prefabs in Folder")]
        public static void ProcessAllPrefabsInFolder()
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder with Prefabs", "Assets", "");
            
            if (string.IsNullOrEmpty(folderPath))
                return;
                
            // Convert absolute path to relative path
            if (folderPath.StartsWith(Application.dataPath))
            {
                folderPath = "Assets" + folderPath.Substring(Application.dataPath.Length);
            }
            
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            var prefabPaths = prefabGuids.Select(guid => AssetDatabase.GUIDToAssetPath(guid)).ToList();
            
            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("FBXify Meshes", 
                    "No prefabs found in the selected folder.", 
                    "OK");
                return;
            }
            
            if (!EditorUtility.DisplayDialog("FBXify Meshes", 
                $"Process {prefabPaths.Count} prefab(s) in folder?", 
                "Process", "Cancel"))
            {
                return;
            }
            
            ProcessPrefabs(prefabPaths);
        }
        
        [MenuItem("Assets/FBXify This Prefab", false, 1000)]
        public static void FBXifyContextMenu()
        {
            ProcessSelectedPrefabs();
        }
        
        [MenuItem("Assets/FBXify This Prefab", true)]
        public static bool FBXifyContextMenuValidation()
        {
            return GetSelectedPrefabs().Count > 0;
        }
        
        private static List<string> GetSelectedPrefabs()
        {
            var prefabs = new List<string>();
            
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    prefabs.Add(path);
                }
            }
            
            return prefabs;
        }
        
        private static void ProcessPrefabs(List<string> prefabPaths)
        {
            int processedCount = 0;
            int failedCount = 0;
            var failedPrefabs = new List<string>();
            
            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    string prefabPath = prefabPaths[i];
                    string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                    
                    float progress = (float)i / prefabPaths.Count;
                    if (EditorUtility.DisplayCancelableProgressBar("FBXify Meshes", 
                        $"Processing {prefabName} ({i + 1}/{prefabPaths.Count})", 
                        progress))
                    {
                        break;
                    }
                    
                    try
                    {
                        if (FBXifyMeshesProcessor.ProcessPrefab(prefabPath))
                        {
                            processedCount++;
                            Log($"Successfully processed: {prefabName}");
                        }
                        else
                        {
                            failedCount++;
                            failedPrefabs.Add(prefabName);
                            LogWarning($"Failed to process: {prefabName}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        failedCount++;
                        failedPrefabs.Add(prefabName);
                        LogError($"Error processing {prefabName}: {e.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();
            }
            
            // Show results
            string message = $"Processed {processedCount} prefab(s) successfully.";
            if (failedCount > 0)
            {
                message += $"\n{failedCount} prefab(s) failed:";
                foreach (var failed in failedPrefabs)
                {
                    message += $"\n  • {failed}";
                }
            }
            
            EditorUtility.DisplayDialog("FBXify Meshes Complete", message, "OK");
        }
        public static string GetIndividualFBXOutputPath(string prefabName, string gameObjectName)
        {
            // Keep FBX files inside Assets folder so Unity can import them
            string fbxFolder = Path.Combine(Application.dataPath, FBX_OUTPUT_ROOT, prefabName);
            
            if (!Directory.Exists(fbxFolder))
            {
                Directory.CreateDirectory(fbxFolder);
            }
            
            return Path.Combine(fbxFolder, $"{gameObjectName}.fbx");
        }
        
        public static string GetIndividualFBXAssetPath(string prefabName, string gameObjectName)
        {
            // Asset path relative to project root
            return Path.Combine("Assets", FBX_OUTPUT_ROOT, prefabName, $"{gameObjectName}.fbx");
        }
    }
}