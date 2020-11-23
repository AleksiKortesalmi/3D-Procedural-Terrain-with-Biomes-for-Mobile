using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// Generate spawn points for biomes within their boundaries and place objects from them.
/// </summary>
[RequireComponent(typeof(MeshGeneration))]
public class AssetPlacement : MonoBehaviour
{
    public TerrainController TerrainController { get; set; }
    private MeshGeneration meshGeneration;

    private readonly List<SpawnPoint> spawnPoints = new List<SpawnPoint>();
    private readonly List<GameObject> objectsOnTile = new List<GameObject>();
    private readonly List<Vector2> generated2DPoints = new List<Vector2>();
    private readonly List<Vector3> generated3DPoints = new List<Vector3>();
    private bool disablingOldObjects = false;
    public int SpawnPointsCount { get; private set; } = 0;
    public int PlacingIndex { get; private set; } = 0;

    private void Awake()
    {
        meshGeneration = GetComponent<MeshGeneration>();
    }

    /// <summary>
    /// Start generating spawnPoints.
    /// </summary>
    public void GenerateSpawnPoints()
    {
        // Stop previous coroutines from affecting the next coroutine
        StopAllCoroutines();

        StartCoroutine(SetSpawnPoints());
    }

    #region Placing

    /// <summary>
    /// Place an object either by instantiating it or moving it from pool.
    /// </summary>
    /// <returns>Object instantiated.</returns>
    public bool PlaceObject()
    {
        // Try to place an object from pool and if the object isn't available(inactive) => instantiate one
        if (PlacingIndex < SpawnPointsCount && !disablingOldObjects)
        {
            Vector2Int key = spawnPoints[PlacingIndex].ObjectPoolIndex;

            if (0 < TerrainController.ObjectPools[key.x][key.y].Count && !TerrainController.ObjectPools[key.x][key.y][0].activeSelf)
            {
                PlaceObjectFromPool(key);

                return false;
            }
            else if(0 < TerrainController.ObjectPools[key.x][key.y].Count)
            {
                MoveToLast(TerrainController.ObjectPools[key.x][key.y], 0);
            }

            InstantiateObject();

            return true;
        }

        return false;
    }

    private void InstantiateObject()
    {
        PlaceableObject objectData = TerrainController.Biomes[spawnPoints[PlacingIndex].ObjectPoolIndex.x].PlaceableObjects[spawnPoints[PlacingIndex].ObjectPoolIndex.y];
        int prefabIndex = Random.Range(0, objectData.Prefabs.Length);

        // Helpful error codes
        if (objectData.Prefabs.Length == 0)
        {
            Debug.LogError("Prefab array's length is zero at: " + "Region " + spawnPoints[PlacingIndex].ObjectPoolIndex.x + " PlaceableObject " + spawnPoints[PlacingIndex].ObjectPoolIndex.y);

            return;
        }
        else if (!spawnPoints[PlacingIndex].Prefabs[prefabIndex])
        {
            Debug.LogError("Prefab missing from: Region " + spawnPoints[PlacingIndex].ObjectPoolIndex.x + " PlaceableObject " + spawnPoints[PlacingIndex].ObjectPoolIndex.y + " Prefab " + prefabIndex);

            return;
        }

        if (Physics.Raycast(spawnPoints[PlacingIndex].Position, Vector3.down, out RaycastHit hit) && PlacingConditionsApply(hit, objectData) && spawnPoints[PlacingIndex].Prefabs[prefabIndex])
        {
            GameObject instantiatedObject = Instantiate(spawnPoints[PlacingIndex].Prefabs[prefabIndex],
                hit.point, Quaternion.identity, TerrainController.PoolObjectParent);

            ApplyTransform(instantiatedObject.transform, hit, objectData, prefabIndex);

            MoveObjectToPool(instantiatedObject, spawnPoints[PlacingIndex].ObjectPoolIndex);

            objectsOnTile.Add(instantiatedObject);
        }

        PlacingIndex++;
    }

