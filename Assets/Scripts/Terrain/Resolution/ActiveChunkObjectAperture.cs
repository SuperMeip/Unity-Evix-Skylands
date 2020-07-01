using Evix.Terrain.Collections;
using Evix.Events;
using Unity.Jobs;
using Evix.Terrain.MeshGeneration;

namespace Evix.Terrain.Resolution {

  /// <summary>
  /// Aperture to handle active gameobject resolution level
  /// </summary>
  class ActiveChunkObjectAperture : ChunkResolutionAperture {

    /// <summary>
    /// Construct
    /// </summary>
    /// <param name="managedChunkRadius"></param>
    /// <param name="managedChunkHeight"></param>
    internal ActiveChunkObjectAperture(int managedChunkRadius, int managedChunkHeight = 0)
    : base(managedChunkRadius, managedChunkHeight) { }

    /// <summary>
    /// Schedule the activate chunk job
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public override ApertureJobHandle getJobFor(Chunk.ID chunkID) {
      ActivateChunkObjectJob job = new ActivateChunkObjectJob(chunkID);
      return new ApertureJobHandle(job, this);
    }

    /// <summary>
    /// The chunk is ready to mesh when the mesh is generated but not empty
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public override bool chunkIsReady(Chunk.ID chunkID) {
      return level.chunks.TryGetValue(chunkID, out Chunk chunk)
        && chunk.meshIsGenerated 
        && !chunk.meshIsEmpty;
    }

    /// <summary>
    /// If the chunk is loaded but empty, we should just drop it from this queue
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    internal override bool validateChunk(Chunk.ID chunkID, out Chunk chunk) {
      // if this is valid for meshing
      // and if the chunk is meshed and isn't empty, or just isn't meshed, it's valid for the queue.
      return level.getApetureByPriority(Level.AperturePriority.Meshed).validateChunk(chunkID, out chunk) 
        && !(chunk.meshIsGenerated && chunk.meshIsEmpty);
    }

    /// <summary>
    /// Do nothing on job complete
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="finishedJob"></param>
    protected override void handleFinishedJob(Chunk.ID chunkID, ref ApertureJobHandle finishedJob) {}

    /// <summary>
    /// A job for notifying the main thread to set the chunk object active
    /// </summary>
    public struct ActivateChunkObjectJob : IJob {
      /// <summary>
      /// The chunk id we're updating to active
      /// </summary>
      Chunk.ID chunkID;

      public ActivateChunkObjectJob(Chunk.ID chunkID) {
        this.chunkID = chunkID;
      }

      /// <summary>
      /// notify the chunk activaton channel that we want this chunk active
      /// </summary>
      public void Execute() {
        World.EventSystem.notifyChannelOf(
          new SetChunkActiveEvent(chunkID),
          EventSystems.WorldEventSystem.Channels.ChunkActivationUpdates
        );
      }
    }

    /// <summary>
    /// An event to notify the level controller to set a chunk active
    /// </summary>
    public struct SetChunkActiveEvent : IEvent {

      /// <summary>
      /// The name of the event
      /// </summary>
      public string name {
        get;
      }

      /// <summary>
      /// The chunk id to set active
      /// </summary>
      public Chunk.ID chunkID {
        get;
      }

      public SetChunkActiveEvent(Chunk.ID chunkID) {
        this.chunkID = chunkID;
        name = $"Setting chunk active: {chunkID.Coordinate}";
      }
    }
  }
}
