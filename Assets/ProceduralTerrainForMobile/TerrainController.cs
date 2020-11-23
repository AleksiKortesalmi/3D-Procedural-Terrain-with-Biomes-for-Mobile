using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Utility;

public class TerrainController : MonoBehaviour {
    [SerializeField]
    private Transform player = null;
    [SerializeField]
    [Tooltip("Optional")]
    private Transform water = null;
    [SerializeField]
    [Range(0.0f, 1.0f)]
    private float waterHeight = 0.5f;

    [Header("Terrain System")]
    [SerializeField]
    private GameObject terrainTilePrefab = null;
    [SerializeField]
    private Vector3 terrainSize = new Vector3(1000, 1000, 1000);
    public Vector3 TerrainSize { get => terrainSize; }
    [SerializeField]
    [Tooltip("Vertices per side.\nTotal vertices = (TileResolution * TileResolution)")]
    private int tileResolution = 10;
    public int TileResolution { get => tileResolution; }
    [SerializeField]
    [Tooltip("How many tiles to render around the player.")]
    private int radiusToRender = 2;
    [SerializeField]
    private Octave[] octaves = new Octave[1] { new Octave(1, 1) };
    public Octave[] Octaves { get => octaves; }
    [SerializeField]
    [Tooltip("Transforms that need tiles rendered under them.(RigidBodies)")]
    private Transform[] gameTransforms = null;
    public bool randomSeed;
    [SerializeField]
    private int seed = 0;

    [Header("Biome System")]
    [SerializeField]
    [Range(0.01f, 1)]
    private float biomeFrequency = 0.5f;
    public float BiomeFrequency { get => biomeFrequency; }
    [SerializeField]
    [Tooltip("Biome data.")]
    private Biome[] biomes = new Biome[] { new Biome() };
    public Biome[] Biomes { get => biomes; private set => biomes = Biomes; }

    [Header("Asset Placement System")]
    [SerializeField]
    [Tooltip("How many tiles to place objects on around the player.")]
    private int radiusToPlaceObjects = 2;
    [SerializeField]
    [Tooltip("Maximum instantiation amount per frame for initial asset placement.")]
    private int instantiationLimitPerFrame = 5;
    [SerializeField]
    [Tooltip("Maximum amount of assets moved from pool per frame.")]
    private int spawnLimitPerFrame = 25;
    [SerializeField]
    [Range(0.00f, 1.00f)]
    [Tooltip("Specify how much a biome's assets go beyond its boundaries.")]
    private float overlapPercentage = 0.5f;
    public float OverLapPercentage { get => overlapPercentage; }
    [SerializeField]
    [Tooltip("Use perlin noise for biomes. Only good for 2 biomes.")]
    private bool usePerlinNoiseForBiomes = false;
    public bool UsePerlinNoiseForBiomes { get => usePerlinNoiseForBiomes; }

    [Header("Performance")]
    [SerializeField]
    [Tooltip("Limit the amount of GameObjects disabled per frame by AssetPlacement on tile creation.")]
    private int disableLimitPerFrame = 25;
    [SerializeField]
    [Tooltip("Limit the amount of points filtered per frame by AssetPlacement on tile creation.")]
    private int pointFilterLimitPerFrame = 100;
    public int PointFilterLimitPerFrame { get => pointFilterLimitPerFrame; }

    // Terrain tiling
    public Transform Level { get; set; }
    private Vector2[] previousCenterTiles;
    private readonly Dictionary<Vector2, GameObject> terrainTiles = new Dictionary<Vector2, GameObject>();
    private readonly List<GameObject> oldTiles = new List<GameObject>();
    private readonly List<Vector2> centerTiles = new List<Vector2>();
    private readonly List<AssetPlacement> assetPlacementInstances = new List<AssetPlacement>();
    private Vector2 startOffset;
    private Vector2 noiseRange = Vector2.one * 256;
    private int totalTileAmount;
    bool changingTiles = false;

    // Asset pooling
    public List<List<List<GameObject>>> ObjectPools { get; set; } = new List<List<List<GameObject>>>();
    public Transform PoolObjectParent { get; private set; }
    private int placingTileIndex = 0;

    // Poisson disc sampling points [biome][placeableobjects][point]
    public List<List<List<Vector2>>> PoissonDiscSamplingPoints { get; private set; } = new List<List<List<Vector2>>>();

    // Array initialization after expansion
    private bool firstDeserialization = true;
    private int biomesLength = 0;
    private readonly List<int> placeableObjectLengths = new List<int>();

