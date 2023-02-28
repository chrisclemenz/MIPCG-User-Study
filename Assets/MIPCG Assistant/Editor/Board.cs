using System;
using System.Collections.Generic;
using System.IO;
using MIPCG;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MIPCG
{
    public class Board
    {
        private int[,] _boardState;
        private int _width;
        public int Width => _width;
        private int _height;
        private readonly MIPCG_SceneObjectComponent _mipcgSceneObjectComponent;
        private readonly AssetManager _assetManager;
        private bool _ignoreNextHierarchyChange;
        private bool _subscribedToSceneChange;

        public int Height => _height;

        
        public Dictionary<Vector2Int, int> RecommendationBuffer { get; }




        public delegate void BoardChangeSingle(Vector2Int pos, int oldID, int newID);

        public delegate void BoardChangeMultiple(Dictionary<Vector2Int,int> beforeChanges, Dictionary<Vector2Int,int> afterChanges);

        public event BoardChangeSingle OnBoardChangeSingle;
        public event BoardChangeMultiple OnBoardChange;

        public Board(MIPCG_SceneObjectComponent mipcgSceneObjectComponent, AssetManager assetManager)
        {
            RecommendationBuffer = new Dictionary<Vector2Int, int>();
            _mipcgSceneObjectComponent = mipcgSceneObjectComponent;
            
            _assetManager = assetManager;
            _width = mipcgSceneObjectComponent.width;
            _height = mipcgSceneObjectComponent.height;
            _boardState = new int[_width, _height];
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    //-1 == empty
                    _boardState[x, y] = -1;
                }
            }

            Synchronize();

            EditorApplication.hierarchyChanged += Synchronize;
            if(!_subscribedToSceneChange)
            {
                _subscribedToSceneChange = true;
                EditorSceneManager.sceneClosing +=
                    (scene, removingScene) => EditorApplication.hierarchyChanged -= Synchronize;
            }
        }

        public void GridSizeChanged(int newWidth, int newHeight)
        {
            _height = newHeight;
            _width = newWidth;
            _boardState = new int[_width, _height];
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    //-1 == empty
                    _boardState[x, y] = -1;
                }
            }
            ClearRecommendations();
            Synchronize();
        }

        public int[,] GetNeighborhood(Vector2Int coords, bool useRecommendations)
        {
            var x = coords.x;
            var y = coords.y;

            int[,] neighbors = new int[3, 3];

            neighbors[0, 0] = At(new Vector2Int(x - 1, y - 1));
            neighbors[1, 0] = At(new Vector2Int(x, y - 1));
            neighbors[2, 0] = At(new Vector2Int(x + 1, y - 1));
            neighbors[0, 1] = At(new Vector2Int(x - 1, y));
            neighbors[1, 1] = At(new Vector2Int(x, y));
            neighbors[2, 1] = At(new Vector2Int(x + 1, y));
            neighbors[0, 2] = At(new Vector2Int(x - 1, y + 1));
            neighbors[1, 2] = At(new Vector2Int(x, y + 1));
            neighbors[2, 2] = At(new Vector2Int(x + 1, y + 1));

            
            if (useRecommendations)
            {
                //check if there are recommendations active to check as well, if so override neighborhood
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x - 1, y - 1)))
                    neighbors[0, 0] = RecommendationBuffer[new Vector2Int(x - 1, y - 1)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x, y - 1)))
                    neighbors[1, 0] = RecommendationBuffer[new Vector2Int(x, y - 1)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x + 1, y - 1)))
                    neighbors[2, 0] = RecommendationBuffer[new Vector2Int(x + 1, y - 1)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x - 1, y)))
                    neighbors[0, 1] = RecommendationBuffer[new Vector2Int(x - 1, y)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x, y)))
                    neighbors[1, 1] = RecommendationBuffer[new Vector2Int(x, y)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x + 1, y)))
                    neighbors[2, 1] = RecommendationBuffer[new Vector2Int(x + 1, y)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x - 1, y + 1)))
                    neighbors[0, 2] = RecommendationBuffer[new Vector2Int(x - 1, y + 1)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x, y + 1)))
                    neighbors[1, 2] = RecommendationBuffer[new Vector2Int(x, y + 1)];
                
                if (RecommendationBuffer.ContainsKey(new Vector2Int(x + 1, y + 1)))
                    neighbors[2, 2] = RecommendationBuffer[new Vector2Int(x + 1, y + 1)];
            }

            return neighbors;
        }

        public void PlaceObject(Vector2Int coords, int id, bool placedByHuman)
        {
            if(IsOutsideBoard(coords)) return;

            _mipcgSceneObjectComponent.PlaceObjectAtID(coords, _assetManager.Assets[id],placedByHuman);
            _ignoreNextHierarchyChange = true;
            int oldID = _boardState[coords.x, coords.y];
            _boardState[coords.x, coords.y] = id;
            
            //only track distribution from human input
            if(placedByHuman)
            {
                // UpdateDistribution(oldID, -1);
                // UpdateDistribution(id, 1);
                OnBoardChangeSingle?.Invoke(coords, oldID, id);
            }
        }
        
        
        
        public void PlaceMultiple(List<Vector2Int> cells, int id, bool placedByHuman)
        {
            Dictionary<Vector2Int, int> oldState = new Dictionary<Vector2Int, int>();
            Dictionary<Vector2Int, int> newState = new Dictionary<Vector2Int, int>();
            foreach (var cell in cells)
            {
                _mipcgSceneObjectComponent.PlaceObjectAtID(cell, _assetManager.Assets[id],placedByHuman);
                _ignoreNextHierarchyChange = true;
                int oldID = _boardState[cell.x, cell.y];
                _boardState[cell.x, cell.y] = id;
                oldState.Add(cell,oldID);
                newState.Add(cell,id);
                // UpdateDistribution(oldID, -1);
            }

            // UpdateDistribution(id, cells.Count);
            OnBoardChange?.Invoke(oldState,newState);
        }


        public int At(Vector2Int coords)
        {
            if (IsOutsideBoard(coords))
                return -1;
            return _boardState[coords.x, coords.y];
        }

        public bool HasNeighbors(Vector2Int coords)
        {
            var x = coords.x;
            var y = coords.y;

            // var bordWithBorder = GetBoardWithBorder();
            for (int dx = -1; dx < 2; dx++)
            {
                for (int dy = -1; dy < 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    if (At(new Vector2Int(x + dx, y + dy)) != -1) return true;
                }
            }

            return false;
        }

        //completely rebuild _boardState from scene data
        private void Synchronize()
        {
            if (_ignoreNextHierarchyChange)
            {
                _ignoreNextHierarchyChange = false;
                return;
            }
            
            Dictionary<Vector2Int, int> oldState = new Dictionary<Vector2Int, int>();
            Dictionary<Vector2Int, int> newState = new Dictionary<Vector2Int, int>();
            var childDict = _mipcgSceneObjectComponent.GetChildrenWithPosition();
            for (int x = 0; x < _width; x++)
            {
                for (int y = 0; y < _height; y++)
                {
                    if (childDict.ContainsKey(new Vector2Int(x, y)))
                    {
                        var id = _assetManager.GetPrefabIDForGameObject(childDict[new Vector2Int(x, y)]);
                        oldState.Add(new Vector2Int(x,y),_boardState[x, y]);
                        _boardState[x, y] = id;
                        newState.Add(new Vector2Int(x,y), id);
                        if (!MIPCG_Editor.GetSettings().uniqueAssets.Contains(_assetManager.Assets[id]))
                        {
                        }
                    }
                    else
                    {
                        oldState.Add(new Vector2Int(x,y),_boardState[x, y]);
                        _boardState[x, y] = -1;
                        newState.Add(new Vector2Int(x,y), -1);
                    }
                }
            }
            
            var AiPlacedChildren = _mipcgSceneObjectComponent.GetAIPlacedChildrenWithPosition();
            RecommendationBuffer.Clear();
            foreach (var child in AiPlacedChildren)
            {
                RecommendationBuffer.Add(child.Key,_assetManager.GetPrefabIDForGameObject(child.Value));
            }
            

            OnBoardChange?.Invoke(oldState,newState);
        }

        public void Clear()
        {
            Dictionary<Vector2Int, int> oldState = new Dictionary<Vector2Int, int>();
            Dictionary<Vector2Int, int> newState = new Dictionary<Vector2Int, int>();
            for (int x = 0; x < _boardState.GetLength(0); x++)
            {
                for (int y = 0; y < _boardState.GetLength(1); y++)
                {
                    oldState.Add(new Vector2Int(x,y),_boardState[x, y]);
                    _boardState[x, y] = -1;
                    newState.Add(new Vector2Int(x,y), -1);
                }
            }

            OnBoardChange?.Invoke(oldState,newState);
        }

       

        public void RemoveObjectFromScene(Vector2Int cell)
        {
            _mipcgSceneObjectComponent.RemoveFromScene(cell);
            int oldID = _boardState[cell.x, cell.y];
            // UpdateDistribution(oldID, -1);
            _boardState[cell.x, cell.y] = -1;
            _ignoreNextHierarchyChange = true;
            OnBoardChangeSingle?.Invoke(cell, oldID, -1);
        }

        public void RemoveMultipleObjectsFromScene(List<Vector2Int> cells)
        {
            Dictionary<Vector2Int, int> oldState = new Dictionary<Vector2Int, int>();
            Dictionary<Vector2Int, int> newState = new Dictionary<Vector2Int, int>();
            foreach (var cell in cells)
            {
                _mipcgSceneObjectComponent.RemoveFromScene(cell);
                int oldID = _boardState[cell.x, cell.y];
                // UpdateDistribution(oldID, -1);
                oldState.Add(cell, oldID);
                newState.Add(cell, -1);
                _boardState[cell.x, cell.y] = -1;
            }

            OnBoardChange?.Invoke(oldState,newState);
        }

        public List<Vector2Int> FloodFill(Vector2Int startingCell)
        {
            
        if( IsOutsideBoard(startingCell) ) return new List<Vector2Int>();
        var startingAssetIndex = At(startingCell);
        List<Vector2Int> visited = new List<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(startingCell);
        visited.Add(startingCell);

        while (queue.Count > 0)
        {
            if (queue.TryDequeue(out var currentCoords))
            {
                //add 4 neighbors to queue

                var up = currentCoords + new Vector2Int(0, 1);
                var down = currentCoords + new Vector2Int(0, -1);
                var left = currentCoords + new Vector2Int(-1, 0);
                var right = currentCoords + new Vector2Int(1, 0);

                if (!visited.Contains(left) && !IsOutsideBoard(left) && At(left) == startingAssetIndex)
                {
                    queue.Enqueue(left);
                    visited.Add(left);
                }

                if (!visited.Contains(right) && !IsOutsideBoard(right) && At(right) == startingAssetIndex)
                {
                    queue.Enqueue(right);
                    visited.Add(right);
                }

                if (!visited.Contains(up) && !IsOutsideBoard(up) && At(up) == startingAssetIndex)
                {
                    queue.Enqueue(up);
                    visited.Add(up);
                }

                if (!visited.Contains(down) && !IsOutsideBoard(down) &&At(down) == startingAssetIndex)
                {
                    queue.Enqueue(down);
                    visited.Add(down);
                }
            }
        }
        
        return visited;
        }

        private bool IsOutsideBoard(Vector2Int cell)
        {
            return
                cell.x > _boardState.GetLength(0) - 1 ||
                cell.y > _boardState.GetLength(1) - 1 ||
                cell.x < 0 || cell.y < 0;
        }
        
        
        public void ClearRecommendations()
        {
            if (RecommendationBuffer == null || RecommendationBuffer.Count == 0) return;
            _mipcgSceneObjectComponent.ClearAutoCompletionObjects();
            _ignoreNextHierarchyChange = true;
            RecommendationBuffer.Clear();
            // Synchronize();
        }

        public void AcceptRecommendation()
        {
            if (RecommendationBuffer == null || RecommendationBuffer.Count == 0) return;
            _mipcgSceneObjectComponent.ClearAutoCompletionObjects();
            foreach (var recommendation in RecommendationBuffer)
            {
                PlaceObject(recommendation.Key, recommendation.Value, true);
            }

            RecommendationBuffer.Clear();
        }

        public void AddToRecommendations(Vector2Int coords, int recommendedID)
        {
            if (RecommendationBuffer.ContainsKey(coords) || recommendedID == -1) return;

            RecommendationBuffer.Add(coords, recommendedID);
            _mipcgSceneObjectComponent.PlaceObjectAtID(coords, _assetManager.Assets[recommendedID],false);
            _ignoreNextHierarchyChange = true;
        }

        public string BoardToString()
        {
            string boardString = "Board placed by Designer:\n";
            for (int y = 0; y < _height;y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    boardString +=_boardState[x, y]+" ";
                }

                boardString+="\n";
            }

            boardString += "Board auto-completed by AI\n";
            for (int y = 0; y < _height;y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var coords = new Vector2Int(x, y);
                    if (RecommendationBuffer.ContainsKey(coords))
                        boardString += RecommendationBuffer[coords] + " ";
                    else
                        boardString += "-1 ";
                }

                boardString+="\n";
            }
            return boardString;

            
            
        }

        public bool IsAssetPresent(int id)
        {
            foreach (var cell in _boardState)
            {
                if (cell == id) return true;
            }

            return RecommendationBuffer.ContainsValue(id);
        }
    }
}