using System.Collections.Generic;
using UnityEngine;
using Utility;

/// <summary>
/// Handles voronoi tiles for biome placement.
/// </summary>
public static class VoronoiTiling
{
	private readonly static Dictionary<Vector2Int, List<Centroid>> voronoiTiles = new Dictionary<Vector2Int, List<Centroid>>();
	private readonly static List<Vector2Int> voronoiTileKeys = new List<Vector2Int>();
	private static readonly List<Centroid> centroids = new List<Centroid>();
	private static Vector2Int tileSize;
	private static Vector2Int previoustargetIndex = new Vector2Int(int.MaxValue, int.MaxValue);
	private static Vector2Int biomeAmountPerTile = new Vector2Int(4, 6);

	/// <summary>
	/// Initialize voronoitiles and set size to <paramref name="tileSize"/>.
	/// </summary>
	/// <param name="tileSize">Voronoitile's new size.</param>
	public static void Initialize(Vector2Int tileSize)
	{
		voronoiTiles.Clear();
		voronoiTileKeys.Clear();
		centroids.Clear();

		VoronoiTiling.tileSize = tileSize;
	}

	/// <summary>
	/// Check 9(3x3 grid) of the closest voronoitiles around <paramref name="target"/> and create missing tiles.
	/// </summary>
	public static void GenerateMissingCentroids(Vector3 target)
	{
		// Get targets voronoitile index
		Vector2Int tileIndex, targetIndex = new Vector2Int(Mathf.FloorToInt(target.x / tileSize.x + .5f), Mathf.FloorToInt(target.z / tileSize.y + .5f));

		if (previoustargetIndex == targetIndex)
			return;
		else
			previoustargetIndex = targetIndex;

		// Check the 3x3 grid around playertile for voronoitiles
		for (int x = targetIndex.x - 1; x < targetIndex.x + 2; x++)
		{
			for (int y = targetIndex.y - 1; y < targetIndex.y + 2; y++)
			{
				// If a voronoitile doesn't exist => create it
				if(!voronoiTiles.ContainsKey(tileIndex = new Vector2Int(x, y)))
				{
					voronoiTiles.Add(tileIndex, new List<Centroid>());
					voronoiTileKeys.Add(tileIndex);

					int amount = Random.Range(biomeAmountPerTile.x, biomeAmountPerTile.y);
					// Generate biomeAmountPerTile amount of centroids(random points) within the voronoitile boundaries
					for (int i = 0; i < amount; i++)
					{
						voronoiTiles[tileIndex].Add(new Centroid(new Vector2Int(
							Random.Range(-tileSize.x / 2 + x * tileSize.x, tileSize.x / 2 + x * tileSize.x)
							, Random.Range(-tileSize.y / 2 + y * tileSize.y, tileSize.y / 2 + y * tileSize.y))
							, Random.Range(0.00f, 1.00f)
							));
					}
				}
			}
		}
	}

	/// <summary>
	/// Get grayscale value of the closest centroid to <paramref name="pixelPos"/> from <paramref name="closestCentroids"/>.
	/// </summary>
	/// <param name="pixelPos">2D position of the vertex.</param>
	/// <param name="closestCentroids">List of the closest centroids the tile.</param>
	public static float GetClosestCentroidValue(Vector2 pixelPos, List<Centroid> closestCentroids)
	{
		float smallestDst = float.MaxValue;
		float value = 0;
		for(int i = 0; i < closestCentroids.Count; i++)
		{
			if ((pixelPos - closestCentroids[i].Position).sqrMagnitude < smallestDst)
			{
				smallestDst = (pixelPos - closestCentroids[i].Position).sqrMagnitude;
				value = closestCentroids[i].GrayScaleValue;
			}
		}

		return value;
	}

	// Get a list of centroids from the 9 closest voronoi tiles (3x3 grid around tile).
	// Every vertex has to go through the amount returned from this method to find the closest.
	// If the amount is too low vertices with same position on different tiles
	// may get different biome indices which eliminates smooth blending between colors = artifacts
	/// <summary>
	/// Get a list of <paramref name="centroidAmount"/> centroids from the 9 closest voronoi tiles to <paramref name="tilePos"/>.
	/// </summary>
	/// <param name="tilePos">Tiles 3D world position</param>
	/// <param name="centroidAmount">Amount of centroids to be returned.</param>
	public static List<Centroid> GetClosestCentroids(Vector3 tilePos, int centroidAmount)
	{
		Vector2 vector2TilePos = new Vector2(tilePos.x / tileSize.x, tilePos.z / tileSize.y);

		// Sort the list based on distances to the tile
		voronoiTileKeys.SortByDistance2D(x => x, vector2TilePos);

		centroids.Clear();

		for (int i = 0; i < 9; i++)
		{
			centroids.AddRange(voronoiTiles[voronoiTileKeys[i]]);
		}

		// Get the centroidAmount of closest centroids from the list
		Vector2 vector2PixelPos = new Vector2(tilePos.x, tilePos.z);

		// Quicksort would be way more optimal for this mostly unsorted list
		centroids.SortByDistance2D(x => x.Position, vector2PixelPos);

		return centroids.GetRange(0, centroidAmount);
	}
}

public class Centroid
{
	public Vector2Int Position { get; set; }
	public float GrayScaleValue { get; set; }

	public Centroid(Vector2Int position, float grayScaleValue)
	{
		Position = position;
		GrayScaleValue = grayScaleValue;
	}
}