using System.Collections.Generic;
using UnityEngine;

namespace MIPCG_Assistant
{
    public class MIPCG_SceneSettingsComponent : MonoBehaviour
    {
        [SerializeField] private string assetFolderPath;

        [SerializeField] private string customPreviewFolderPath;

        [SerializeField] private string aiModelPath;
        [SerializeField] private string aiModelName;

        [SerializeField] private bool useSmoothing;
        [SerializeField] private bool useFallback;
        [SerializeField] private bool useDiagonalNeighborhoods;

        [SerializeField] private List<GameObject> uniqueAssets;


        // public string AssetFolderPath => assetFolderPath;
        //
        // public string CustomPreviewFolderPath => customPreviewFolderPath;
        //
        // public string AIModelPath => aiModelPath;
        //
        // public string AIModelName => aiModelName;
        //
        // public bool UseDiagonalNeighborhoods => useDiagonalNeighborhoods;
        //
        // public bool UseFallback => useFallback;
        //
        // public bool UseSmoothing => useSmoothing;
    }
}