    private void OnValidate()
    {
        if (usePerlinNoiseForBiomes && biomes.Length > 2)
        {
            Debug.LogWarning("Perlin noise for more than 2 biomes isn't optimal");
        }

        if (radiusToPlaceObjects > radiusToRender)
            radiusToPlaceObjects = radiusToRender;

        if (firstDeserialization)
        {
            // This is the first time the editor properties have been deserialized in the object.
            // We take the actual array sizes.

            biomesLength = biomes.Length;
            firstDeserialization = false;

            for (int i = 0; i < biomes.Length; i++)
            {
                placeableObjectLengths.Add(biomes[i].PlaceableObjects.Length);
            }
        }
        else
        {
            // Something has changed in the object's properties. Verify whether the array size
            // has changed. If it has been expanded, initialize the new elements.
            // Without this, new elements would be initialized to zero / null (first new element)
            // or to the value of the last element.

            if (biomes.Length > biomesLength)
            {
                for (int i = biomesLength; i < biomes.Length; i++)
                    biomes[i] = new Biome();

                biomesLength = biomes.Length;
            }

            for (int i = 0; i < biomes.Length; i++)
            {
                if (biomes.Length > placeableObjectLengths.Count)
                    placeableObjectLengths.Add(biomes[i].PlaceableObjects.Length);
                if (biomes.Length < placeableObjectLengths.Count)
                    placeableObjectLengths.RemoveAt(placeableObjectLengths.Count - 1);

                if (biomes[i].PlaceableObjects.Length > placeableObjectLengths[i])
                {
                    for (int j = placeableObjectLengths[i]; j < biomes[i].PlaceableObjects.Length; j++)
                        biomes[i].PlaceableObjects[j] = new PlaceableObject();

                    placeableObjectLengths[i] = biomes.Length;
                }
            }
        }
    }

    private void Awake() {
        if (!player)
        {
            Debug.LogError("Player not assigned!");

            return;
        }

        totalTileAmount = (1 + radiusToRender * 2) * (1 + radiusToRender * 2);

        VoronoiTiling.Initialize((int)((terrainSize.x + terrainSize.z) * (100 - 95 * biomeFrequency)) * Vector2Int.one);

        GeneratePoissonDiscPoints();

        InitializePools();

        InitialLoad();
    }

    private void Update() {
        if (!player)
            return;

        // Start placing assets after all the tiles have been created
        if (terrainTiles.Count == totalTileAmount)
        {
            PlaceObjects();
        }

        // Save the tile the player is on
        Vector2 playerTile = TileFromPosition(player.position);
        // Save the tiles of all tracked objects in gameTransforms (including the player)
        centerTiles.Clear();
        centerTiles.Add(playerTile);
        foreach (Transform t in gameTransforms)
            centerTiles.Add(TileFromPosition(t.localPosition));

        // If no tiles exist yet or tiles should change
        if (previousCenterTiles == null || TilesHaveChanged(centerTiles))
        {
            // Create initial tiles immediately then start moving the existing tiles 1 per frame using a coroutine
            if(!changingTiles)
                StartCoroutine(CreateMissingTiles(centerTiles, playerTile));
        }
            
        previousCenterTiles = centerTiles.ToArray();
    }

    #region Terrain Tiling

    public void InitialLoad()
    {
        Level = new GameObject("Level").transform;
        Level.transform.position = new Vector3(0, -terrainSize.y * 2, 0);

        player.parent = Level;

        PoolObjectParent = new GameObject("PoolObjects").transform;
        PoolObjectParent.transform.parent = Level;

        if (water)
        {
            // Set water height and 
            float waterSideLength = radiusToRender * 2 + 1;
            water.transform.parent = Level;
            water.position = new Vector3(0, -terrainSize.y * (1 - waterHeight), 0);
            water.localScale = new Vector3(terrainSize.x / 1 * waterSideLength, terrainSize.z / 1 * waterSideLength, 1);
        }

        if (randomSeed)
            seed = UnityEngine.Random.Range(0, 100000);

        UnityEngine.Random.InitState(seed);
        // Choose a random place on perlin noise
        startOffset = new Vector2(UnityEngine.Random.Range(0f, noiseRange.x), UnityEngine.Random.Range(0f, noiseRange.y));
        RandomizeInitState();
    }

    /// <summary>
    /// Checks if (<paramref name="xIndex"/>, <paramref name="yIndex"/>) tile needs to be created and return true if tile was created/moved.
    /// </summary>
    private bool CreateTile(int xIndex, int yIndex, List<GameObject> tileObjects)
    {
        if (!terrainTiles.ContainsKey(new Vector2(xIndex, yIndex)))
        {
            tileObjects.Add(CreateOrMoveTile(xIndex, yIndex));

            return true;
        }

        return false;
    }

