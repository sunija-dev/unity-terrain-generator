using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/*
 * To use this script, create a terrain object and copy it. 
 * E.g. for a 3x3 terrain you'll need 9 terrain objects in total.
 * 
 * It's easiest to create a terrain asset in the Project inspector and duplicate it.
 * */


public class TerrainGenerator : MonoBehaviour {

    public float worldScale = 6000;
    public float worldDepthDivider = 1000f;
    public float mapSize = 10000; // the actual 3d size of the terrain tiles

    public int resolution = 257;
    public int distantResolution = 33; // has to be higher than 32

    public float worldOffsetX = 0;
    public float worldOffsetY = 0;

    public int highResDistanceChunks = 2;

    public GameObject trackingObject;

    [Header("Adaptive Calculation")]
    public bool useCalculationLimit = true;
    public int calculatedHeightsPerFrame = 10000;
    public bool useAdaptiveCalcLimit = true;
    public float keepFPSAbove = 60;
    public int adjustCalculationBy = 1000;
    public int minCalculationsPerFrame = 3000;

    [Header("Noise Iterations")]
    public TerrainIteration[] iterations;
    public Terrain[] terrains;

    private int maxTerrainDepth = 0;
    private int terrainsInLine = 3;
    private int currCalculatedHeights = 0;

    private bool terrainGenRunning = false;

    private bool didtrackingObjectChange = false;
    private GameObject lastTrackingObject;

    void Start()
    {
        if (trackingObject != null)
        {
            GenerateAllTerrains();
        }
    }

    void Update()
    {
        // recalculate if tracking object changes
        if (CheckTrackingObjectChange())
        {
            GenerateAllTerrains();
        }
        
        if (!terrainGenRunning)
        {
            StartCoroutine(UpdateTerrainsCoroutine(false, useCalculationLimit));
        }
        else
        {
            if (useAdaptiveCalcLimit)
            {
                AdaptCalculationLimit();
            }
        }
    }

    /// <summary>
    /// Generates all terrains within one frame around the player.
    /// </summary>
    public void GenerateAllTerrains()
    {
        terrains = GameObject.FindObjectsOfType<Terrain>();
        PlaceTerrains();
        StartCoroutine(UpdateTerrainsCoroutine(true, false));
        Debug.Log("Debug: Generated all terrains.");
    }

    /// <summary>
    /// Places all terrains at their according grid positions.
    /// </summary>
    private void PlaceTerrains()
    {
        float distance = mapSize;

        terrainsInLine = (int)Mathf.Sqrt(terrains.Length);
        for (int x = 0; x < terrainsInLine; x++)
        {
            for (int y = 0; y < terrainsInLine; y++)
            {
                int xCentered = x - terrainsInLine / 2;
                int yCentered = y - terrainsInLine / 2;
                terrains[x * terrainsInLine + y].transform.position = new Vector3(xCentered * distance, 0, yCentered * distance);
            }
        }
    }

    /// <summary>
    /// Move terrains around player if necessary and recalculates heights. Depends on player movement.
    /// </summary>
    IEnumerator UpdateTerrainsCoroutine(bool updateAll, bool useCalculationLimitLocal)
    {
        // if player too far away from terrain (on x or y)
        // move terrain by jumplength (2*width) in that direction
        terrainGenRunning = true;

        for (int i = 0; i < terrains.Length; i++)
        {
            bool isLowRes = false;
            if (terrains[i].terrainData.heightmapResolution == distantResolution) isLowRes = true;
            float maxDistance = mapSize * ((float)terrainsInLine / 2f) + mapSize * 0.01f;
            float highResDistance = mapSize * (((float)highResDistanceChunks * 2f + 1f) / 2f);

            // Does tile need to be moved in front of the player?
            bool needsMove = false;
            bool needsLowRes = false;
            Vector3 trackingPosition = Vector3.zero;
            if (trackingObject != null)
            {
                trackingPosition = trackingObject.transform.position;
            }

            float distanceX = trackingPosition.x - (terrains[i].transform.position.x + mapSize * 0.5f);
            if (Mathf.Abs(distanceX) > maxDistance)
            {
                terrains[i].transform.position += Mathf.Sign(distanceX) * Vector3.right * mapSize * (float)terrainsInLine;
                needsMove = true;
            }

            float distanceZ = trackingPosition.z - (terrains[i].transform.position.z + mapSize * 0.5f);
            if (Mathf.Abs(distanceZ) > maxDistance)
            {
                terrains[i].transform.position += Mathf.Sign(distanceZ) * Vector3.forward * mapSize * (float)terrainsInLine;
                needsMove = true;
            }

            // Does tile need to become other resolution?
            if (Mathf.Abs(distanceX) > highResDistance) needsLowRes = true;
            if (Mathf.Abs(distanceZ) > highResDistance) needsLowRes = true;

            // Move/recalculate tile if necessary
            if (updateAll || needsMove || !(isLowRes == needsLowRes))
            {
                int resolution = this.resolution;
                if (needsLowRes) resolution = distantResolution;

                // ugly, but needed so the "Generate terrain" button as well as adaptive calculations work
                if (useCalculationLimitLocal)
                {
                    yield return StartCoroutine(
                        GenerateTerrainCouroutine(terrains[i].terrainData, terrains[i].transform.position, resolution, i, useCalculationLimitLocal, (newTerrainData) =>
                        {
                            terrains[i].terrainData = newTerrainData;
                        }));
                }
                else
                {
                    StartCoroutine(
                        GenerateTerrainCouroutine(terrains[i].terrainData, terrains[i].transform.position, resolution, i, useCalculationLimitLocal, (newTerrainData) =>
                        {
                            if (i < terrains.Length)
                            {
                                terrains[i].terrainData = newTerrainData;
                            }
                        }));
                }
            }
        }
        terrainGenRunning = false;
        yield return null;
    }

