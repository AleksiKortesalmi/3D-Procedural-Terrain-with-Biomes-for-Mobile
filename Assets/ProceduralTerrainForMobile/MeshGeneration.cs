using ProceduralToolkit;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the mesh and finds biome boundaries for the tile.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class MeshGeneration : MonoBehaviour {
    public TerrainController TerrainController { get; set; }
    private AssetPlacement assetPlacement;
    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;

    // Biome management
    // YEdgesPerXStep and XEdgesPerYStep are boundaries for the biomes
    public Vector2[][] YEdgesPerXStep { get; private set; } = new Vector2[][] { };
    public Vector2[][] XEdgesPerYStep { get; private set; } = new Vector2[][] { };

    private MeshDraft draft;
    private int xSegments, zSegments, vertexCount = 0;
    private float xStep, zStep;
    private List<Centroid> closestCentroids = new List<Centroid>();

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        if (GetComponent<AssetPlacement>())
            assetPlacement = GetComponent<AssetPlacement>();
    }

    /// <summary>
    /// Start generating the mesh.
    /// </summary>
    public void GenerateNewMesh(Vector2[] terrainNoiseOffset, Vector2 biomeNoiseOffset) 
    {
        // Disable renderer so the old mesh can't be seen in the new place.
        meshRenderer.enabled = false;

        // Check the terrain tile's 9(3x3 grid) closest voronoi tiles if some tiles are missing create them. Otherwise do nothing.
        VoronoiTiling.GenerateMissingCentroids(transform.position);

        // Get the closest centroids to the terrain tile to have less centroids for the tiles vertices to go through.
        // "amount" is the centroid amount that every vertex in a tile goes through to find the closest one(to get the biome it belongs to).
        // Too small => Artifacts, Too large => Lag spike
        closestCentroids = VoronoiTiling.GetClosestCentroids(transform.position, 5);

        // Stop possible previous coroutines from affecting the latest coroutine
        StopAllCoroutines();

        StartCoroutine(GenerateDraft(terrainNoiseOffset, biomeNoiseOffset));
    }

    /// <summary>
    /// Create the meshdraft and call DraftToMesh which converts the draft to a mesh.
    /// </summary>
    private IEnumerator GenerateDraft(Vector2[] terrainNoiseOffset, Vector2 biomeNoiseOffset) 
    {
        int tileResolution = TerrainController.TileResolution;
        Biome[] biomes = TerrainController.Biomes;
        Vector3 terrainSize = TerrainController.TerrainSize;
        Octave[] octaves = TerrainController.Octaves;

        if (vertexCount == 0)
        {
            xSegments = tileResolution - 1;
            zSegments = tileResolution - 1;

            xStep = terrainSize.x / xSegments;
            zStep = terrainSize.z / zSegments;
            vertexCount = 6 * xSegments * zSegments;

            draft = new MeshDraft
            {
                name = "Terrain",
                vertices = new List<Vector3>(vertexCount),
                triangles = new List<int>(vertexCount),
                normals = new List<Vector3>(vertexCount),
                colors = new List<Color>(vertexCount)
            };

            for (int i = 0; i < vertexCount; i++)
            {
                draft.vertices.Add(Vector3.zero);
                draft.triangles.Add(0);
                draft.normals.Add(Vector3.zero);
                draft.colors.Add(Color.black);
            }
        }

        InitializeBiomeEdges();

        for (int x = 0; x < xSegments; x++) {
            for (int z = 0; z < zSegments; z++) {
                int index0 = 6 * (x + z * xSegments);
                int index1 = index0 + 1;
                int index2 = index0 + 2;
                int index3 = index0 + 3;
                int index4 = index0 + 4;
                int index5 = index0 + 5;

                float height00 = GetHeight(x + 0, z + 0, octaves, terrainNoiseOffset);
                float height01 = GetHeight(x + 0, z + 1, octaves, terrainNoiseOffset);
                float height10 = GetHeight(x + 1, z + 0, octaves, terrainNoiseOffset);
                float height11 = GetHeight(x + 1, z + 1, octaves, terrainNoiseOffset);

                int biome00 = GetBiome(x + 0, z + 0, terrainSize, biomes.Length, biomeNoiseOffset);
                int biome01 = GetBiome(x + 0, z + 1, terrainSize, biomes.Length, biomeNoiseOffset);
                int biome10 = GetBiome(x + 1, z + 0, terrainSize, biomes.Length, biomeNoiseOffset);
                int biome11 = GetBiome(x + 1, z + 1, terrainSize, biomes.Length, biomeNoiseOffset);

                Vector3 vertex00 = new Vector3((x + 0) * xStep, height00 * terrainSize.y, (z + 0) * zStep);
                Vector3 vertex01 = new Vector3((x + 0) * xStep, height01 * terrainSize.y, (z + 1) * zStep);
                Vector3 vertex10 = new Vector3((x + 1) * xStep, height10 * terrainSize.y, (z + 0) * zStep);
                Vector3 vertex11 = new Vector3((x + 1) * xStep, height11 * terrainSize.y, (z + 1) * zStep);

                CompareToBiomeEdges(vertex00, biome00);
                CompareToBiomeEdges(vertex01, biome01);
                CompareToBiomeEdges(vertex10, biome10);
                CompareToBiomeEdges(vertex11, biome11);

                draft.vertices[index0] = vertex00;
                draft.vertices[index1] = vertex01;
                draft.vertices[index2] = vertex11;
                draft.vertices[index3] = vertex00;
                draft.vertices[index4] = vertex11;
                draft.vertices[index5] = vertex10;

                draft.colors[index0] = biomes[biome00].Color.Evaluate(height00);
                draft.colors[index1] = biomes[biome01].Color.Evaluate(height01);
                draft.colors[index2] = biomes[biome11].Color.Evaluate(height11);
                draft.colors[index3] = biomes[biome00].Color.Evaluate(height00);
                draft.colors[index4] = biomes[biome11].Color.Evaluate(height11);
                draft.colors[index5] = biomes[biome10].Color.Evaluate(height10);

                Vector3 normal000111 = Vector3.Cross(vertex01 - vertex00, vertex11 - vertex00).normalized;
                Vector3 normal001011 = Vector3.Cross(vertex11 - vertex00, vertex10 - vertex00).normalized;

                draft.normals[index0] = normal000111;
                draft.normals[index1] = normal000111;
                draft.normals[index2] = normal000111;
                draft.normals[index3] = normal001011;
                draft.normals[index4] = normal001011;
                draft.normals[index5] = normal001011;

                draft.triangles[index0] = index0;
                draft.triangles[index1] = index1;
                draft.triangles[index2] = index2;
                draft.triangles[index3] = index3;
                draft.triangles[index4] = index4;
                draft.triangles[index5] = index5;

                yield return null;
            }
        }

        DraftToMesh();
    }

    /// <summary>
    /// Convert the draft to a mesh and assign it to the tile's MeshFilter component.
    /// </summary>
    private void DraftToMesh()
    {
        // Move the center of the vertices to the center of the tile
        draft.Move(Vector3.left * TerrainController.TerrainSize.x / 2 + Vector3.back * TerrainController.TerrainSize.z / 2);

        meshFilter.mesh = draft.ToMesh();

        meshCollider.sharedMesh = meshFilter.mesh;

        meshRenderer.enabled = true;

        // Start generating points for asset placement
        // now that we know where each biome is (BiomeEdges, YEdgesPerXStep and XEdgesPerYStep)
        if(assetPlacement)
            assetPlacement.GenerateSpawnPoints();
    }

    /// <summary>
    /// Check if tile contains <paramref name="biomeIndex"/> biome.
    /// </summary>
    public bool TileContainsBiome(int biomeIndex)
    {
        for (int i = 0; i < XEdgesPerYStep[biomeIndex].Length; i++)
        {
            if (!float.IsPositiveInfinity(XEdgesPerYStep[biomeIndex][i].x) && !float.IsNegativeInfinity(XEdgesPerYStep[biomeIndex][i].y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if <paramref name="point"/> is within <paramref name="biomeIndex"/> biome's boundaries with <paramref name="overlapPercentage"/> taken into consideration.
    /// </summary>
    public bool PointIsInBiome(Vector2 point, int biomeIndex, float overlapPercentage)
    {
        // Get the points step indices by simply dividing by step and rounding to closest integer
        int xIndex = Mathf.RoundToInt(point.x / xStep);
        int yIndex = Mathf.RoundToInt(point.y / zStep);

        // Check if point is within edges + overlap and if it is not => return false
        if (point.x > XEdgesPerYStep[biomeIndex][yIndex].x - (xStep * overlapPercentage)
            && point.x < XEdgesPerYStep[biomeIndex][yIndex].y + (xStep * overlapPercentage)
            && point.y > YEdgesPerXStep[biomeIndex][xIndex].x - (xStep * overlapPercentage)
            && point.y < YEdgesPerXStep[biomeIndex][xIndex].y + (xStep * overlapPercentage))
        {
            return true;
        }

        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Get the vertex's height from perlin noise.
    /// </summary>
    private float GetHeight(int x, int z, Octave[] octaves, Vector2[] terrainNoiseOffset) 
    {
        float total = 0;
        float maxValue = 0;

        for (int i = 0; i < octaves.Length; i++)
        {
            float noiseX = octaves[i].Frequency * x / xSegments + terrainNoiseOffset[i].x;
            float noiseZ = octaves[i].Frequency * z / zSegments + terrainNoiseOffset[i].y;

            total += Mathf.PerlinNoise(noiseX, noiseZ) * octaves[i].Amplitude;

            maxValue += octaves[i].Amplitude;
        }

        return total / maxValue;
    }

    /// <summary>
    /// Get biome that given vertex belongs to.
    /// </summary>
    private int GetBiome(int x, int z, Vector3 terrainSize, int biomeAmount, Vector2 noiseOffset)
    {
        float noiseX, noiseZ, value;

        if (TerrainController.UsePerlinNoiseForBiomes)
        {
            noiseX = TerrainController.BiomeFrequency * 0.1f * x / xSegments + noiseOffset.x;
            noiseZ = TerrainController.BiomeFrequency * 0.1f * z / zSegments + noiseOffset.y;
            value = Mathf.PerlinNoise(noiseX, noiseZ);
        }
        else
        {
            noiseX = x * xStep + transform.position.x - terrainSize.x / 2;
            noiseZ = z * zStep + transform.position.z - terrainSize.z / 2;
            value = VoronoiTiling.GetClosestCentroidValue(new Vector2Int((int)noiseX, (int)noiseZ), closestCentroids);
        }

        return Mathf.FloorToInt(value / (1.00f / biomeAmount));
    }

    /// <summary>
    /// Initialize biome edges with infinite values to find the biome edges.
    /// </summary>
    private void InitializeBiomeEdges()
    {
        int biomeAmount = TerrainController.Biomes.Length, vertexAmount = TerrainController.TileResolution;

        if (XEdgesPerYStep.Length != biomeAmount)
        {
            XEdgesPerYStep = new Vector2[biomeAmount][];
            YEdgesPerXStep = new Vector2[biomeAmount][];
        }

        for (int x = 0; x < XEdgesPerYStep.Length; x++)
        {
            if (XEdgesPerYStep[x] == null)
            {
                XEdgesPerYStep[x] = new Vector2[vertexAmount];
                YEdgesPerXStep[x] = new Vector2[vertexAmount];
            }

            for (int i = 0; i < xSegments + 1; i++)
            {
                if (XEdgesPerYStep[x][i] == null)
                {
                    XEdgesPerYStep[x][i] = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
                }
                else
                {
                    XEdgesPerYStep[x][i].x = float.PositiveInfinity;
                    XEdgesPerYStep[x][i].y = float.NegativeInfinity;
                }

                if (YEdgesPerXStep[x][i] == null)
                {
                    YEdgesPerXStep[x][i] = new Vector2(float.PositiveInfinity, float.NegativeInfinity);
                }
                else
                {
                    YEdgesPerXStep[x][i].x = float.PositiveInfinity;
                    YEdgesPerXStep[x][i].y = float.NegativeInfinity;
                }
            }
        }
    }

    /// <summary>
    /// Compare <paramref name="vertex"/> to edge values of <paramref name="biomeIndex"/> biome to find the biome's boundaries.
    /// </summary>
    private void CompareToBiomeEdges(Vector3 vertex, int biomeIndex)
    {
        // Compare vertex's Y(Z in 3D) value to others in its horizontal row(same X value)
        // If its the smallest or largest yet, update the old edge's X(min) or Y(max) accordingly
        int index = Mathf.RoundToInt(vertex.x / xStep);

        float min = YEdgesPerXStep[biomeIndex][index].x, max = YEdgesPerXStep[biomeIndex][index].y;

        if (vertex.z < min)
        {
            min = vertex.z;
        }

        if (vertex.z > max)
        {
            max = vertex.z;
        }

        YEdgesPerXStep[biomeIndex][index] = new Vector2(min, max);

        // Compare vertex's X value to other vertices in its vertical row(same Y value)
        // If its the smallest or largest yet, update the old edge's X(min) or Y(max) accordingly
        index = Mathf.RoundToInt(vertex.z / zStep);

        min = XEdgesPerYStep[biomeIndex][index].x;
        max = XEdgesPerYStep[biomeIndex][index].y;

        if (vertex.x < min)
        {
            min = vertex.x;
        }

        if (vertex.x > max)
        {
            max = vertex.x;
        }

        XEdgesPerYStep[biomeIndex][index] = new Vector2(min, max);
    }

    #endregion Helper Methods
}