    /// <summary>
    /// Create/move (<paramref name="xIndex"/>, <paramref name="yIndex"/>) tile and return created/moved tile.
    /// </summary>
    private GameObject CreateOrMoveTile(int xIndex, int yIndex)
    {
        GameObject terrain;

        // If the oldTiles "object pool" is empty instantiate a new tile 
        // otherwise grab a tile from pool and initialize it
        if (oldTiles.Count == 0)
        {
            terrain = Instantiate(
                terrainTilePrefab,
                Vector3.zero,
                Quaternion.identity,
                Level
            );
        }
        else
        {
            oldTiles[0].transform.position = Vector3.zero;
            oldTiles[0].transform.rotation = Quaternion.identity;
            terrain = oldTiles[0];
        }

        // Move tile to its proper position
        terrain.transform.localPosition = new Vector3(terrainSize.x * xIndex, terrainSize.y, terrainSize.z * yIndex);

        // Trim "(Clone)" and the old index from the name for a new index
        string indexFormat = "[" + xIndex + " , " + yIndex + "]";
        terrain.name = TrimIndex(terrain.name, indexFormat);
        terrain.name = TrimEnd(terrain.name, "(Clone)") + indexFormat;

        terrainTiles.Add(new Vector2(xIndex, yIndex), terrain);

        MeshGeneration gm = terrain.GetComponent<MeshGeneration>();
        if (gm.TerrainController == null)
            gm.TerrainController = this;
        gm.GenerateNewMesh(TerrainNoiseOffset(xIndex, yIndex), BiomeNoiseOffset(xIndex, yIndex));

        AssetPlacement po = gm.GetComponent<AssetPlacement>();
        StartCoroutine(po.DisableObjects(disableLimitPerFrame));
        if (po.TerrainController == null)
            po.TerrainController = this;

        return terrain;
    }

    /// <summary>
    /// Calculate noise offsets of tile for terrain height perlin noise.
    /// </summary>
    private Vector2[] TerrainNoiseOffset(int xIndex, int yIndex)
    {
        Vector2[] noiseOffset = new Vector2[octaves.Length];

        for (int i = 0; i < octaves.Length; i++)
        {
            noiseOffset[i] = new Vector2(
                    (xIndex * octaves[i].Frequency + startOffset.x) % noiseRange.x,
                    (yIndex * octaves[i].Frequency + startOffset.y) % noiseRange.y
                );

            if (noiseOffset[i].x < 0)
                noiseOffset[i] = new Vector2(noiseOffset[i].x + noiseRange.x, noiseOffset[i].y);
            if (noiseOffset[i].y < 0)
                noiseOffset[i] = new Vector2(noiseOffset[i].x, noiseOffset[i].y + noiseRange.y);
        }

        return noiseOffset;
    }

    /// <summary>
    /// Calculate noise offset of tile for biome perlin noise.
    /// </summary>
    private Vector2 BiomeNoiseOffset(int xIndex, int yIndex)
    {
        Vector2 biomeNoiseOffset = new Vector2(
                (xIndex * biomeFrequency * 0.1f + startOffset.x) % noiseRange.x,
                (yIndex * biomeFrequency * 0.1f + startOffset.y) % noiseRange.y
            );

        if (biomeNoiseOffset.x < 0)
            biomeNoiseOffset = new Vector2(biomeNoiseOffset.x + noiseRange.x, biomeNoiseOffset.y);
        if (biomeNoiseOffset.y < 0)
            biomeNoiseOffset = new Vector2(biomeNoiseOffset.x, biomeNoiseOffset.y + noiseRange.y);

        return biomeNoiseOffset;
    }

    /// <summary>
    /// Get the index of the tile under <paramref name="position"/>.
    /// </summary>
    private Vector2 TileFromPosition(Vector3 position)
    {
        return new Vector2(Mathf.FloorToInt(position.x / terrainSize.x + .5f), Mathf.FloorToInt(position.z / terrainSize.z + .5f));
    }

