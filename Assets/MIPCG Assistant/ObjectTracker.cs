using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class ObjectTracker : MonoBehaviour
{
    [FormerlySerializedAs("_isPlacedByAI")] [SerializeField] public bool _isPlacedByHuman;

    void OnDrawGizmos()
    {
        if (_isPlacedByHuman)
        {
            return;
        }
        Gizmos.color = Color.blue;
        
        Gizmos.DrawWireCube(transform.position,
            Vector3.one);
    }
}