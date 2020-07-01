using Evix.Terrain.Collections;
using Evix.Terrain.DataGeneration;
using Unity.Jobs;

namespace Evix.Terrain.Resolution {

  /// <summary>
  /// Apeture for loading chunk voxel data into memmory
  /// </summary>
  class VoxelDataAperture : ChunkResolutionAperture {

    /// <summary>
    /// Construct
    /// </summary>
    /// <param name="managedChunkRadius"></param>
    /// <param name="managedChunkHeight"></param>
    internal VoxelDataAperture(int managedChunkRadius, int managedChunkHeight = 0)
    : base(managedChunkRadius, true, managedChunkHeight) {
      yWeightMultiplier = 1.5f;
    }

    /// <summary>
    /// Schedule the job for loading new data from file
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public override ApertureJobHandle getJobFor(Chunk.ID chunkID, FocusAdjustmentType adjustmentType) {
      IJob job;
      if (adjustmentType == FocusAdjustmentType.InFocus) {
        if (LevelDAO.ChunkFileExists(chunkID, level)) {
          job = new LevelDAO.LoadChunkDataFromFileJob(chunkID, level.name);
          // if there's no file, we need to generate the chunk data from scratch
        } else {
          job = BiomeMap.GetTerrainGenerationJob(chunkID, level);
        }
      /// if it's out of focus, we want to save the chunk to file
      } else if (level.chunks.TryGetValue(chunkID, out Chunk chunkToSave)) {
        job = new LevelDAO.SaveChunkDataToFileJob(chunkID, level.name, chunkToSave.getVoxels(), chunkToSave.solidVoxelCount);
      } else throw new System.MissingMemberException(
        $"VoxelDataAperture is trying to save chunk data for {chunkID} but could not find the chunk data in the level"
      );

      return new ApertureJobHandle(job, this);
    }

    /// <summary>
    /// If the chunk is already loaded, we don't need to load it again
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="chunk"></param>
    /// <returns></returns>
    internal override bool validateChunk(Chunk.ID chunkID, out Chunk chunk) {
      return base.validateChunk(chunkID, out chunk) && (chunk == null || !chunk.isLoaded);
    }

    /// <summary>
    /// Do things for different jobs
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="finishedJobHandle"></param>
    protected override void handleFinishedJob(Chunk.ID chunkID, ref ApertureJobHandle finishedJobHandle) {
      Chunk newlyLoadedChunk = new Chunk();
      switch (finishedJobHandle.job) {
        /// same for both, but they're structs so can't inherit.
        case LevelDAO.LoadChunkDataFromFileJob lcdffj:
          // if we didn't generate an empty chunk, copy the voxels over.
          if (lcdffj.solidVoxelCount[0] > 0) {
            newlyLoadedChunk.setVoxels(
              lcdffj.outVoxels, 
              lcdffj.solidVoxelCount[0]
            );
          }
          // also dispose of the native array
          lcdffj.outVoxels.Dispose(finishedJobHandle.jobHandle);
          lcdffj.solidVoxelCount.Dispose(finishedJobHandle.jobHandle);
          break;
        case BiomeMap.GenerateChunkDataFromSourceJob gcdfsj:
          if (gcdfsj.solidVoxelCount[0] > 0) {
            newlyLoadedChunk.setVoxels(
              gcdfsj.outVoxels,
              gcdfsj.solidVoxelCount[0]
            );
          }
          gcdfsj.outVoxels.Dispose(finishedJobHandle.jobHandle);
          gcdfsj.solidVoxelCount.Dispose(finishedJobHandle.jobHandle);
          break;
        /// once the chunk data is saved, remove it from the level
        case LevelDAO.SaveChunkDataToFileJob scdtfj:
          level.chunks.Remove(scdtfj.chunkID);
          break;
        default:
          return;
      }

      // add chunk to the level
      newlyLoadedChunk.isLoaded = true;
      level.chunks.Add(chunkID, newlyLoadedChunk);
    }
  }
}
