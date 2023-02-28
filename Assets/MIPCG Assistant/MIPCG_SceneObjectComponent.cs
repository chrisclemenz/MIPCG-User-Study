using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

namespace MIPCG
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Grid))]
    public class MIPCG_SceneObjectComponent : MonoBehaviour
    {
        


        [SerializeField] private Grid grid;

        [SerializeField] [Range(1, 50)] public int width;
        [SerializeField] [Range(1, 50)] public int height;


        private Dictionary<Vector2Int, GameObject> _cellObjects;

        private Dictionary<Vector2Int, GameObject> _recommendationObjects;

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    Gizmos.color = Color.green;
                    Vector3Int offset = new Vector3Int(width / 2, 0, height / 2);
                    Gizmos.DrawWireCube(grid.CellToWorld(new Vector3Int(x, 0, y) - offset),
                        new Vector3(grid.cellSize.x, .0f, grid.cellSize.z));
                }
            }
        }

#if UNITY_EDITOR

        public Dictionary<Vector2Int, GameObject> GetChildrenWithPosition()
        {
            Dictionary<Vector2Int, GameObject> childDict = new Dictionary<Vector2Int, GameObject>();

            foreach (Transform child in transform)
            {
                if (child.GetComponent<ObjectTracker>()) continue;
                Vector3Int pos = grid.WorldToCell(child.position);
                //invert z
                pos.z = height - 1 - pos.z;
                pos += new Vector3Int(width / 2, 0, -height / 2);
                childDict.TryAdd(new Vector2Int(pos.x, pos.z), child.gameObject);
            }


            return childDict;
        }
        
        public Dictionary<Vector2Int, GameObject> GetAIPlacedChildrenWithPosition()
        {
            Dictionary<Vector2Int, GameObject> childDict = new Dictionary<Vector2Int, GameObject>();

            foreach (Transform child in transform)
            {
                if (!child.GetComponent<ObjectTracker>()) continue;
                Vector3Int pos = grid.WorldToCell(child.position);
                //invert z
                pos.z = height - 1 - pos.z;
                pos += new Vector3Int(width / 2, 0, -height / 2);
                childDict.TryAdd(new Vector2Int(pos.x, pos.z), child.gameObject);
            }


            return childDict;
        }

        public bool PlaceObjectAtID(Vector2Int id, GameObject asset, bool placedByHuman)
        {
            _cellObjects ??= new Dictionary<Vector2Int, GameObject>();
            _recommendationObjects ??= new Dictionary<Vector2Int, GameObject>();
            if (_cellObjects.ContainsKey(id))
            {
                DestroyImmediate(_cellObjects[id]);
                _cellObjects.Remove(id);
                // RemoveFromScene(id);
            }

            var obj = PrefabUtility.InstantiatePrefab(asset, transform) as GameObject;

            //get world position at id
            Vector3Int offset = new Vector3Int(width / 2, 0, height / 2);
            Vector3 worldPos = grid.GetCellCenterWorld(new Vector3Int(id.x, 0, height - 1 - id.y) - offset);
            worldPos -= grid.cellSize * 0.5f;

            if (!placedByHuman)
            {
                obj.transform.position = worldPos;
                obj.AddComponent<ObjectTracker>()._isPlacedByHuman = placedByHuman;
                obj.name = "AutoCompleted_" + obj.name;
                _recommendationObjects.TryAdd(id, obj);
            }
            else if (obj != null)
            {
                obj.transform.position = worldPos;
                _cellObjects[id] = obj;
                return true;
            }

            return false;
        }


        public void RemoveFromScene(Vector2Int id)
        {
            _cellObjects ??= new Dictionary<Vector2Int, GameObject>();
            if (!_cellObjects.ContainsKey(id)) return;
            GameObject oldObj = _cellObjects[id];
            _cellObjects.Remove(id);
            DestroyImmediate(oldObj);
        }

        public void Clear()
        {
            _cellObjects?.Clear();
            var tempList = transform.Cast<Transform>().ToList();
            foreach (var child in tempList)
            {
                DestroyImmediate(child.gameObject);
            }
        }

        public void ClearAutoCompletionObjects()
        {
            if (_recommendationObjects == null) return;
            foreach (var obj in _recommendationObjects)
            {
                DestroyImmediate(obj.Value);
            }

            _recommendationObjects.Clear();
        }

        public void Reinit()
        {
            _cellObjects = GetChildrenWithPosition();
            _recommendationObjects = GetAIPlacedChildrenWithPosition();
        }

#endif
        public GameObject GetObjectAt(Vector2Int id)
        {
            _cellObjects ??= new Dictionary<Vector2Int, GameObject>();
            if (_cellObjects.ContainsKey(id))
                return _cellObjects[id];
            return null;
        }

        
    }
}