    private void PlaceObjectFromPool(Vector2Int key)
    {
        GameObject placeableObject = TerrainController.ObjectPools[key.x][key.y][0];
        PlaceableObject objectData = TerrainController.Biomes[spawnPoints[PlacingIndex].ObjectPoolIndex.x].PlaceableObjects[spawnPoints[PlacingIndex].ObjectPoolIndex.y];

        if (Physics.Raycast(spawnPoints[PlacingIndex].Position, Vector3.down, out RaycastHit hit) && PlacingConditionsApply(hit, objectData))
        {
            ApplyTransform(placeableObject.transform, hit, objectData);

            objectsOnTile.Add(placeableObject);

            // Move object to last in the pool 
            MoveToLast(TerrainController.ObjectPools[key.x][key.y], 0);

            placeableObject.SetActive(true);
        }

        PlacingIndex++;
    }

    /// <summary>
    /// Check if the <paramref name="objectData"/> placing conditions apply to <paramref name="hit"/>.
    /// </summary>
    /// <param name="hit">Placing raycasthit.</param>
    /// <param name="objectData">PlaceableObject data for the object.</param>
    private bool PlacingConditionsApply(RaycastHit hit, PlaceableObject objectData)
    {
        // Get slope angle and make sure it isn't NaN
        float slopeAngle = Mathf.Acos(Vector3.Dot(Vector3.up, hit.normal)) * Mathf.Rad2Deg;
        if (float.IsNaN(slopeAngle))
            slopeAngle = 0;

        // Check the conditions
        if (Random.Range(0, 100) < Mathf.Clamp01(objectData.AmountByHeight.Evaluate((hit.point.y + TerrainController.TerrainSize.y) / TerrainController.TerrainSize.y)) * 100
            && Random.Range(0, 100) < Mathf.Clamp01(objectData.AmountBySlopeAngle.Evaluate(slopeAngle / 90)) * 100
            && (hit.collider.CompareTag("Terrain")
            || objectData.PlaceOnWater && hit.collider.CompareTag("Water")
            || objectData.PlaceOnOtherObjects && objectData.PlaceOnWater
            || objectData.PlaceOnOtherObjects && !objectData.PlaceOnWater && !hit.collider.CompareTag("Water")))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Apply the <paramref name="objectData"/> placing "commands" to <paramref name="objectTransform"/>.
    /// </summary>
    /// <param name="objectTransform">Target objects transform.</param>
    /// <param name="hit">Placing raycast.</param>
    /// <param name="objectData">PlaceableObject data for the target object.</param>
    /// <param name="prefabIndex">Index of prefab to get prefabs scale for instantiation.</param>
    private void ApplyTransform(Transform objectTransform, RaycastHit hit, PlaceableObject objectData, int prefabIndex = -1)
    {
        objectTransform.position = hit.point + objectData.TranslateRandomRange.RandomVector3();

        objectTransform.eulerAngles = objectData.RotateRandomRange.RandomVector3();

        // Only scale on instantiation
        if(prefabIndex >= 0)
            objectTransform.localScale = objectData.Prefabs[prefabIndex].transform.localScale * Random.Range(objectData.ScaleRandomRange.x, objectData.ScaleRandomRange.y);
    }

    #endregion Placing

    #region Pooling

    private void MoveToLast(List<GameObject> list, int index)
    {
        GameObject moveableObject = list[index];
        list.RemoveAt(index);
        list.Add(moveableObject);
    }

    private void MoveObjectToPool(GameObject instantiatedObject, Vector2Int key)
    {
        TerrainController.ObjectPools[key.x][key.y].Add(instantiatedObject);
    }

    /// <summary>
    /// Disable objects that are on this tile, making them available to place on other tiles.
    /// </summary>
    public IEnumerator DisableObjects(int limitPerFrame)
    {
        // Disable placing on this tile so new objects dont get deactivated immediately
        disablingOldObjects = true;

        if (objectsOnTile.Count != 0)
        {
            int perFrame = limitPerFrame, num = perFrame;

            for (int i = 0; i < objectsOnTile.Count; i++)
            {
                objectsOnTile[i].SetActive(false);

                if (i > num)
                {
                    num += perFrame;

                    yield return null;
                }
            }

            objectsOnTile.Clear();
        }

        disablingOldObjects = false;
    }

    #endregion Pooling

    #region Spawnpoint Generation

    /// <summary>
    /// Fill or modify spawnPoints list. If spawnPoints is full only change the values inside the already existing SpawnPoint instances.
    /// </summary>
    private IEnumerator SetSpawnPoints()
    {
        Biome[] biomes = TerrainController.Biomes;
        Vector3 terrainSize = TerrainController.TerrainSize;
        int limitPerFrame = TerrainController.PointFilterLimitPerFrame;

        PlacingIndex = 0;
        SpawnPointsCount = 0;

        for (int i = 0; i < biomes.Length; i++)
        {
            // Check if terrain tile has any of "i" biomes in it
            if (meshGeneration.TileContainsBiome(i))
            {
                PlaceableObject[] placeableObjects = biomes[i].PlaceableObjects;
                for (int j = 0; j < placeableObjects.Length; j++)
                {
                    generated2DPoints.Clear();
                    generated3DPoints.Clear();

                    // Get the unfiltered 2D points
                    if (placeableObjects[j].UseRandomPointsForPlacing)
                        GenerateRandomPoints(TerrainController.Biomes[i].PlaceableObjects[j].AmountPerTile);
                    else
                        generated2DPoints.AddRange(TerrainController.PoissonDiscSamplingPoints[i][j]);

                    // Convert the 2D points to usable over-the-terrain 3D points and add them to the points list
                    Vector3 correction = Vector3.left * terrainSize.x / 2 + Vector3.back * terrainSize.z / 2;
                    float overlapPercentage = TerrainController.OverLapPercentage;
                    Vector2 point;

                    // Limit the amount of points filtered per frame
                    int num = limitPerFrame;
                    for (int y = 0; y < generated2DPoints.Count; y++)
                    {
                        point = generated2DPoints[y];

                        // First check if the point is roughly in the biomes and if it is then more precisely with PointIsInBiome
                        // This is done for performance reasons because there can be thousands of points to go through
                        if (meshGeneration.PointIsInBiome(point, i, overlapPercentage))
                        {
                            generated3DPoints.Add(transform.position + correction + new Vector3(generated2DPoints[y].x, terrainSize.y * 2, generated2DPoints[y].y));
                        }

                        // Wait for a frame if limit is exceeded
                        if (y > num)
                        {
                            num += limitPerFrame;

                            yield return null;
                        }

                    }

                    // Add/Set generated points to the spawnPoints
                    if (generated3DPoints.Count != 0)
                        for (int x = 0; x < generated3DPoints.Count; x++)
                        {
                            if (spawnPoints.Count > SpawnPointsCount)
                            {
                                spawnPoints[SpawnPointsCount].Prefabs = placeableObjects[j].Prefabs;
                                spawnPoints[SpawnPointsCount].Position = generated3DPoints[x];
                                spawnPoints[SpawnPointsCount].ObjectPoolIndex = new Vector2Int(i, j);
                            }
                            else
                            {
                                spawnPoints.Add(new SpawnPoint(placeableObjects[j].Prefabs, generated3DPoints[x], new Vector2Int(i, j)));
                            }

                            // The list is never shortened the items are only modified due to garbage accumulation
                            // "spawnPointsCount" is used to limit the amount of objects placed(in "PlaceObject")
                            // because the actual Count can be longer than how much we want to place this time
                            SpawnPointsCount++;
                        }
                }
            }
        }
    }

    /// <summary>
    /// Generate <paramref name="amount"/> of random points inside tile boundaries.
    /// </summary>
    /// <param name="amount">Amount to generate.</param>
    private void GenerateRandomPoints(int amount)
    {
        Vector3 terrainSize = TerrainController.TerrainSize;

        generated2DPoints.Clear();

        for (int i = 0; i < amount; i++)
        {
            generated2DPoints.Add(new Vector2(Random.Range(0, terrainSize.x), Random.Range(0, terrainSize.z)));
        }
    }

    #endregion Spawnpoint Generation
}

public class SpawnPoint
{
    public GameObject[] Prefabs { get; set; }
    public Vector3 Position { get; set; }
    public Vector2Int ObjectPoolIndex { get; set; }

    public SpawnPoint(GameObject[] prefabs, Vector3 position, Vector2Int objectPoolIndex)
    {
        Prefabs = prefabs;
        Position = position;
        ObjectPoolIndex = objectPoolIndex;
    }
}