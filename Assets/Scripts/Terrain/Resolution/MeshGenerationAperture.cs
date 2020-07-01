using Evix.Terrain.Collections;
using Evix.Terrain.MeshGeneration;
using Evix.Events;
using System.Collections.Generic;

namespace Evix.Terrain.Resolution {

  /// <summary>
  /// Aperture to handle mesh generation resolution level
  /// </summary>
  class MeshGenerationAperture : ChunkResolutionAperture {

    /// <summary>
    /// The voxel data we've pulled from the level for meshing the chunk
    /// </summary>
    Dictionary<Chunk.ID, byte[]> preparedVoxelData
      = new Dictionary<Chunk.ID, byte[]>();

    /// <summary>
    /// Construct
    /// </summary>
    /// <param name="managedChunkRadius"></param>
    /// <param name="managedChunkHeight"></param>
    internal MeshGenerationAperture(int managedChunkRadius, int managedChunkHeight = 0)
    : base(managedChunkRadius, managedChunkHeight) {}

    /// <summary>
    /// Schedule a mesh job. Can only be called from the main thread!
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public override ApertureJobHandle getJobFor(Chunk.ID chunkID) {
      if (preparedVoxelData.TryGetValue(chunkID, out byte[] marchVoxelData)) {
        MarchingTetsMeshGenerator.MarchingTetsMeshGenJob job = MarchingTetsMeshGenerator.GetJob(marchVoxelData);
        preparedVoxelData.Remove(chunkID);
        return new ApertureJobHandle(job, this);
      } else throw new System.MissingMemberException(
        $"The MeshGenerationAperture does not have any march voxel data prepared for {chunkID} to create a new MarchingTetsMeshGenJob"
      );
    }

    /// <summary>
    /// If the chunk is loaded and it's neighbors are too, it's ready for meshing
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public override bool chunkIsReady(Chunk.ID chunkID) {
      if (level.chunks.TryGetValue(chunkID, out Chunk chunk)) {
        if (!chunk.isEmpty && chunk.isLoaded) {
          if (chunk.isSolid) {
            (bool necessaryNeighborsAreLoaded, bool blockingNeighborsAreSolid) = MarchingTetsMeshGenerator.GetRequiredChunkNeighborStats(chunkID, level);
            // if the blocking neighbors arn't solid, and the nessisary neighbors are loaded, then we good to go.
            return !(blockingNeighborsAreSolid || !necessaryNeighborsAreLoaded);
          } else {
            return MarchingTetsMeshGenerator.NecessaryChunkNeighborsAreLoaded(chunkID, level);
          }
        }
      }

      return false;
    }

    /// <summary>
    /// If the chunk is loaded but empty, it's invalid for meshing
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    internal override bool validateChunk(Chunk.ID chunkID, out Chunk chunk) {
      if (base.validateChunk(chunkID, out chunk) && chunk != null) {
        // we don't want the chunk to be loaded and empty, or loaded and solid with it's blocking neighbors also solid
        return !(chunk.isLoaded && (chunk.isEmpty || (chunk.isSolid && MarchingTetsMeshGenerator.AllBlockingNeighborsAreSolid(chunkID, level))));
      }

      return true;
    }

    /// <summary>
    /// Prepare the voxel data we plan to march over
    /// </summary>
    /// <param name="chunkID"></param>
    public override void prepareChunkJobData(Chunk.ID chunkID) {
      preparedVoxelData[chunkID] = MarchingTetsMeshGenerator.GetVoxelsToMarchOver(chunkID, level);
    }

    /// <summary>
    /// When the mesh gen job finishes, let the render thread know to start meshing the chunk
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="finishedJobHandle"></param>
    protected override void handleFinishedJob(Chunk.ID chunkID, ref ApertureJobHandle finishedJobHandle) {
      switch(finishedJobHandle.job) {
        case MarchingTetsMeshGenerator.MarchingTetsMeshGenJob mtmgj:
          /// package the mesh data 
          // no verts = it's an empty mesh
          bool meshIsEmpty = mtmgj.outVerticies.Length == 0; 
          VoxelMeshData chunkMeshData = new VoxelMeshData(
            chunkID,
            meshIsEmpty,
            mtmgj.outVerticies,
            mtmgj.outTriangles,
            mtmgj.outColors
          );

          /// dispose of the allocated resources
          mtmgj.outVerticies.Dispose(finishedJobHandle.jobHandle);
          mtmgj.outTriangles.Dispose(finishedJobHandle.jobHandle);
          mtmgj.outColors.Dispose(finishedJobHandle.jobHandle);

          // update the chunk data to say if it's meshed
          if (level.chunks.TryGetValue(chunkID, out Chunk updatedChunk)) {
            updatedChunk.meshIsGenerated = true;
            updatedChunk.meshIsEmpty = meshIsEmpty;
          }

          /// notify the chunk manager
          World.EventSystem.notifyChannelOf(
            new ChunkMeshLoadingFinishedEvent(chunkMeshData),
            EventSystems.WorldEventSystem.Channels.ChunkActivationUpdates
          );
          return;
        default:
          return;
      }
    }

    /// <summary>
    /// Event notifying the level controller that a chunk mesh is ready
    /// </summary>
    public struct ChunkMeshLoadingFinishedEvent : IEvent {
      public string name {
        get;
      }

      /// <summary>
      /// The compiled chunk mesh data
      /// </summary>
      public VoxelMeshData chunkMeshData {
        get;
      }

      public ChunkMeshLoadingFinishedEvent(VoxelMeshData chunkMeshData) {
        this.chunkMeshData = chunkMeshData;
        name = $"Chunk mesh finished generating for {chunkMeshData.chunkID.Coordinate}";
      }
    }
  }
}
