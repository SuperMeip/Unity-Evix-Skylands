﻿using Evix.Terrain.Collections;
using Evix.Voxels;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Evix.Terrain.MeshGeneration {

  /// <summary>
  /// Generates a collection of voxel vertex data into a marching cubes mesh
  /// </summary>
  public static class MarchingTetsMeshGenerator {

    /// <summary>
    /// The size of a block in world space
    /// </summary>
    public const float BlockSize = 1.0f;

    /// <summary>
    /// The diameter of the area we march over
    /// </summary>
    public const int MarchDiameter = Chunk.Diameter + 1;

    /// <summary>
    /// The chunk neighbors needed to be loaded to succesfully march gen a chunk mesh
    /// </summary>
    public static Coordinate[] NecessaryLoadedChunkNeighborOffsets = new Coordinate[] {
      Directions.East.Offset,
      Directions.Above.Offset,
      Directions.North.Offset,
      Directions.East.Offset + Directions.North.Offset,
      Directions.Above.Offset + Directions.North.Offset,
      Directions.East.Offset + Directions.Above.Offset,
      Directions.East.Offset + Directions.North.Offset + Directions.Above.Offset
    };

    /// <summary>
    /// Get the voxels we need to march over the given chunk as a byte array
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static byte[] GetVoxelsToMarchOver(Chunk.ID chunkID, Level level) {
      byte[] voxels = new byte[MarchDiameter * MarchDiameter * MarchDiameter]; 
      Coordinate chunkWorldLocation = chunkID.toWorldLocation();
      //@todo: move this data get into some prep step in the Manager Thread.
      chunkWorldLocation.until(chunkWorldLocation + MarchDiameter, (worldLocation) => {
        voxels[(worldLocation - chunkWorldLocation).flatten(MarchDiameter)] = level.getVoxel(worldLocation);
      });

      return voxels;
    }

    /// <summary>
    /// Create a new marching cubes generation job for the given chunk in a level
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static MarchingTetsMeshGenJob GetJob(byte[] voxelsToMarchOver) {
      return new MarchingTetsMeshGenJob(voxelsToMarchOver);
    }

    /// <summary>
    /// Get information about the neightboring chunks this generator needs loaded for this chunk to be meshed properly
    /// </summary>
    /// <returns></returns>
    public static (bool necessaryNeighborsAreLoaded, bool blockingNeighborsAreSolid) GetRequiredChunkNeighborStats(Chunk.ID chunkID, Level level) {
      bool allNeighborsAreLoaded = true;
      bool allNeighborsAreSolid = true;
      foreach (Coordinate neighborOffset in NecessaryLoadedChunkNeighborOffsets) {
        /// break and return if both are already false
        if (!allNeighborsAreLoaded && !allNeighborsAreSolid) {
          return (false, false);
        }

        Chunk.ID neighborKey = chunkID.Coordinate + neighborOffset;
        // if the neighbor is out of bounds, it counts as loaded.
        if (neighborKey.Coordinate.isWithin(Coordinate.Zero, level.chunkBounds)) {
          // if the coordinate is in bounds, and we can't find it in the dictionary or it's not loaded, then it can't be loaded or solid
          if (!level.chunks.TryGetValue(neighborKey, out Chunk neighbor)) {
            return (false, false);
          // if we find the chunk check for both
          } else {
            bool isLoaded = neighbor.isLoaded;
            bool isSolid = neighbor.isSolid;
            // if neigther are true, return.
            if (!isLoaded && !isSolid) {
              return (false, false);
            }

            // set whichever one was false to false, don't override a false with true.
            allNeighborsAreLoaded = !isLoaded ? isLoaded : allNeighborsAreLoaded;
            allNeighborsAreSolid = !isSolid ? isSolid : allNeighborsAreSolid;
          }
        // if it's not within chunk bounds, the neighbor can't be solid
        } else {
          allNeighborsAreSolid = false;
        }
      }

      return (allNeighborsAreLoaded, allNeighborsAreSolid);
    }

    /// <summary>
    /// Get if the nessisary neighbors of the current chunk have been loaded in the given level
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public static bool NecessaryChunkNeighborsAreLoaded(Chunk.ID chunkID, Level level) {
      foreach (Coordinate neighborOffset in NecessaryLoadedChunkNeighborOffsets) {
        Chunk.ID neighborKey = chunkID.Coordinate + neighborOffset;
        // if the neighbor is out of bounds, we return true because it will never be loaded
        // if the coordinate is in bounds, and we can't find it in the dictionary or it's not loaded, then we return false.
        if (neighborKey.Coordinate.isWithin(Coordinate.Zero, level.chunkBounds)
          && (!level.chunks.TryGetValue(neighborKey, out Chunk neighbor) || !neighbor.isLoaded)
        ) {
          return false;
        }
      }

      return true;
    }

    /// <summary>
    /// Get if the nessisary neighbors of the current chunk have been loaded in the given level
    /// We should check all NeighborOffsetsThatNeedThisChunkLoaded to see if there's a single chunk that's loaded with data.
    /// If there's no loaded chunk neighbors that have any blocks, we can drop this chunk
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public static bool IsANessisaryChunkNeighbor(Chunk.ID chunkID, Level level) {
      foreach (Coordinate neighborOffset in NecessaryLoadedChunkNeighborOffsets) {
        Chunk.ID neighborKey = chunkID.Coordinate + neighborOffset;
        // if the neighbor is within the chunk radius we need to check it, if not, it can be considered empty
        if (neighborKey.Coordinate.isWithin(Coordinate.Zero, level.chunkBounds)) {
          // if we find the chunk and it's empty we need to check if it's empty and loaded.
          if (level.chunks.TryGetValue(neighborKey, out Chunk neighbor)) {
            if (neighbor.isLoaded) {
              // if the neighbor is loaded and isn't empty, we need this chunk to be meshed.
              if (!neighbor.isEmpty) {
                return true;
              }
            // if the neighboring chunk isn't loaded yet, we can't say for sure that this chunk isn't needed.
            } else {
              return true;
            }
          // if the neighboring chunk isn't loaded yet, we can't say for sure that this chunk isn't needed.
          } else {
            return true;
          }
        }
      }

      return false;
    }

    /// <summary>
    /// Check if all of the neighbors that block visibility of the current chunkID(when its solid) are solid.
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static bool AllBlockingNeighborsAreSolid(Chunk.ID chunkID, Level level) {
      foreach (Coordinate neighborOffset in NecessaryLoadedChunkNeighborOffsets) {
        Chunk.ID neighborKey = chunkID.Coordinate + neighborOffset;
        // if the neighbor is out of bounds, it can't be solid.
        // If it's not found, it's not loaded and we can't say it's solid.
        if (!neighborKey.Coordinate.isWithin(Coordinate.Zero, level.chunkBounds) || !level.chunks.TryGetValue(neighborKey, out Chunk neighbor) || !neighbor.isSolid) {
          return false;
        }
      }

      return true;
    }

    ///// SUB FUNCTIONS

    /// <summary>
    /// March over the set of blocks and generate the needed mesh data
    /// </summary>
    /// <param name="voxels">The voxels to itterate over</param>
    /// <returns></returns>
    static void MarchOverVoxels(
      NativeArray<byte> voxels,
      out List<Vector3> outVerticies,
      out List<int> outTriangles,
      out List<Color> outColors
    ) {
      int vertexIndex = 0;
      Vector3[] blockEdgeVerticies = new Vector3[12];
      Color[] blockEdgeVertexUVValues = new Color[12];
      List<Vector3> verticies = new List<Vector3>();
      List<int> triangles = new List<int>();
      List<Color> colors = new List<Color>();

      // March over each point
      Coordinate.Zero.until(new Coordinate(Chunk.Diameter), (currentVoxelLocation) => {
        int blockMask = 0;
        IVoxelType[] blockTypes = new IVoxelType[Octants.All.Length];
        Vector3[] blockVertexLocations = new Vector3[Octants.All.Length];

        // get all 8 vertex's data for this block.
        foreach (Octants.Octant octant in Octants.All) {
          Coordinate blockVertexLocation = currentVoxelLocation + octant.Offset;
          blockVertexLocations[octant.Value] = blockVertexLocation.vec3 * BlockSize;
          blockTypes[octant.Value] = TerrainBlock.Types.Get(voxels[blockVertexLocation.flatten(MarchDiameter)]);

          // if this vertex is solid, mark it
          if (blockTypes[octant.Value].IsSolid) {
            blockMask = blockMask.SetVertexMaskForOctant(octant);
          }
        }


        // for each edge of the block for just this point
        //   (the block for this point is the block who's WestBottomSouth most vertex's xyz
        //     is equal to the point's xyz)
        int edgeMask = LookupTables.EdgeTable[blockMask];
        for (int i = 0; i < 12; i++) {
          // if this blocks isosurface passes through edge i/12 on the box
          if ((edgeMask & (1 << i)) != 0) {
            // for each edge, get the pair of points that that edge connects
            int[] edgeVerticies = LookupTables.EdgeIndexTable[i];

            //interpolate between them to determine the location of this vertex
            blockEdgeVerticies[i] = VertexInterpolate(
              blockVertexLocations[edgeVerticies[0]],
              blockVertexLocations[edgeVerticies[1]]
            );

            // try to get color data from the terrain types
            blockEdgeVertexUVValues[i] = UVInterpolate(
              blockTypes[edgeVerticies[0]],
              blockTypes[edgeVerticies[1]]
            );
          }
        }

        // Get the correct row of triangles, and iterate over the vertex data
        // to store it in the mesh arrays
        int[] rowOfTriangles = LookupTables.TriangleTable[blockMask];
        for (int currentTriangleStartPointIndex = 0; currentTriangleStartPointIndex < rowOfTriangles.Length; currentTriangleStartPointIndex += 3) {
          verticies.Add(blockEdgeVerticies[rowOfTriangles[currentTriangleStartPointIndex + 0]]);
          colors.Add(blockEdgeVertexUVValues[rowOfTriangles[currentTriangleStartPointIndex + 0]]);
          triangles.Add(vertexIndex);
          vertexIndex++;

          verticies.Add(blockEdgeVerticies[rowOfTriangles[currentTriangleStartPointIndex + 1]]);
          colors.Add(blockEdgeVertexUVValues[rowOfTriangles[currentTriangleStartPointIndex + 1]]);
          triangles.Add(vertexIndex);
          vertexIndex++;

          verticies.Add(blockEdgeVerticies[rowOfTriangles[currentTriangleStartPointIndex + 2]]);
          colors.Add(blockEdgeVertexUVValues[rowOfTriangles[currentTriangleStartPointIndex + 2]]);
          triangles.Add(vertexIndex);
          vertexIndex++;
        }
      });

      /// Set the output values for the job
      outVerticies = verticies;
      outTriangles = triangles;
      outColors    = colors;
    }

    /// <summary>
    /// Lerp between two colors
    /// </summary>
    /// <param name="voxelTypeOne"></param>
    /// <param name="voxelTypeTwo"></param>
    /// <returns></returns>
    static Color UVInterpolate(IVoxelType voxelTypeOne, IVoxelType voxelTypeTwo) {
      // if either of the corners is the non solid one, use the other corner's color
      if (!voxelTypeOne.IsSolid) {
        return voxelTypeTwo.Color;
      }
      if (!voxelTypeTwo.IsSolid) {
        return voxelTypeOne.Color;
      }
      return Color.Lerp(voxelTypeOne.Color, voxelTypeTwo.Color, 0.5f);
    }

    /// <summary>
    /// Vertex interpolation between the scalars of two points on one edge
    /// </summary>
    /// <param name="locationP1"></param>
    /// <param name="locationP2"></param>
    /// <param name="valueP1"></param>
    /// <param name="valueP2"></param>
    /// <returns></returns>
    static Vector3 VertexInterpolate(Vector3 edgePointOne, Vector3 edgePointTwo) {
      return edgePointOne + 0.5f * (edgePointTwo - edgePointOne);
    }

    /// <summary>
    /// A unity parallel job to run to generate the marched mesh values.
    /// </summary>
    public struct MarchingTetsMeshGenJob : IJob {

      /// <summary>
      /// The input collection of voxels as a native array
      /// </summary>
      [DeallocateOnJobCompletion]
      readonly NativeArray<byte> inVoxels;

      /// <summary>
      /// The verts output by this job
      /// </summary>
      public NativeList<Vector3> outVerticies;

      /// <summary>
      /// The output triangles
      /// </summary>
      public NativeList<int> outTriangles;

      /// <summary>
      /// The output colors
      /// </summary>
      public NativeList<Color> outColors;

      /// <summary>
      /// Make a new mesh gen job
      /// </summary>
      /// <param name="voxelsToMarchOver"></param>
      internal MarchingTetsMeshGenJob(byte[] voxelsToMarchOver) {
        inVoxels = new NativeArray<byte>(MarchDiameter * MarchDiameter * MarchDiameter, Allocator.Persistent);
        inVoxels.CopyFrom(voxelsToMarchOver);
        outVerticies = new NativeList<Vector3>(3000, Allocator.Persistent);
        outTriangles = new NativeList<int>(3000, Allocator.Persistent);
        outColors = new NativeList<Color>(3000, Allocator.Persistent);
      }

      /// <summary>
      /// Execute the job
      /// </summary>
      public void Execute() {
        MarchOverVoxels(
          inVoxels,
          out List<Vector3> generatedVerticies,
          out List<int> generatedTriangles,
          out List<Color> generatedColors
        );

        outVerticies.CopyFrom(generatedVerticies.ToArray());
        outTriangles.CopyFrom(generatedTriangles.ToArray());
        outColors.CopyFrom(generatedColors.ToArray());
      }
    }

    /// <summary>
    /// Marching cube lookup data
    /// </summary>
    static class LookupTables {
      /// <summary>
      /// The 12 edges of a block in order, defined by the values of the octants of the vertexes they connect
      /// </summary>
      public static readonly int[][] EdgeIndexTable = {
        new[] {0, 1},
        new[] {1, 2},
        new[] {2, 3},
        new[] {3, 0},
        new[] {4, 5},
        new[] {5, 6},
        new[] {6, 7},
        new[] {7, 4},
        new[] {0, 4},
        new[] {1, 5},
        new[] {2, 6},
        new[] {3, 7}
    };

      /// <summary>
      /// A table mapping the 8 bit vertex bitmask of a block, to a 12 bit mask of which edges of
      /// the block the isosurface must pass through for those solid vertexes.
      /// </summary>
      public static readonly int[] EdgeTable = {
        0x0, 0x109, 0x203, 0x30a, 0x406, 0x50f, 0x605, 0x70c,
        0x80c, 0x905, 0xa0f, 0xb06, 0xc0a, 0xd03, 0xe09, 0xf00,
        0x190, 0x99, 0x393, 0x29a, 0x596, 0x49f, 0x795, 0x69c,
        0x99c, 0x895, 0xb9f, 0xa96, 0xd9a, 0xc93, 0xf99, 0xe90,
        0x230, 0x339, 0x33, 0x13a, 0x636, 0x73f, 0x435, 0x53c,
        0xa3c, 0xb35, 0x83f, 0x936, 0xe3a, 0xf33, 0xc39, 0xd30,
        0x3a0, 0x2a9, 0x1a3, 0xaa, 0x7a6, 0x6af, 0x5a5, 0x4ac,
        0xbac, 0xaa5, 0x9af, 0x8a6, 0xfaa, 0xea3, 0xda9, 0xca0,
        0x460, 0x569, 0x663, 0x76a, 0x66, 0x16f, 0x265, 0x36c,
        0xc6c, 0xd65, 0xe6f, 0xf66, 0x86a, 0x963, 0xa69, 0xb60,
        0x5f0, 0x4f9, 0x7f3, 0x6fa, 0x1f6, 0xff, 0x3f5, 0x2fc,
        0xdfc, 0xcf5, 0xfff, 0xef6, 0x9fa, 0x8f3, 0xbf9, 0xaf0,
        0x650, 0x759, 0x453, 0x55a, 0x256, 0x35f, 0x55, 0x15c,
        0xe5c, 0xf55, 0xc5f, 0xd56, 0xa5a, 0xb53, 0x859, 0x950,
        0x7c0, 0x6c9, 0x5c3, 0x4ca, 0x3c6, 0x2cf, 0x1c5, 0xcc,
        0xfcc, 0xec5, 0xdcf, 0xcc6, 0xbca, 0xac3, 0x9c9, 0x8c0,
        0x8c0, 0x9c9, 0xac3, 0xbca, 0xcc6, 0xdcf, 0xec5, 0xfcc,
        0xcc, 0x1c5, 0x2cf, 0x3c6, 0x4ca, 0x5c3, 0x6c9, 0x7c0,
        0x950, 0x859, 0xb53, 0xa5a, 0xd56, 0xc5f, 0xf55, 0xe5c,
        0x15c, 0x55, 0x35f, 0x256, 0x55a, 0x453, 0x759, 0x650,
        0xaf0, 0xbf9, 0x8f3, 0x9fa, 0xef6, 0xfff, 0xcf5, 0xdfc,
        0x2fc, 0x3f5, 0xff, 0x1f6, 0x6fa, 0x7f3, 0x4f9, 0x5f0,
        0xb60, 0xa69, 0x963, 0x86a, 0xf66, 0xe6f, 0xd65, 0xc6c,
        0x36c, 0x265, 0x16f, 0x66, 0x76a, 0x663, 0x569, 0x460,
        0xca0, 0xda9, 0xea3, 0xfaa, 0x8a6, 0x9af, 0xaa5, 0xbac,
        0x4ac, 0x5a5, 0x6af, 0x7a6, 0xaa, 0x1a3, 0x2a9, 0x3a0,
        0xd30, 0xc39, 0xf33, 0xe3a, 0x936, 0x83f, 0xb35, 0xa3c,
        0x53c, 0x435, 0x73f, 0x636, 0x13a, 0x33, 0x339, 0x230,
        0xe90, 0xf99, 0xc93, 0xd9a, 0xa96, 0xb9f, 0x895, 0x99c,
        0x69c, 0x795, 0x49f, 0x596, 0x29a, 0x393, 0x99, 0x190,
        0xf00, 0xe09, 0xd03, 0xc0a, 0xb06, 0xa0f, 0x905, 0x80c,
        0x70c, 0x605, 0x50f, 0x406, 0x30a, 0x203, 0x109, 0x0
    };

      /// <summary>
      /// Triangle values for each valid shape in the marching cubes mesh.
      /// Indexed by an 8 bit mask of which vertexes of the block are solid, based on octant value
      /// </summary>
      public static readonly int[][] TriangleTable = {
        new int[] { },
        new[] {0, 8, 3},
        new[] {0, 1, 9},
        new[] {1, 8, 3, 9, 8, 1},
        new[] {1, 2, 10},
        new[] {0, 8, 3, 1, 2, 10},
        new[] {9, 2, 10, 0, 2, 9},
        new[] {2, 8, 3, 2, 10, 8, 10, 9, 8},
        new[] {3, 11, 2},
        new[] {0, 11, 2, 8, 11, 0},
        new[] {1, 9, 0, 2, 3, 11},
        new[] {1, 11, 2, 1, 9, 11, 9, 8, 11},
        new[] {3, 10, 1, 11, 10, 3},
        new[] {0, 10, 1, 0, 8, 10, 8, 11, 10},
        new[] {3, 9, 0, 3, 11, 9, 11, 10, 9},
        new[] {9, 8, 10, 10, 8, 11},
        new[] {4, 7, 8},
        new[] {4, 3, 0, 7, 3, 4},
        new[] {0, 1, 9, 8, 4, 7},
        new[] {4, 1, 9, 4, 7, 1, 7, 3, 1},
        new[] {1, 2, 10, 8, 4, 7},
        new[] {3, 4, 7, 3, 0, 4, 1, 2, 10},
        new[] {9, 2, 10, 9, 0, 2, 8, 4, 7},
        new[] {2, 10, 9, 2, 9, 7, 2, 7, 3, 7, 9, 4},
        new[] {8, 4, 7, 3, 11, 2},
        new[] {11, 4, 7, 11, 2, 4, 2, 0, 4},
        new[] {9, 0, 1, 8, 4, 7, 2, 3, 11},
        new[] {4, 7, 11, 9, 4, 11, 9, 11, 2, 9, 2, 1},
        new[] {3, 10, 1, 3, 11, 10, 7, 8, 4},
        new[] {1, 11, 10, 1, 4, 11, 1, 0, 4, 7, 11, 4},
        new[] {4, 7, 8, 9, 0, 11, 9, 11, 10, 11, 0, 3},
        new[] {4, 7, 11, 4, 11, 9, 9, 11, 10},
        new[] {9, 5, 4},
        new[] {9, 5, 4, 0, 8, 3},
        new[] {0, 5, 4, 1, 5, 0},
        new[] {8, 5, 4, 8, 3, 5, 3, 1, 5},
        new[] {1, 2, 10, 9, 5, 4},
        new[] {3, 0, 8, 1, 2, 10, 4, 9, 5},
        new[] {5, 2, 10, 5, 4, 2, 4, 0, 2},
        new[] {2, 10, 5, 3, 2, 5, 3, 5, 4, 3, 4, 8},
        new[] {9, 5, 4, 2, 3, 11},
        new[] {0, 11, 2, 0, 8, 11, 4, 9, 5},
        new[] {0, 5, 4, 0, 1, 5, 2, 3, 11},
        new[] {2, 1, 5, 2, 5, 8, 2, 8, 11, 4, 8, 5},
        new[] {10, 3, 11, 10, 1, 3, 9, 5, 4},
        new[] {4, 9, 5, 0, 8, 1, 8, 10, 1, 8, 11, 10},
        new[] {5, 4, 0, 5, 0, 11, 5, 11, 10, 11, 0, 3},
        new[] {5, 4, 8, 5, 8, 10, 10, 8, 11},
        new[] {9, 7, 8, 5, 7, 9},
        new[] {9, 3, 0, 9, 5, 3, 5, 7, 3},
        new[] {0, 7, 8, 0, 1, 7, 1, 5, 7},
        new[] {1, 5, 3, 3, 5, 7},
        new[] {9, 7, 8, 9, 5, 7, 10, 1, 2},
        new[] {10, 1, 2, 9, 5, 0, 5, 3, 0, 5, 7, 3},
        new[] {8, 0, 2, 8, 2, 5, 8, 5, 7, 10, 5, 2},
        new[] {2, 10, 5, 2, 5, 3, 3, 5, 7},
        new[] {7, 9, 5, 7, 8, 9, 3, 11, 2},
        new[] {9, 5, 7, 9, 7, 2, 9, 2, 0, 2, 7, 11},
        new[] {2, 3, 11, 0, 1, 8, 1, 7, 8, 1, 5, 7},
        new[] {11, 2, 1, 11, 1, 7, 7, 1, 5},
        new[] {9, 5, 8, 8, 5, 7, 10, 1, 3, 10, 3, 11},
        new[] {5, 7, 0, 5, 0, 9, 7, 11, 0, 1, 0, 10, 11, 10, 0},
        new[] {11, 10, 0, 11, 0, 3, 10, 5, 0, 8, 0, 7, 5, 7, 0},
        new[] {11, 10, 5, 7, 11, 5},
        new[] {10, 6, 5},
        new[] {0, 8, 3, 5, 10, 6},
        new[] {9, 0, 1, 5, 10, 6},
        new[] {1, 8, 3, 1, 9, 8, 5, 10, 6},
        new[] {1, 6, 5, 2, 6, 1},
        new[] {1, 6, 5, 1, 2, 6, 3, 0, 8},
        new[] {9, 6, 5, 9, 0, 6, 0, 2, 6},
        new[] {5, 9, 8, 5, 8, 2, 5, 2, 6, 3, 2, 8},
        new[] {2, 3, 11, 10, 6, 5},
        new[] {11, 0, 8, 11, 2, 0, 10, 6, 5},
        new[] {0, 1, 9, 2, 3, 11, 5, 10, 6},
        new[] {5, 10, 6, 1, 9, 2, 9, 11, 2, 9, 8, 11},
        new[] {6, 3, 11, 6, 5, 3, 5, 1, 3},
        new[] {0, 8, 11, 0, 11, 5, 0, 5, 1, 5, 11, 6},
        new[] {3, 11, 6, 0, 3, 6, 0, 6, 5, 0, 5, 9},
        new[] {6, 5, 9, 6, 9, 11, 11, 9, 8},
        new[] {5, 10, 6, 4, 7, 8},
        new[] {4, 3, 0, 4, 7, 3, 6, 5, 10},
        new[] {1, 9, 0, 5, 10, 6, 8, 4, 7},
        new[] {10, 6, 5, 1, 9, 7, 1, 7, 3, 7, 9, 4},
        new[] {6, 1, 2, 6, 5, 1, 4, 7, 8},
        new[] {1, 2, 5, 5, 2, 6, 3, 0, 4, 3, 4, 7},
        new[] {8, 4, 7, 9, 0, 5, 0, 6, 5, 0, 2, 6},
        new[] {7, 3, 9, 7, 9, 4, 3, 2, 9, 5, 9, 6, 2, 6, 9},
        new[] {3, 11, 2, 7, 8, 4, 10, 6, 5},
        new[] {5, 10, 6, 4, 7, 2, 4, 2, 0, 2, 7, 11},
        new[] {0, 1, 9, 4, 7, 8, 2, 3, 11, 5, 10, 6},
        new[] {9, 2, 1, 9, 11, 2, 9, 4, 11, 7, 11, 4, 5, 10, 6},
        new[] {8, 4, 7, 3, 11, 5, 3, 5, 1, 5, 11, 6},
        new[] {5, 1, 11, 5, 11, 6, 1, 0, 11, 7, 11, 4, 0, 4, 11},
        new[] {0, 5, 9, 0, 6, 5, 0, 3, 6, 11, 6, 3, 8, 4, 7},
        new[] {6, 5, 9, 6, 9, 11, 4, 7, 9, 7, 11, 9},
        new[] {10, 4, 9, 6, 4, 10},
        new[] {4, 10, 6, 4, 9, 10, 0, 8, 3},
        new[] {10, 0, 1, 10, 6, 0, 6, 4, 0},
        new[] {8, 3, 1, 8, 1, 6, 8, 6, 4, 6, 1, 10},
        new[] {1, 4, 9, 1, 2, 4, 2, 6, 4},
        new[] {3, 0, 8, 1, 2, 9, 2, 4, 9, 2, 6, 4},
        new[] {0, 2, 4, 4, 2, 6},
        new[] {8, 3, 2, 8, 2, 4, 4, 2, 6},
        new[] {10, 4, 9, 10, 6, 4, 11, 2, 3},
        new[] {0, 8, 2, 2, 8, 11, 4, 9, 10, 4, 10, 6},
        new[] {3, 11, 2, 0, 1, 6, 0, 6, 4, 6, 1, 10},
        new[] {6, 4, 1, 6, 1, 10, 4, 8, 1, 2, 1, 11, 8, 11, 1},
        new[] {9, 6, 4, 9, 3, 6, 9, 1, 3, 11, 6, 3},
        new[] {8, 11, 1, 8, 1, 0, 11, 6, 1, 9, 1, 4, 6, 4, 1},
        new[] {3, 11, 6, 3, 6, 0, 0, 6, 4},
        new[] {6, 4, 8, 11, 6, 8},
        new[] {7, 10, 6, 7, 8, 10, 8, 9, 10},
        new[] {0, 7, 3, 0, 10, 7, 0, 9, 10, 6, 7, 10},
        new[] {10, 6, 7, 1, 10, 7, 1, 7, 8, 1, 8, 0},
        new[] {10, 6, 7, 10, 7, 1, 1, 7, 3},
        new[] {1, 2, 6, 1, 6, 8, 1, 8, 9, 8, 6, 7},
        new[] {2, 6, 9, 2, 9, 1, 6, 7, 9, 0, 9, 3, 7, 3, 9},
        new[] {7, 8, 0, 7, 0, 6, 6, 0, 2},
        new[] {7, 3, 2, 6, 7, 2},
        new[] {2, 3, 11, 10, 6, 8, 10, 8, 9, 8, 6, 7},
        new[] {2, 0, 7, 2, 7, 11, 0, 9, 7, 6, 7, 10, 9, 10, 7},
        new[] {1, 8, 0, 1, 7, 8, 1, 10, 7, 6, 7, 10, 2, 3, 11},
        new[] {11, 2, 1, 11, 1, 7, 10, 6, 1, 6, 7, 1},
        new[] {8, 9, 6, 8, 6, 7, 9, 1, 6, 11, 6, 3, 1, 3, 6},
        new[] {0, 9, 1, 11, 6, 7},
        new[] {7, 8, 0, 7, 0, 6, 3, 11, 0, 11, 6, 0},
        new[] {7, 11, 6},
        new[] {7, 6, 11},
        new[] {3, 0, 8, 11, 7, 6},
        new[] {0, 1, 9, 11, 7, 6},
        new[] {8, 1, 9, 8, 3, 1, 11, 7, 6},
        new[] {10, 1, 2, 6, 11, 7},
        new[] {1, 2, 10, 3, 0, 8, 6, 11, 7},
        new[] {2, 9, 0, 2, 10, 9, 6, 11, 7},
        new[] {6, 11, 7, 2, 10, 3, 10, 8, 3, 10, 9, 8},
        new[] {7, 2, 3, 6, 2, 7},
        new[] {7, 0, 8, 7, 6, 0, 6, 2, 0},
        new[] {2, 7, 6, 2, 3, 7, 0, 1, 9},
        new[] {1, 6, 2, 1, 8, 6, 1, 9, 8, 8, 7, 6},
        new[] {10, 7, 6, 10, 1, 7, 1, 3, 7},
        new[] {10, 7, 6, 1, 7, 10, 1, 8, 7, 1, 0, 8},
        new[] {0, 3, 7, 0, 7, 10, 0, 10, 9, 6, 10, 7},
        new[] {7, 6, 10, 7, 10, 8, 8, 10, 9},
        new[] {6, 8, 4, 11, 8, 6},
        new[] {3, 6, 11, 3, 0, 6, 0, 4, 6},
        new[] {8, 6, 11, 8, 4, 6, 9, 0, 1},
        new[] {9, 4, 6, 9, 6, 3, 9, 3, 1, 11, 3, 6},
        new[] {6, 8, 4, 6, 11, 8, 2, 10, 1},
        new[] {1, 2, 10, 3, 0, 11, 0, 6, 11, 0, 4, 6},
        new[] {4, 11, 8, 4, 6, 11, 0, 2, 9, 2, 10, 9},
        new[] {10, 9, 3, 10, 3, 2, 9, 4, 3, 11, 3, 6, 4, 6, 3},
        new[] {8, 2, 3, 8, 4, 2, 4, 6, 2},
        new[] {0, 4, 2, 4, 6, 2},
        new[] {1, 9, 0, 2, 3, 4, 2, 4, 6, 4, 3, 8},
        new[] {1, 9, 4, 1, 4, 2, 2, 4, 6},
        new[] {8, 1, 3, 8, 6, 1, 8, 4, 6, 6, 10, 1},
        new[] {10, 1, 0, 10, 0, 6, 6, 0, 4},
        new[] {4, 6, 3, 4, 3, 8, 6, 10, 3, 0, 3, 9, 10, 9, 3},
        new[] {10, 9, 4, 6, 10, 4},
        new[] {4, 9, 5, 7, 6, 11},
        new[] {0, 8, 3, 4, 9, 5, 11, 7, 6},
        new[] {5, 0, 1, 5, 4, 0, 7, 6, 11},
        new[] {11, 7, 6, 8, 3, 4, 3, 5, 4, 3, 1, 5},
        new[] {9, 5, 4, 10, 1, 2, 7, 6, 11},
        new[] {6, 11, 7, 1, 2, 10, 0, 8, 3, 4, 9, 5},
        new[] {7, 6, 11, 5, 4, 10, 4, 2, 10, 4, 0, 2},
        new[] {3, 4, 8, 3, 5, 4, 3, 2, 5, 10, 5, 2, 11, 7, 6},
        new[] {7, 2, 3, 7, 6, 2, 5, 4, 9},
        new[] {9, 5, 4, 0, 8, 6, 0, 6, 2, 6, 8, 7},
        new[] {3, 6, 2, 3, 7, 6, 1, 5, 0, 5, 4, 0},
        new[] {6, 2, 8, 6, 8, 7, 2, 1, 8, 4, 8, 5, 1, 5, 8},
        new[] {9, 5, 4, 10, 1, 6, 1, 7, 6, 1, 3, 7},
        new[] {1, 6, 10, 1, 7, 6, 1, 0, 7, 8, 7, 0, 9, 5, 4},
        new[] {4, 0, 10, 4, 10, 5, 0, 3, 10, 6, 10, 7, 3, 7, 10},
        new[] {7, 6, 10, 7, 10, 8, 5, 4, 10, 4, 8, 10},
        new[] {6, 9, 5, 6, 11, 9, 11, 8, 9},
        new[] {3, 6, 11, 0, 6, 3, 0, 5, 6, 0, 9, 5},
        new[] {0, 11, 8, 0, 5, 11, 0, 1, 5, 5, 6, 11},
        new[] {6, 11, 3, 6, 3, 5, 5, 3, 1},
        new[] {1, 2, 10, 9, 5, 11, 9, 11, 8, 11, 5, 6},
        new[] {0, 11, 3, 0, 6, 11, 0, 9, 6, 5, 6, 9, 1, 2, 10},
        new[] {11, 8, 5, 11, 5, 6, 8, 0, 5, 10, 5, 2, 0, 2, 5},
        new[] {6, 11, 3, 6, 3, 5, 2, 10, 3, 10, 5, 3},
        new[] {5, 8, 9, 5, 2, 8, 5, 6, 2, 3, 8, 2},
        new[] {9, 5, 6, 9, 6, 0, 0, 6, 2},
        new[] {1, 5, 8, 1, 8, 0, 5, 6, 8, 3, 8, 2, 6, 2, 8},
        new[] {1, 5, 6, 2, 1, 6},
        new[] {1, 3, 6, 1, 6, 10, 3, 8, 6, 5, 6, 9, 8, 9, 6},
        new[] {10, 1, 0, 10, 0, 6, 9, 5, 0, 5, 6, 0},
        new[] {0, 3, 8, 5, 6, 10},
        new[] {10, 5, 6},
        new[] {11, 5, 10, 7, 5, 11},
        new[] {11, 5, 10, 11, 7, 5, 8, 3, 0},
        new[] {5, 11, 7, 5, 10, 11, 1, 9, 0},
        new[] {10, 7, 5, 10, 11, 7, 9, 8, 1, 8, 3, 1},
        new[] {11, 1, 2, 11, 7, 1, 7, 5, 1},
        new[] {0, 8, 3, 1, 2, 7, 1, 7, 5, 7, 2, 11},
        new[] {9, 7, 5, 9, 2, 7, 9, 0, 2, 2, 11, 7},
        new[] {7, 5, 2, 7, 2, 11, 5, 9, 2, 3, 2, 8, 9, 8, 2},
        new[] {2, 5, 10, 2, 3, 5, 3, 7, 5},
        new[] {8, 2, 0, 8, 5, 2, 8, 7, 5, 10, 2, 5},
        new[] {9, 0, 1, 5, 10, 3, 5, 3, 7, 3, 10, 2},
        new[] {9, 8, 2, 9, 2, 1, 8, 7, 2, 10, 2, 5, 7, 5, 2},
        new[] {1, 3, 5, 3, 7, 5},
        new[] {0, 8, 7, 0, 7, 1, 1, 7, 5},
        new[] {9, 0, 3, 9, 3, 5, 5, 3, 7},
        new[] {9, 8, 7, 5, 9, 7},
        new[] {5, 8, 4, 5, 10, 8, 10, 11, 8},
        new[] {5, 0, 4, 5, 11, 0, 5, 10, 11, 11, 3, 0},
        new[] {0, 1, 9, 8, 4, 10, 8, 10, 11, 10, 4, 5},
        new[] {10, 11, 4, 10, 4, 5, 11, 3, 4, 9, 4, 1, 3, 1, 4},
        new[] {2, 5, 1, 2, 8, 5, 2, 11, 8, 4, 5, 8},
        new[] {0, 4, 11, 0, 11, 3, 4, 5, 11, 2, 11, 1, 5, 1, 11},
        new[] {0, 2, 5, 0, 5, 9, 2, 11, 5, 4, 5, 8, 11, 8, 5},
        new[] {9, 4, 5, 2, 11, 3},
        new[] {2, 5, 10, 3, 5, 2, 3, 4, 5, 3, 8, 4},
        new[] {5, 10, 2, 5, 2, 4, 4, 2, 0},
        new[] {3, 10, 2, 3, 5, 10, 3, 8, 5, 4, 5, 8, 0, 1, 9},
        new[] {5, 10, 2, 5, 2, 4, 1, 9, 2, 9, 4, 2},
        new[] {8, 4, 5, 8, 5, 3, 3, 5, 1},
        new[] {0, 4, 5, 1, 0, 5},
        new[] {8, 4, 5, 8, 5, 3, 9, 0, 5, 0, 3, 5},
        new[] {9, 4, 5},
        new[] {4, 11, 7, 4, 9, 11, 9, 10, 11},
        new[] {0, 8, 3, 4, 9, 7, 9, 11, 7, 9, 10, 11},
        new[] {1, 10, 11, 1, 11, 4, 1, 4, 0, 7, 4, 11},
        new[] {3, 1, 4, 3, 4, 8, 1, 10, 4, 7, 4, 11, 10, 11, 4},
        new[] {4, 11, 7, 9, 11, 4, 9, 2, 11, 9, 1, 2},
        new[] {9, 7, 4, 9, 11, 7, 9, 1, 11, 2, 11, 1, 0, 8, 3},
        new[] {11, 7, 4, 11, 4, 2, 2, 4, 0},
        new[] {11, 7, 4, 11, 4, 2, 8, 3, 4, 3, 2, 4},
        new[] {2, 9, 10, 2, 7, 9, 2, 3, 7, 7, 4, 9},
        new[] {9, 10, 7, 9, 7, 4, 10, 2, 7, 8, 7, 0, 2, 0, 7},
        new[] {3, 7, 10, 3, 10, 2, 7, 4, 10, 1, 10, 0, 4, 0, 10},
        new[] {1, 10, 2, 8, 7, 4},
        new[] {4, 9, 1, 4, 1, 7, 7, 1, 3},
        new[] {4, 9, 1, 4, 1, 7, 0, 8, 1, 8, 7, 1},
        new[] {4, 0, 3, 7, 4, 3},
        new[] {4, 8, 7},
        new[] {9, 10, 8, 10, 11, 8},
        new[] {3, 0, 9, 3, 9, 11, 11, 9, 10},
        new[] {0, 1, 10, 0, 10, 8, 8, 10, 11},
        new[] {3, 1, 10, 11, 3, 10},
        new[] {1, 2, 11, 1, 11, 9, 9, 11, 8},
        new[] {3, 0, 9, 3, 9, 11, 1, 2, 9, 2, 11, 9},
        new[] {0, 2, 11, 8, 0, 11},
        new[] {3, 2, 11},
        new[] {2, 3, 8, 2, 8, 10, 10, 8, 9},
        new[] {9, 10, 2, 0, 9, 2},
        new[] {2, 3, 8, 2, 8, 10, 0, 1, 8, 1, 10, 8},
        new[] {1, 10, 2},
        new[] {1, 3, 8, 9, 1, 8},
        new[] {0, 9, 1},
        new[] {0, 3, 8},
        new int[] { }
    };
    }
  }

  public static class IntVertexMaskExtensions {
    /// <summary>
    /// Set this block's vertex 'is solid' flag for the given octant
    /// </summary>
    /// <param name="value"></param>
    /// <param name="octant"></param>
    /// <param name="toTrue"></param>
    /// <returns></returns>
    public static int SetVertexMaskForOctant(this int value, Octants.Octant octant, bool toTrue = true) {
      int mask = 1 << octant.Value;
      if (toTrue) {
        return value | mask;
      }

      return value & ~mask;
    }
  }
}