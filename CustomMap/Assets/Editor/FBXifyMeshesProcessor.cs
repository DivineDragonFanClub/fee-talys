using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;

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
                Debug.LogError($"[FBXify] Failed to load prefab: {prefabPath}");
                return false;
            }
            
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            string fbxPath = FBXifyMeshes.GetFBXOutputPath(prefabName);
            
            // Export to FBX using Unity's FBX Exporter
            string exportedPath = ModelExporter.ExportObject(fbxPath, prefab);
            if (string.IsNullOrEmpty(exportedPath))
            {
                Debug.LogError($"[FBXify] Failed to export FBX: {prefabName}");
                return false;
            }
            Debug.Log($"[FBXify] Exported FBX to: {exportedPath}");
            
            // Import the FBX back into Unity
            string fbxAssetPath = FBXifyMeshes.GetFBXAssetPath(prefabName);
            
            // Force Unity to import the FBX file
            AssetDatabase.Refresh();
            AssetDatabase.ImportAsset(fbxAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            
            // Wait for import to complete
            AssetDatabase.SaveAssets();
            
            // Update mesh references in the original prefab
            if (!UpdatePrefabMeshReferences(prefabPath, fbxAssetPath))
            {
                Debug.LogError($"[FBXify] Failed to update mesh references: {prefabName}");
                return false;
            }
            
            return true;
        }
        
        private static bool UpdatePrefabMeshReferences(string prefabPath, string fbxAssetPath)
        {
            // Ensure the FBX exists in the asset database
            if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
            {
                Debug.LogError($"[FBXify] FBX file does not exist at path: {fbxAssetPath}");
                return false;
            }
            
            // Load the FBX as a model
            GameObject fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
            if (fbxModel == null)
            {
                Debug.LogError($"[FBXify] Failed to load imported FBX: {fbxAssetPath}. Trying to refresh asset database...");
                AssetDatabase.Refresh();
                fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                
                if (fbxModel == null)
                {
                    Debug.LogError($"[FBXify] Still cannot load FBX after refresh: {fbxAssetPath}");
                    return false;
                }
            }
            
            // Get all meshes from the FBX
            var fbxMeshes = new Dictionary<string, Mesh>();
            CollectMeshesFromFBX(fbxModel, fbxMeshes, fbxAssetPath);
            
            // Load and update the prefab
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[FBXify] Failed to load prefab for updating: {prefabPath}");
                return false;
            }
            
            try
            {
                // Update all MeshFilter components in the prefab
                MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                int updatedCount = 0;
                
                // First, let's see what meshes are available
                if (fbxMeshes.Count == 0)
                {
                    Debug.LogWarning($"[FBXify] No meshes found in FBX file!");
                }
                else
                {
                    Debug.Log($"[FBXify] Found {fbxMeshes.Count} meshes in FBX: {string.Join(", ", fbxMeshes.Keys)}");
                }
                
                foreach (var meshFilter in meshFilters)
                {
                    if (meshFilter.sharedMesh != null)
                    {
                        string originalMeshName = meshFilter.sharedMesh.name;
                        string gameObjectName = meshFilter.gameObject.name;
                        
                        // Unity's FBX Exporter typically uses the GameObject name for the mesh
                        // Let's try to find a mesh that matches
                        Mesh fbxMesh = null;
                        string matchedName = null;
                        
                        // First try exact GameObject name match
                        if (fbxMeshes.TryGetValue(gameObjectName, out fbxMesh))
                        {
                            matchedName = gameObjectName;
                        }
                        // If there's only one mesh and we have many MeshFilters, they might all share it
                        else if (fbxMeshes.Count == 1)
                        {
                            var singleMesh = fbxMeshes.First();
                            fbxMesh = singleMesh.Value;
                            matchedName = singleMesh.Key;
                            Debug.Log($"[FBXify] Using single mesh '{matchedName}' for GameObject '{gameObjectName}'");
                        }
                        // Try to find any mesh that contains the GameObject name
                        else
                        {
                            foreach (var kvp in fbxMeshes)
                            {
                                if (kvp.Key.Contains(gameObjectName) || gameObjectName.Contains(kvp.Key))
                                {
                                    fbxMesh = kvp.Value;
                                    matchedName = kvp.Key;
                                    break;
                                }
                            }
                        }
                        
                        if (fbxMesh != null)
                        {
                            meshFilter.sharedMesh = fbxMesh;
                            updatedCount++;
                            Debug.Log($"[FBXify] Updated: {gameObjectName} -> {matchedName} (mesh ID: {fbxMesh.GetInstanceID()})");
                        }
                        else
                        {
                            Debug.LogWarning($"[FBXify] No match for: {gameObjectName} (original: {originalMeshName})");
                        }
                    }
                }
                
                // Save the updated prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Debug.Log($"[FBXify] Updated {updatedCount} mesh references in prefab: {Path.GetFileNameWithoutExtension(prefabPath)}");
                
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
        
        private static void CollectMeshesFromFBX(GameObject fbxObject, Dictionary<string, Mesh> meshDict, string fbxAssetPath)
        {
            // Get all sub-assets of type Mesh from the FBX
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
            
            foreach (var asset in subAssets)
            {
                if (asset is Mesh mesh)
                {
                    string meshName = mesh.name;
                    if (!meshDict.ContainsKey(meshName))
                    {
                        meshDict[meshName] = mesh;
                        Debug.Log($"[FBXify] Found mesh in FBX: {meshName}");
                    }
                }
            }
            
            // Also collect meshes from MeshFilters in the FBX GameObject hierarchy
            MeshFilter[] meshFilters = fbxObject.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null)
                {
                    string objectName = mf.gameObject.name;
                    if (!meshDict.ContainsKey(objectName))
                    {
                        meshDict[objectName] = mf.sharedMesh;
                        Debug.Log($"[FBXify] Found mesh via MeshFilter: {objectName} -> {mf.sharedMesh.name}");
                    }
                }
            }
        }
    }
}