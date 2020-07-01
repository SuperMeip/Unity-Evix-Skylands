using Evix.Terrain.Collections;
using Evix.Terrain.DataGeneration.Sources;
using Unity.Jobs;
using Unity.Collections;
using Evix.Voxels;

namespace Evix.Terrain.DataGeneration {
  public static class BiomeMap {

    /// <summary>
    /// Get a chunk terrain generation job from the biome map
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static GenerateChunkDataFromSourceJob GetTerrainGenerationJob(Chunk.ID chunkID, Level level) {
      return new GenerateChunkDataFromSourceJob(chunkID, level.seed);
    }

    /// <summary>
    /// Get the biome for the given level seed and chunk id
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="levelSeed"></param>
    /// <returns></returns>
    static VoxelSource GetBiomeForChunk(Chunk.ID chunkID, int levelSeed) {
      return new PerlinSource(levelSeed);
    }

    /// <summary>
    /// Generate all the voxels for the given chunk id using the provided biome
    /// </summary>
    /// <param name="biome"></param>
    /// <param name="chunkID"></param>
    /// <param name="generatedVoxels"></param>
    /// <returns>the number of solid voxels generated</returns>
    static int GenerateTerrainDataForChunk(VoxelSource biome, Chunk.ID chunkID, out byte[] generatedVoxels) {
      int solidVoxelCount = 0;
      generatedVoxels = null;
      byte[] voxels = new byte[Chunk.Diameter * Chunk.Diameter * Chunk.Diameter];
      Coordinate chunkWorldLocation = chunkID.toWorldLocation();

      chunkWorldLocation.until(chunkWorldLocation + Chunk.Diameter, currentWorldLocation => {
        byte voxelValue = biome.getVoxelValueAt(currentWorldLocation);
        if (voxelValue != Voxel.Types.Empty.Id) {
          solidVoxelCount++;
          Coordinate localChunkVoxelLocation = currentWorldLocation - chunkWorldLocation;
          voxels[localChunkVoxelLocation.flatten(Chunk.Diameter)] = voxelValue;
        }
      });

      generatedVoxels = voxels;
      return solidVoxelCount;
    }

    /// <summary>
    /// Generates the chunk data from a biome source
    /// </summary>
    public struct GenerateChunkDataFromSourceJob : IJob {

      /// <summary>
      /// The voxel data generated from the biome
      /// </summary>
      public NativeArray<byte> outVoxels; 

      /// <summary>
      /// If the loaded chunk ended up being empty
      /// </summary>
      public NativeArray<int> solidVoxelCount;

      /// <summary>
      /// The level name, used for getting the biome
      /// </summary>
      readonly int levelSeed;

      /// <summary>
      /// the chunk ID we're getting data for.
      /// </summary>
      Chunk.ID chunkID;

      public GenerateChunkDataFromSourceJob(Chunk.ID chunkID, int levelSeed) {
        this.chunkID = chunkID;
        this.levelSeed = levelSeed;
        solidVoxelCount = new NativeArray<int>(1, Allocator.Persistent);
        outVoxels = new NativeArray<byte>(Chunk.Diameter * Chunk.Diameter * Chunk.Diameter, Allocator.Persistent);
      }

      /// <summary>
      /// Get the biome and generate the chunk data for it
      /// </summary>
      public void Execute() {
        VoxelSource biome = GetBiomeForChunk(chunkID, levelSeed);
        solidVoxelCount[0] = GenerateTerrainDataForChunk(biome, chunkID, out byte[] generatedVoxels);
        outVoxels.CopyFrom(generatedVoxels);
      }
    }
  }
}
