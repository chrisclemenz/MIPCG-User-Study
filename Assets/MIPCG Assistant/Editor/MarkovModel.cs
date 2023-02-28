using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Serialization;
using Unity.Plastic.Newtonsoft.Json;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;

namespace MIPCG
{
    using AssetCountMap = Dictionary<int, int>;
    
    public class MarkovModel
    {
        private readonly string _neighborhoodDescription;

        public readonly bool _isDiagonal;
        private Dictionary<string, AssetCountMap> _statistics;
        private string _lastLoadedJson = "";
        private bool _initializedFromSaveData;
        
        private readonly Board _board;
        private readonly bool[,] _mask;

        public MarkovModel( Board board, bool[,] mask, string neighborhoodDescription, bool isDiagonal = false)
        {
            _neighborhoodDescription = neighborhoodDescription;
            _isDiagonal = isDiagonal;
            Assert.AreEqual(9, mask.Length);
            Assert.AreEqual(3, mask.GetLength(0));
            Assert.AreEqual(3, mask.GetLength(1));
            _mask = mask;
            _board = board;
            _board.OnBoardChange += BoardChanged;
            _board.OnBoardChangeSingle += UpdateWindow;
            _statistics = new Dictionary<string, AssetCountMap>();
            ReinitializeStatisticsFromBoard();
        }

        private void BoardChanged(Dictionary<Vector2Int, int> beforechanges, Dictionary<Vector2Int, int> afterchanges)
        {
            ReinitializeStatisticsFromBoard();
        }

        private bool TryChangeStatisticsAdd(string key, int ID)
        {
            if (key == "" || key.Contains("-1"))
            {
                return false;
            }

            _statistics.TryAdd(key, new AssetCountMap());
            _statistics[key].TryAdd(ID, 0);
            _statistics[key][ID]++;
            return true;
        }

        private bool TryChangeStatisticsSubtract(string key, int ID)
        {
            if (ID < 0) return false;
            if (key == "" || key.Contains("-1") || !_statistics.ContainsKey(key) || !_statistics[key].ContainsKey(ID))
            {
                return false;
            }

            Assert.IsTrue(ID >= 0, "ID not >0");
            Assert.IsTrue(_statistics.ContainsKey(key));
            Assert.IsTrue(_statistics[key].ContainsKey(ID));
            _statistics[key][ID]--;
            if (_statistics[key][ID] <= 0) _statistics[key].Remove(ID);
            if (_statistics[key].Count <= 0) _statistics.Remove(key);
            return true;
        }

        private void ReinitializeStatisticsFromBoard()
        {
            //reset current statistics and reinitialize it either from zero or saved data
            if (_initializedFromSaveData)
            {
                Deserialize(_lastLoadedJson);
            }
            else
            {
                _statistics = new Dictionary<string, AssetCountMap>();
            }

            //Update Probabilities from board state
            for (int x = 0; x < _board.Width + 1; x++)
            {
                for (int y = 0; y < _board.Height + 1; y++)
                {
                    var cellObject = _board.At(new Vector2Int(x, y));
                    //if cell is empty continue
                    if (cellObject == -1) continue;
                    if (!_board.HasNeighbors(new Vector2Int(x, y))) continue;
                    //get all neighbors of cell
                    var neighborhood = _board.GetNeighborhood(new Vector2Int(x, y),false);
                    var key = GetMaskedKey(neighborhood);
                    if (key == "" || key.Contains("-1"))
                    {
                        continue;
                    }

                    _statistics.TryAdd(key, new AssetCountMap());
                    _statistics[key].TryAdd(cellObject, 0);
                    _statistics[key][cellObject]++;
                }
            }
        }

        //When one single position changes, move a 3x3 window over its neighborhood and update it including itself (the center)
        //by decreasing every neighborhood before the change happened and increase it after change happened
        private void UpdateWindow(Vector2Int center, int oldID, int newID)
        {
            if (oldID == newID) return;
            Vector2Int[] windowOffsets =
            {
                new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
                new Vector2Int(-1, 0), new Vector2Int(1, 0),
                new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1)
            };

            //move window over all offsets
            foreach (var offset in windowOffsets)
            {
                var neighbor = _board.At(center + offset);
                //if this neighbor in the window is empty and is not the changed position (i.e. the center of the window)
                //then there is no need to do anything
                if (neighbor == -1) continue;

                //get new neighborhood and increase by one
                var currentNeighborsNeighborhood = _board.GetNeighborhood(center + offset,false);
                var newKey = GetMaskedKey(currentNeighborsNeighborhood);
                TryChangeStatisticsAdd(newKey, neighbor);

                //this neighborhood has already changed before we receive the callback
                //so we have to set the index on the position relative to its neighbor to the old value
                var oldNeighborsNeighborhood = _board.GetNeighborhood(center + offset,false);
                oldNeighborsNeighborhood[1 - offset.x, 1 - offset.y] = oldID;
                var oldKey = GetMaskedKey(oldNeighborsNeighborhood);
                TryChangeStatisticsSubtract(oldKey, neighbor);
            }

            // handle center that changed separate
            var nbh = _board.GetNeighborhood(center,false);
            var key = GetMaskedKey(nbh);
            if (oldID >= 0)
                TryChangeStatisticsSubtract(key, oldID);
            if (newID >= 0)
                TryChangeStatisticsAdd(key, newID);
        }

        public Dictionary<int, int> GetRecommendationForNeighborhood(Vector2Int coords)
        {
            var neighborhood = _board.GetNeighborhood(coords,true);
            var key = GetMaskedKey(neighborhood);
            if (!_statistics.ContainsKey(key))
            {
                return null;
            }

            return _statistics[key];

        }

        private string GetMaskedKey(int[,] neighborhood)
        {
            Assert.AreEqual(9, neighborhood.Length);
            Assert.AreEqual(3, neighborhood.GetLength(0));
            Assert.AreEqual(3, neighborhood.GetLength(1));

            var key = "";
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    if (x == 1 && y == 1) continue;
                    if (!_mask[x, y]) continue;
                    if (key != "")
                    {
                        key += "|";
                    }

                    key += neighborhood[x, y];
                }
            }

            return key;
        }
        
        public void Serialize(TextWriter textWriter)
        {
            textWriter.Write(_neighborhoodDescription + ", Mask:" + JsonConvert.SerializeObject(_mask) + "\n");
            string serializedStatistics = JsonConvert.SerializeObject(_statistics);
            textWriter.Write(serializedStatistics + "\n");
            _lastLoadedJson = serializedStatistics;
            _initializedFromSaveData = true;

        }

        public void Deserialize(string text)
        {
            _statistics = JsonConvert.DeserializeObject<Dictionary<string, AssetCountMap>>(text);
            _lastLoadedJson = text;
            _initializedFromSaveData = true;
        }

        public void Reset()
        {
            _lastLoadedJson = "";
            _initializedFromSaveData = false;
            ReinitializeStatisticsFromBoard();
        }
    }
}