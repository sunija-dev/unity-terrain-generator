using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(TerrainGenerator))]
public class TerrainGeneratorEditor : Editor {


    public override void OnInspectorGUI()
    {
        TerrainGenerator terrainGeneratorScript = (TerrainGenerator)target;
        if (GUILayout.Button("Generate Terrain"))
        {
            terrainGeneratorScript.GenerateAllTerrains();
        }

        DrawDefaultInspector();
    }

}
