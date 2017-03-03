using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

class World : MonoBehaviour
{
    Terrain terrain;
    public Texture2D heightmap;
    public float detailLevel = 5.0f;
    public float minResolution = 2.0f;

    // Use this for initialization
    void Start()
    {
        terrain = new Terrain("Terrain", 256.0f, heightmap, this.gameObject.transform, detailLevel, minResolution);
    }

    // Update is called once per frame
    void Update()
    {
        terrain.detailLevel = detailLevel;
        terrain.minResolution = minResolution;
        //StartCoroutine(terrain.Update());
        terrain.Update();
    }
}