    /// <summary>
    /// Initialize UnityEngine.Random.InitState with time.
    /// </summary>
    private void RandomizeInitState()
    {
        UnityEngine.Random.InitState((int)DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// If <paramref name="centerTiles"/> are different in any way compared to previous center tiles return true.
    /// </summary>
    private bool TilesHaveChanged(List<Vector2> centerTiles)
    {
        if (previousCenterTiles.Length != centerTiles.Count)
            return true;

        for (int i = 0; i < previousCenterTiles.Length; i++)
            if (previousCenterTiles[i] != centerTiles[i])
                return true;

        return false;
    }

    /// <summary>
    /// If <paramref name="str"/> ends with <paramref name="end"/>, trim it off.
    /// </summary>
    private static string TrimEnd(string str, string end)
    {
        if (str.EndsWith(end))
            return str.Substring(0, str.LastIndexOf(end));
        return str;
    }

    /// <summary>
    /// If <paramref name="str"/> has the first character of <paramref name="indexFormat"/> in it, trim it and the rest off.
    /// </summary>
    private static string TrimIndex(string str, string indexFormat)
    {
        int startOfTileIndex = str.IndexOf(indexFormat.Substring(0, 1));
        if (startOfTileIndex >= 0)
            return str.Substring(0, startOfTileIndex);
        return str;
    }

    /// <summary>
    /// Go through all the tile indeces in the radiusToRender radius of <pa and create missing ones or move the old tiles to the new position.
    /// </summary>
    private IEnumerator CreateMissingTiles(List<Vector2> centerTiles, Vector2 playerTile)
    {
        changingTiles = true;

        List<GameObject> tileObjects = new List<GameObject>();

        //Activate staying tiles
        foreach (Vector2 tile in centerTiles)
        {
            bool isPlayerTile = tile == playerTile;
            int radius = isPlayerTile ? radiusToRender : 1;
            for (int i = -radius; i <= radius; i++)
                for (int j = -radius; j <= radius; j++)
                {
                    Vector2 key = new Vector2((int)tile.x + i, (int)tile.y + j);
                    if (terrainTiles.ContainsKey(key))
                    {
                        tileObjects.Add(terrainTiles[key]);
                    }
                }
        }

        List<Vector2> keysToRemove = new List<Vector2>();
        foreach (KeyValuePair<Vector2, GameObject> kv in terrainTiles)
            if (!tileObjects.Contains(kv.Value))
            {
                oldTiles.Add(kv.Value);
                keysToRemove.Add(kv.Key);
            }

        //Create new tiles
        for (int tileIndex = 0; tileIndex < centerTiles.Count; tileIndex++)
        {
            bool isPlayerTile = centerTiles[tileIndex] == playerTile;

            if (isPlayerTile && water)
                water.localPosition = new Vector3(centerTiles[tileIndex].x * terrainSize.x, water.localPosition.y, centerTiles[tileIndex].y * terrainSize.z);

            int radius = isPlayerTile ? radiusToRender : 1;
            for (int i = -radius; i <= radius; i++)
                for (int j = -radius; j <= radius; j++)
                {
                    if (CreateTile((int)centerTiles[tileIndex].x + i, (int)centerTiles[tileIndex].y + j, tileObjects) && oldTiles.Count != 0)
                    {
                        oldTiles.RemoveAt(0);

                        yield return null;
                    }
                }
        }

        foreach (Vector2 key in keysToRemove)
            terrainTiles.Remove(key);

        changingTiles = false;
    }

    #endregion Terrain Tiling

    #region Asset Placement Control

    /// <summary>
    /// Sort tiles by distance and go through them placing the wanted amount of objects by either moving an object from pool or instantiating it.
    /// </summary>
    private void PlaceObjects()
    {
        if (assetPlacementInstances.Count == 0)
        {
            foreach (KeyValuePair<Vector2, GameObject> tile in terrainTiles)
            {
                if (tile.Value != null)
                    assetPlacementInstances.Add(tile.Value.GetComponent<AssetPlacement>());
            }
        }

        assetPlacementInstances.SortByDistance3D(x => x.transform.position, player.position);

        // Go through the radiusToPlaceObjects amount of tiles and place the objects until instantiation or spawn limits are reached.
        int spawns = 0, instantiations = 0;

        while (spawnLimitPerFrame > spawns && instantiationLimitPerFrame > instantiations)
        {
            if (assetPlacementInstances[placingTileIndex].PlaceObject())
                instantiations++;
            else
                spawns++;

            int totalPlacingTiles = (1 + radiusToPlaceObjects * 2) * (1 + radiusToPlaceObjects * 2);

            // Place every object on the closest tiles before placing on the distant ones
            if (assetPlacementInstances[0].PlacingIndex != assetPlacementInstances[0].SpawnPointsCount)
            {
                placingTileIndex = 0;

                continue;
            }
            else if (placingTileIndex == 8 && assetPlacementInstances[8].PlacingIndex != assetPlacementInstances[8].SpawnPointsCount)
            {
                placingTileIndex = 1;

                continue;
            }
            else
            {
                placingTileIndex++;
                if (placingTileIndex == totalPlacingTiles)
                    placingTileIndex = 0;
            }
        }
    }

    public void InitializePools()
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            ObjectPools.Add(new List<List<GameObject>>());

            for (int j = 0; j < biomes[i].PlaceableObjects.Length; j++)
            {
                ObjectPools[i].Add(new List<GameObject>());
            }
        }
    }

