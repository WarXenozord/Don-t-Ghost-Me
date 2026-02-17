using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapToggler : MonoBehaviour
{

    [SerializeField] private GameObject _map;
    [SerializeField] private KeyCode _toggleKey;


    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(_toggleKey)){
            _map.SetActive(!_map.activeInHierarchy);
        }
    }
}
