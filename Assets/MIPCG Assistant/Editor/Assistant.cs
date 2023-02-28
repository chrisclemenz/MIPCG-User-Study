using System.Collections.Generic;
using System.IO;
using System.Linq;
using MIPCG_Assistant;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MIPCG
{
    public enum SmoothingType
    {
        Softmax,
        Laplace,
        None
    };

    public class Assistant
    {
        // private const string SAVE_FILE_NAME = "SavedAIModel.json";

        private readonly Board _board;
        private readonly MIPCG_SceneSettingsComponent _sceneSettingsComponent;
        private readonly AssetManager _assetManager;
        private readonly List<MarkovModel> _models;
        private Dictionary<int, int> _assetDistribution;

        private string _lastLoadedJson = "";
        private bool _initializedFromSaveData;

        public Assistant(Board board, MIPCG_SceneSettingsComponent sceneSettingsComponent, AssetManager assetManager)
        {
            _board = board;
            _sceneSettingsComponent = sceneSettingsComponent;
            _assetManager = assetManager;
            _board.OnBoardChangeSingle += DistributionChanged;
            _board.OnBoardChange += DistributionChangedMultiple;
            _models = new List<MarkovModel>
            {
                //2d arrays initializer takes columns!
                //complete neighborhood
                new MarkovModel( board,
                    new bool[3, 3] { { true, true, true }, { true, false, true }, { true, true, true } }, "Complete 8",true),
                //cross
                new MarkovModel( board,
                    new bool[3, 3] { { false, true, false }, { true, false, true }, { false, true, false } }, "Cross"),
                //both diagonals
                new MarkovModel( board,
                    new bool[3, 3] { { true, false, true }, { false, false, false }, { true, false, true } },
                    "Both Diagonals",true),
                //corners
                //L-U
                new MarkovModel( board,
                    new bool[3, 3] { { false, true, false }, { true, false, false }, { false, false, false } },
                    "LU Corner"),
                //U-R
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { true, false, false }, { false, true, false } },
                    "UR Corner"),
                //R-D
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { false, false, true }, { false, true, false } },
                    "RD Corner"),
                //D-L
                new MarkovModel( board,
                    new bool[3, 3] { { false, true, false }, { false, false, true }, { false, false, false } },
                    "DL Corner"),
                //straight lines
                //UD
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { true, false, true }, { false, false, false } },
                    "Vertical Line"),
                //LR
                new MarkovModel( board,
                    new bool[3, 3] { { false, true, false }, { false, false, false }, { false, true, false } },
                    "Horizontal Line"),
                //single diagonals
                //UL-BR
                new MarkovModel( board,
                    new bool[3, 3] { { true, false, false }, { false, false, false }, { false, false, true } },
                    "UL-BR Diagonal",true),
                //BL-UR
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, true }, { false, false, false }, { true, false, false } },
                    "BL-UR Diagonal",true),
                //up
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { true, false, false }, { false, false, false } }, "Up"),
                //down
                new MarkovModel( board,
                    new bool[3, 3]
                        { { false, false, false }, { false, false, true }, { false, false, false } }, "Down"),
                //left
                new MarkovModel( board,
                    new bool[3, 3]
                        { { false, true, false }, { false, false, false }, { false, false, false } }, "Left"),
                //right
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { false, false, false }, { false, true, false } },
                    "Right"),
                //UL
                new MarkovModel( board,
                    new bool[3, 3] { { true, false, false }, { false, false, false }, { false, false, false } },
                    "UL",true),
                //UR
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { false, false, false }, { true, false, false } },
                    "UR",true),
                //BL
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, true }, { false, false, false }, { false, false, false } },
                    "BL",true),
                //BR
                new MarkovModel( board,
                    new bool[3, 3] { { false, false, false }, { false, false, false }, { false, false, true } },
                    "BR",true)
            };
            _assetDistribution = new Dictionary<int, int>();
            Load();
        }

        private void DistributionChangedMultiple(Dictionary<Vector2Int, int> beforeChanges,
            Dictionary<Vector2Int, int> afterChanges)
        {
            ReinitializeDistributionFromBoard();
        }

        private void DistributionChanged(Vector2Int pos, int oldID, int newID)
        {
            UpdateDistribution(oldID, -1);
            UpdateDistribution(newID, 1);
            // LogDistribution();
        }

        public Dictionary<int, float> GetRecommendationForNeighborhood(Vector2Int coords, SmoothingType smoothingType,
            bool useFallback)
        {
            Dictionary<int, int> modelStatistics = null;

            bool useDiagonals =  MIPCG_Editor.GetSettings().useDiagonalNeighborhoods;
            List<GameObject> uniqueAssets = MIPCG_Editor.GetSettings().uniqueAssets;
            foreach (var model in _models)
            {
                //only look for a recommendation if don't already have one
                if(!useDiagonals && model._isDiagonal) continue;
                modelStatistics ??= model.GetRecommendationForNeighborhood(coords);
                if(modelStatistics == null) continue;
                modelStatistics = new Dictionary<int, int>(modelStatistics);
                foreach (var unique in uniqueAssets)
                {
                    var id = _assetManager.GetPrefabIDForGameObject(unique);
                    if (_board.IsAssetPresent(id) && modelStatistics.ContainsKey(id))
                    {
                        modelStatistics.Remove(id);
                    }
                }

                if (modelStatistics.Count == 0) modelStatistics = null;
            }

            //if not found any then go to fallback

            Dictionary<int, float> probabilities = new Dictionary<int, float>();


            if (useFallback && (modelStatistics == null || modelStatistics.Count == 0))
                modelStatistics = _assetDistribution;
            if (modelStatistics == null || modelStatistics.Count == 0) return null;

            modelStatistics = new Dictionary<int, int>(modelStatistics);
            foreach (var unique in uniqueAssets)
            {
                var id = _assetManager.GetPrefabIDForGameObject(unique);
                if (_board.IsAssetPresent(id) && modelStatistics.ContainsKey(id))
                {
                    modelStatistics.Remove(id);
                }
            }
           
            if( modelStatistics.Count == 0) return null;
            // var copy = new Dictionary<int, float>(modelStatistics);
            var observationSum = modelStatistics.Values.Sum();
            var maxObservation = modelStatistics.Values.Max();
            var numOfDifferentObservations = modelStatistics.Count;
            switch (smoothingType)
            {
                case SmoothingType.Softmax:

                    var normalizationFactor = 0.0f;
                    foreach (var pair in modelStatistics)
                    {
                        //bring the observations into [0,1] to de-emphasize the most observed
                        float exp = Mathf.Exp(((float)pair.Value / maxObservation));
                        probabilities.Add(pair.Key, exp);
                        normalizationFactor += exp;
                    }

                    var copy = new Dictionary<int, float>(probabilities);
                    foreach (var pair in copy)
                    {
                        probabilities[pair.Key] = pair.Value / normalizationFactor;
                    }

                    break;

                case SmoothingType.Laplace:
                    const int alpha = 1;

                    foreach (var asset in modelStatistics)
                    {
                        probabilities.Add(asset.Key,
                            (float)(asset.Value + alpha) / (observationSum + alpha * numOfDifferentObservations));
                    }

                    break;

                case SmoothingType.None:
                    foreach (var asset in modelStatistics)
                    {
                        probabilities.Add(asset.Key, (float)asset.Value / observationSum);
                    }

                    break;
            }

            return probabilities;
        }
        
        public void Save()
        {
            var settings = MIPCG_Editor.GetSettings();
            var saveFolderPath = settings.aiModelPath;
            var saveFileName = settings.aiModelName + ".json";
            FileStream fileStream = File.Open(saveFolderPath + "/" + saveFileName, FileMode.Create);
            TextWriter textWriter = new StreamWriter(fileStream);
            foreach (var model in _models)
            {
                model.Serialize(textWriter);
            }

            textWriter.Write("Overall distribution:\n");
            var serializedDistribution = JsonConvert.SerializeObject(_assetDistribution);
            textWriter.Write(serializedDistribution);
            textWriter.Close();
            _lastLoadedJson = serializedDistribution;
            _initializedFromSaveData = true;
            AssetDatabase.Refresh();
        }

        public void Load()
        {
            var settings = MIPCG_Editor.GetSettings();
            var saveFolderPath = settings.aiModelPath;
            var saveFileName = settings.aiModelName + ".json";
            if (!File.Exists(saveFolderPath + "/" + saveFileName)) return;
            var lines = File.ReadAllLines(saveFolderPath + "/" + saveFileName);
            for (int lineNumber = 1, modelNumber = 0; modelNumber < _models.Count; lineNumber += 2, modelNumber++)
            {
                _models[modelNumber].Deserialize(lines[lineNumber]);
            }

            //load general distribution
            //use index from end expression i.e. "^1" ... neat
            var serializedDistribution = lines[^1];
            _assetDistribution = JsonConvert.DeserializeObject<Dictionary<int, int>>(serializedDistribution);
            _lastLoadedJson = serializedDistribution;
            _initializedFromSaveData = true;
        }

        public void Reset()
        {
            var settings = MIPCG_Editor.GetSettings();
            var saveFolderPath = settings.aiModelPath;
            var saveFileName = settings.aiModelName + ".json";
            foreach (var model in _models)
            {
                model.Reset();
            }

            AssetDatabase.DeleteAsset(saveFolderPath + "/" + saveFileName);
            _assetDistribution.Clear();
            _lastLoadedJson = "";
            _initializedFromSaveData = false;
            //load distribuiton from board again
            ReinitializeDistributionFromBoard();
        }

        private void ReinitializeDistributionFromBoard()
        {
            if (_initializedFromSaveData)
            {
                _assetDistribution = JsonConvert.DeserializeObject<Dictionary<int, int>>(_lastLoadedJson);
            }
            else
            {
                _assetDistribution = new Dictionary<int, int>();
            }

            for (int x = 0; x < _board.Width + 1; x++)
            {
                for (int y = 0; y < _board.Height + 1; y++)
                {
                    UpdateDistribution(_board.At(new Vector2Int(x, y)), 1);
                }
            }
        }

        private void UpdateDistribution(int id, int valueChange)
        {
            if (id < 0) return;
            _assetDistribution.TryAdd(id, 0);
            _assetDistribution[id] += valueChange;
            if (_assetDistribution[id] <= 0) _assetDistribution.Remove(id);
        }
    }
}