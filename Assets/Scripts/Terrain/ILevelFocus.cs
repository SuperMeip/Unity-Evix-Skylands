using Evix.Terrain.Collections;

namespace Evix.Terrain {
  public interface ILevelFocus {

    /// <summary>
    /// The current 
    /// </summary>
    Chunk.ID currentChunk {
      get;
    }

    /// <summary>
    /// The previous 
    /// </summary>
    Chunk.ID previousChunk {
      get;
    }

    /// <summary>
    /// Callback for when the level has finished updating for this focus,
    /// this can be used to set the previous chunk location to what the level knows it to be.
    /// </summary>
    void onFocusUpdatedForLevel(Chunk.ID toNewChunk);

    /// <summary>
    /// Activat the focus' tracking
    /// </summary>
    void activate();
  }
}
