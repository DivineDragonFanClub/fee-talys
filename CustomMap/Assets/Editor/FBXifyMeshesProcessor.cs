using System.IO;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using static CustomMap.Editor.FBXifyMeshes;

namespace CustomMap.Editor
{
    public static class FBXifyMeshesProcessor
    {
        public static bool ProcessPrefab(string prefabPath)
        {
            // Load the prefab
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                LogError($"Failed to load prefab: {prefabPath}");
                return false;
            }
            
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            
            // Load prefab contents for editing
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                LogError($"Failed to load prefab contents: {prefabPath}");
                return false;
            }
            
            try
            {
                // Find all MeshFilters in the prefab
                MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                if (meshFilters.Length == 0)
                {
                    LogWarning($"No MeshFilters found in prefab: {prefabName}");
                    return false;
                }
                
                Log($"Found {meshFilters.Length} MeshFilters in {prefabName}");
                
                // Export each GameObject with a MeshFilter as a separate FBX
                int exportedCount = 0;
                foreach (var meshFilter in meshFilters)
                {
                    if (meshFilter.sharedMesh == null)
                    {
                        LogWarning($"Skipping {meshFilter.gameObject.name} - no mesh assigned");
                        continue;
                    }
                    
                    string gameObjectName = meshFilter.gameObject.name;
                    string fbxPath = GetIndividualFBXOutputPath(prefabName, gameObjectName);
                    
                    // Export this specific GameObject
                    string exportedPath = ModelExporter.ExportObject(fbxPath, meshFilter.gameObject);
                    if (string.IsNullOrEmpty(exportedPath))
                    {
                        LogError($"Failed to export GameObject: {gameObjectName}");
                        continue;
                    }
                    
                    Log($"Exported {gameObjectName} to: {exportedPath}");
                    exportedCount++;
                }
                
                if (exportedCount == 0)
                {
                    LogError($"No GameObjects were exported from {prefabName}");
                    return false;
                }
                
                // Force Unity to import all the FBX files
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
                
                // Update mesh references to point to the FBX files
                if (!UpdatePrefabMeshReferences(prefabRoot, prefabName, meshFilters))
                {
                    LogError($"Failed to update mesh references: {prefabName}");
                    return false;
                }
                
                // Save the updated prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Log($"Successfully processed {exportedCount} meshes in {prefabName}");
                
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
        
        private static bool UpdatePrefabMeshReferences(GameObject prefabRoot, string prefabName, MeshFilter[] meshFilters)
        {
            int updatedCount = 0;
            
            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;
                    
                string gameObjectName = meshFilter.gameObject.name;
                string fbxAssetPath = GetIndividualFBXAssetPath(prefabName, gameObjectName);
                
                // Check if the FBX file exists
                if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
                {
                    LogWarning($"FBX not found for {gameObjectName}, skipping");
                    continue;
                }
                
                // Load the FBX
                GameObject fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                if (fbxObject == null)
                {
                    LogWarning($"Failed to load FBX for {gameObjectName}");
                    continue;
                }
                
                // Get the mesh from the FBX
                // The FBX should contain the mesh as a sub-asset
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                Mesh fbxMesh = null;
                
                foreach (var asset in subAssets)
                {
                    if (asset is Mesh mesh)
                    {
                        fbxMesh = mesh;
                        Log($"Found mesh '{mesh.name}' in {gameObjectName}.fbx");
                        break;
                    }
                }
                
                if (fbxMesh != null)
                {
                    meshFilter.sharedMesh = fbxMesh;
                    updatedCount++;
                    Log($"Updated mesh reference: {gameObjectName} -> {fbxMesh.name}");
                }
                else
                {
                    LogWarning($"No mesh found in FBX for {gameObjectName}");
                }
            }
            
            Log($"Updated {updatedCount}/{meshFilters.Length} mesh references");
            return updatedCount > 0;
        }
    }
}