    /// <summary>
    /// Calculates terrain. Calls GenerateHeightsCoroutine to get height values.
    /// </summary>
    IEnumerator GenerateTerrainCouroutine(TerrainData terrainData, Vector3 worldPos, int resolution, int terrainID, bool useCalculationLimitLocal, System.Action<TerrainData> callback)
    {
        terrainData.heightmapResolution = resolution;

        maxTerrainDepth = 0;
        for (int i = 0; i < iterations.Length; i++)
        {
            maxTerrainDepth += iterations[i].depth;
        }

        terrainData.size = new Vector3(mapSize, maxTerrainDepth * worldScale / worldDepthDivider, mapSize);
        
        // ugly, but needed so the "Generate terrain" button as well as adaptive calculations work
        if (useCalculationLimitLocal)
        {
            yield return StartCoroutine(
                GenerateHeightsCoroutine(worldPos, resolution, terrainID, useCalculationLimitLocal, (heights) =>
                {
                    terrainData.SetHeights(0, 0, heights);
                }));
        }
        else
        {
            StartCoroutine(
                GenerateHeightsCoroutine(worldPos, resolution, terrainID, useCalculationLimitLocal, (heights) =>
                {
                    terrainData.SetHeights(0, 0, heights);
                }));
        }

        callback(terrainData);
        yield return null;
    }

    /// <summary>
    /// Calculates all height values in one tile. Waits till next frame if calculationLimit is reached.
    /// </summary>
    IEnumerator GenerateHeightsCoroutine(Vector3 worldPos, int resolution, int terrainID, bool useCalculationLimitLocal, System.Action<float[,]> callback)
    {
        float[,] heights = new float[resolution, resolution];

        // for every noise iteration, go over every vertex and calculate height
        for (int i = 0; i < iterations.Length; i++)
        {
            if (iterations[i].isUsed)
            {
                for (int x = 0; x < resolution; x++)
                {
                    for (int y = 0; y < resolution; y++)
                    {
                            heights[x, y] += CalculateHeight(x, y, iterations[i], worldPos, resolution);
                            currCalculatedHeights++;
                    }
                }
                if (useCalculationLimitLocal && currCalculatedHeights > calculatedHeightsPerFrame)
                {
                    currCalculatedHeights = 0;
                    yield return null;
                }
            }
        }
        callback(heights);
    }

    float CalculateHeight(int x, int y, TerrainIteration iteration, Vector3 worldPos, int resolution)
    {
        float worldPosOffsetX = worldPos.z / worldScale;
        float worldPosOffsetY = worldPos.x / worldScale;
        float xCoord = (float)x  / ((float)resolution - 1f) * mapSize / worldScale + iteration.offsetX + worldOffsetX + worldPosOffsetX;
        float yCoord = (float)y  / ((float)resolution - 1f) * mapSize / worldScale + iteration.offsetY + worldOffsetY + worldPosOffsetY;
        xCoord *= iteration.distortionX / iteration.scale / iteration.rarity;
        yCoord *= iteration.distortionY / iteration.scale / iteration.rarity;

        float height = Mathf.PerlinNoise(xCoord, yCoord);
        height = iteration.depthScaling.Evaluate(height * iteration.rarity - (1f - 1f / iteration.rarity) * iteration.rarity);
        height *= iteration.depth / (float)maxTerrainDepth;

        return height;
    }

    // In/decreases calculated height values per frame, to keep above FPS limit.
    private void AdaptCalculationLimit()
    {
        float fps = (1f / Time.smoothDeltaTime);
        if (fps > keepFPSAbove)
        {
            calculatedHeightsPerFrame += adjustCalculationBy;
        }
        else
        {
            calculatedHeightsPerFrame -= adjustCalculationBy;
            if (calculatedHeightsPerFrame < minCalculationsPerFrame) calculatedHeightsPerFrame = minCalculationsPerFrame;
        }
    }

    // Check if the tracked object changed.
    private bool CheckTrackingObjectChange()
    {
        if (trackingObject != lastTrackingObject)
        {
            lastTrackingObject = trackingObject;
            return true;
        }

        return false;
    }

}


[System.Serializable]
public class TerrainIteration
{
    public string name = "Iteration";
    public bool isUsed = true;

    public AnimationCurve depthScaling;

    public int depth = 20;
    public float scale = 20f;
    public float rarity = 1f;

    public float offsetX = 100f;
    public float offsetY = 100f;

    public float distortionX = 1.0f;
    public float distortionY = 1.0f;
}
