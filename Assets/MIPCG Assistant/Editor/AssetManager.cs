using System.Collections.Generic;
using System.Linq;
using MIPCG;
using MIPCG_Assistant;
using UnityEditor;
using UnityEngine;

namespace MIPCG
{
    public class AssetManager
    {
        private readonly MIPCG_SceneSettingsComponent _sceneSettingsComponent;
        private Dictionary<GameObject, int> _assets;
        private Dictionary<int, Texture2D> _assetsIcons;
        private List<Texture2D> _assetsIconList;
        private Dictionary<int, Texture2D> _assetPreviewList;

        public List<GameObject> Assets => _assets.Keys.ToList();

        public AssetManager(MIPCG_SceneSettingsComponent sceneSettingsComponent)
        {
            _sceneSettingsComponent = sceneSettingsComponent;
            LoadAssets();
            LoadAssetIcons();
        }

        private void LoadAssetIcons()
        {
            var previewPath = MIPCG_Editor.GetSettings().customPreviewFolderPath;
            _assetsIcons = new Dictionary<int, Texture2D>();
            var iconsGUIDs = AssetDatabase.FindAssets("t:Texture2D", new[] { previewPath });
            int currentId = 0;
            foreach (var guid in iconsGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                _assetsIcons.Add(currentId, asset);
                currentId++;
            }

            _assetsIconList = _assetsIcons.Values.ToList();
        }

        private void LoadAssets()
        {
            var assetPath =  MIPCG_Editor.GetSettings().assetFolderPath;
            var allObjectGuids = AssetDatabase.FindAssets("t:Prefab", new[] { assetPath });
            _assets = new Dictionary<GameObject, int>();
            int currentId = 0;
            foreach (var guid in allObjectGuids)
            {
                _assets.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)), currentId);
                currentId++;
            }
        }

        public List<Texture2D> GetAssetPreviews()
        {
            List<Texture2D> previews;
            if (_assetsIcons.Count == 0)
            {
                AssetPreview.SetPreviewTextureCacheSize(1024);
                previews = new List<Texture2D>();
                if (AssetPreview.IsLoadingAssetPreviews()) return null;
                foreach (var asset in _assets)
                {
                    previews.Add(AssetPreview.GetAssetPreview(asset.Key));
                }
            }
            else
            {
                return _assetsIconList;
            }

            return previews;
        }

        public int GetPrefabIDForGameObject(GameObject gameObject)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(gameObject);
            return _assets[prefab];
        }
    }
}