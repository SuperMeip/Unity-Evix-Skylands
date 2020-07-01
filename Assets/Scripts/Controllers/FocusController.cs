using Evix.Terrain;
using Evix.Terrain.Collections;
using Evix.Terrain.Resolution;
using UnityEngine;

namespace Evix.Controllers {

  /// <summary>
  /// A player controller for use in unity
  /// </summary>
  public class FocusController : MonoBehaviour, ILevelFocus {

    /// <summary>
    /// If this player is active
    /// </summary>
    public bool isActive {
      get;
      private set;
    }

    /// <summary>
    /// The chunk location of this player
    /// </summary>
    public Chunk.ID currentChunk {
      get;
      private set;
    }

    /// <summary>
    /// The previous chunk location of the character
    /// </summary>
    public Chunk.ID previousChunk {
      get;
      private set;
    }

    /// <summary>
    /// The world (voxel) location of this player
    /// </summary>
#if DEBUG
    [ReadOnly] public
#endif
    Vector3 worldLocation;

    /// <summary>
    /// the previous world location of the character
    /// </summary>
    Vector3 previousWorldLocation;

    ///// UNITY FUNCTIONS

    void Update() {
      /// check to see if we should update the chunks
      if (!isActive) {
        return;
      }

      // if this is active and the world position has changed, check if the chunk has changed
      worldLocation = transform.position;
      if (worldLocation != previousWorldLocation) {
        previousWorldLocation = worldLocation;
        currentChunk = worldLocation / Chunk.Diameter;
      }
    }

#if DEBUG

    void OnDrawGizmos() {
      // ignore gizmo if inactive
      if (!isActive) {
        return;
      }

      Level level = World.Current.activeLevel;
      Vector3 worldChunkLocation = ((currentChunk.Coordinate * Chunk.Diameter) + (Chunk.Diameter / 2)).vec3;

      /// draw the chunk this focus is in
      Gizmos.color = new Color(1.0f, 0.64f, 0.0f);
      Gizmos.DrawWireCube(worldChunkLocation, new Vector3(Chunk.Diameter, Chunk.Diameter, Chunk.Diameter));
      worldChunkLocation -= new Vector3((Chunk.Diameter / 2), (Chunk.Diameter / 2), (Chunk.Diameter / 2));

      /// draw the active chunk area
      ChunkResolutionAperture activeAperture = level.getApetureByPriority(Level.AperturePriority.Active);
      Gizmos.color = Color.green;
      Gizmos.DrawWireCube(worldChunkLocation, new Vector3(
        activeAperture.managedChunkRadius * 2,
        activeAperture.managedChunkHeightRadius * 2,
        activeAperture.managedChunkRadius * 2
      ) * Chunk.Diameter);

      /// draw the meshed chunk area
      ChunkResolutionAperture meshAperture = level.getApetureByPriority(Level.AperturePriority.Meshed);
      Gizmos.color = Color.blue;
      Gizmos.DrawWireCube(worldChunkLocation, new Vector3(
        meshAperture.managedChunkRadius * 2,
        meshAperture.managedChunkHeightRadius * 2,
        meshAperture.managedChunkRadius * 2
      ) * Chunk.Diameter);

      /// draw the meshed chunk area
      ChunkResolutionAperture loadedAperture = level.getApetureByPriority(Level.AperturePriority.Loaded);
      Gizmos.color = Color.yellow;
      Gizmos.DrawWireCube(worldChunkLocation, new Vector3(
        loadedAperture.managedChunkRadius * 2,
        loadedAperture.managedChunkHeightRadius * 2,
        loadedAperture.managedChunkRadius * 2
      ) * Chunk.Diameter);

      // draw all the chunks in the loading queue right now
      /*foreach ((Chunk.ID chunk, Level.AperturePriority priority) in level.getProcessingChunksByApeturePriority()) {
        if (priority == Level.AperturePriority.Meshed) {
          Gizmos.color = Color.blue;
        } else if (priority == Level.AperturePriority.Loaded) {
          Gizmos.color = Color.yellow;
        } else {
          Gizmos.color = Color.white;
        }

        Gizmos.DrawWireCube(
          ((chunk.Coordinate * Chunk.Diameter) + (Chunk.Diameter / 2)).vec3,
          new Vector3(Chunk.Diameter, Chunk.Diameter, Chunk.Diameter)
        );
      }*/
    }
#endif

    /// <summary>
    /// Set the previous to the current chunk the level thinks we're in, to keep it up to date
    /// </summary>
    public void onFocusUpdatedForLevel(Chunk.ID toNewChunk) {
      previousChunk = toNewChunk;
    }

    /// <summary>
    /// Set the world position of the focus. Also sets the chunk position.
    /// </summary>
    public void setPosition(Coordinate worldPosition) {
      transform.position = worldLocation = previousWorldLocation = worldPosition.vec3;
      currentChunk = previousChunk = worldLocation / Chunk.Diameter;
    }

    /// <summary>
    /// set the controller active
    /// </summary>
    public void activate() {
      isActive = true;
    }
  }
}