    /// <summary>
    /// Generate the poisson disc sampling points for the PlaceableObjects that use them.
    /// </summary>
    private void GeneratePoissonDiscPoints()
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            PoissonDiscSamplingPoints.Add(new List<List<Vector2>>());

            for (int j = 0; j < biomes[i].PlaceableObjects.Length; j++)
            {
                if (!biomes[i].PlaceableObjects[j].UseRandomPointsForPlacing)
                    PoissonDiscSamplingPoints[i].Add(new List<Vector2>(PoissonDiscSampling.GeneratePoints(biomes[i].PlaceableObjects[j].ObjectSpread, new Vector2(terrainSize.x, terrainSize.z))));
                else
                    PoissonDiscSamplingPoints[i].Add(new List<Vector2>());
            }
        }
    }

    #endregion Asset Placement Control
}

[Serializable]
public class Octave
{
    [SerializeField]
    private float frequency, amplitude;
    public float Frequency { get => frequency; set => frequency = Frequency; }
    public float Amplitude { get => amplitude; set => amplitude = Amplitude; }

    public Octave(float frequency, float amplitude)
    {
        this.frequency = frequency;
        this.amplitude = amplitude;
    }
}
[Serializable]
public class Biome
{
    [SerializeField]
    private Gradient color = new Gradient();
    public Gradient Color { get => color; set => color = Color; }
    [SerializeField]
    private PlaceableObject[] placeableObjects = new PlaceableObject[] { new PlaceableObject() };
    public PlaceableObject[] PlaceableObjects { get => placeableObjects; set => placeableObjects = PlaceableObjects; }
}

[Serializable]
public class PlaceableObject
{
    [SerializeField]
    [Tooltip("Have more prefabs for variety.")]
    private GameObject[] prefabs = new GameObject[1];
    public GameObject[] Prefabs { get => prefabs; }
    [SerializeField]
    [Tooltip("Objects radius or distance to others.")]
    private float objectSpread = 50;
    public float ObjectSpread { get => objectSpread; }
    [SerializeField]
    [Tooltip("Percentage of assets placed by height.\nX axis => height. Y axis => percentage placed.")]
    private AnimationCurve amountByHeight = AnimationCurve.Constant(0, 1, 1);
    public AnimationCurve AmountByHeight { get => amountByHeight; }
    [SerializeField]
    [Tooltip("Percentage of assets placed by slope angle.\nX axis => slope angle (0 - 90). Y axis => percentage placed.")]
    private AnimationCurve amountBySlopeAngle = AnimationCurve.Constant(0, 1, 1);
    public AnimationCurve AmountBySlopeAngle { get => amountBySlopeAngle; }
    [SerializeField]
    private Vector3Range translateRandomRange = new Vector3Range(Vector3.zero, Vector3.zero);
    public Vector3Range TranslateRandomRange { get => translateRandomRange; }
    [SerializeField]
    private Vector3Range rotateRandomRange = new Vector3Range(Vector3.zero, Vector3.zero);
    public Vector3Range RotateRandomRange { get => rotateRandomRange; }
    [SerializeField]
    private Vector2 scaleRandomRange = Vector2.one;
    public Vector2 ScaleRandomRange { get => scaleRandomRange; }
    [SerializeField]
    private bool placeOnOtherObjects = false;
    public bool PlaceOnOtherObjects { get => placeOnOtherObjects; }
    [SerializeField]
    private bool placeOnWater = false;
    public bool PlaceOnWater { get => placeOnWater; }
    [SerializeField]
    [Tooltip("Precise control on how many objects are on a tile but they aren't evenly spaced.")]
    private bool useRandomPointsForPlacing = false;
    public bool UseRandomPointsForPlacing { get => useRandomPointsForPlacing; }
    [SerializeField]
    [Tooltip("Only used if random points are used for placing, otherwise objectSpread is used to determine how many objects there are per tile.")]
    private int amountPerTile = 1;
    public int AmountPerTile { get => amountPerTile; }
}
