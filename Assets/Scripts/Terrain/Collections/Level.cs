using Evix.Terrain.Resolution;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Evix.Terrain.Collections {

  /// <summary>
  /// A basic level storing chunks.
  /// </summary>
  public class Level {

    /// <summary>
    /// Resolution layers that apetures manage.
    /// </summary>
    public enum AperturePriority { Loaded, Meshed, Active }

    /// <summary>
    /// The overall bounds of the level, max x y and z
    /// </summary>
    public readonly Coordinate chunkBounds;

    /// <summary>
    /// The seed the level uses for generation
    /// </summary>
    public readonly int seed;

    /// <summary>
    /// The name of the level
    /// </summary>
    public string name = "No Man's Land";

    /// <summary>
    /// The collection of chunks
    /// </summary>
    public readonly Dictionary<Chunk.ID, Chunk> chunks
      = new Dictionary<Chunk.ID, Chunk>();

    /// <summary>
    /// The foci this level is loaded around managed by assigned ID
    /// </summary>
    readonly Dictionary<int, ILevelFocus> levelFociByID
      = new Dictionary<int, ILevelFocus>();

    /// <summary>
    /// The chunk load apetures, listed by priority
    /// </summary>
    readonly ChunkResolutionAperture[] apertures;

    /// <summary>
    /// The current highest assigned focus id.
    /// </summary>
    int currentMaxFocusID = -1;

    ///// CONSTRUCTORS

    /// <summary>
    /// Create a new level of the given size that uses the given apetures.
    /// </summary>
    /// <param name="chunkBounds"></param>
    /// <param name="apeturesByPriority"></param>
    public Level(Coordinate chunkBounds, ChunkResolutionAperture[] apeturesByPriority) {
      seed = 1234;
      this.chunkBounds = chunkBounds;
      apertures = apeturesByPriority;
      if (apeturesByPriority != null && apeturesByPriority.Length == Enum.GetNames(typeof(AperturePriority)).Length) {
        for (AperturePriority priority = 0; (int)priority < apeturesByPriority.Length; priority++) {
          apertures[(int)priority].setLevel(this, priority);
        }
      } else if (apertures == null) {
        apertures = new ChunkResolutionAperture[0];
      }
    }

    ///// PUBLIC FUNCTIONS

    /// <summary>
    /// Get a voxel based on it's world location
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public byte this[int x, int y, int z] {
      get {
        if (chunks.TryGetValue(Chunk.ID.FromWorldPos(x, y, z), out Chunk chunk)) {
          return chunk[x & 0xF, y & 0xF, z & 0xF];
        }

        return 0;
      }

      set {
        if (chunks.TryGetValue(Chunk.ID.FromWorldPos(x, y, z), out Chunk chunk)) {
          chunk[x & 0xF, y & 0xF, z & 0xF] = value;
        } else {
          World.Debugger.logError($"Tried to set a value in non existent chunk {x}, {y}, {z}");
        }
      }
    }

    /// <summary>
    /// Get the voxel at the given world coordinate
    /// </summary>
    /// <param name="worldLocation"></param>
    /// <returns></returns>
    public byte getVoxel(Coordinate worldLocation) {
      return this[worldLocation.x, worldLocation.y, worldLocation.z];
    }

    /// <summary>
    /// Get the id for the given level focus+
    /// </summary>
    /// <param name="focus"></param>
    /// <returns></returns>
    public int getFocusID(ILevelFocus focus) {
      foreach (KeyValuePair<int, ILevelFocus> storedFocus in levelFociByID) {
        if (storedFocus.Value == focus) {
          return storedFocus.Key;
        }
      }

      return -1;
    }

    /// <summary>
    /// Return the apeture for the resolution layer.
    /// </summary>
    /// <param name="resolutionLayer"></param>
    /// <returns></returns>
    public ChunkResolutionAperture getApetureByPriority(AperturePriority resolutionLayer) {
      return apertures.Length > (int)resolutionLayer
        ? apertures[(int)resolutionLayer]
        : null;
    }

    /// <summary>
    /// do something for each focus in the level
    /// </summary>
    /// <param name="action"></param>
    public void forEachFocus(Action<ILevelFocus> action) {
      foreach (ILevelFocus focus in levelFociByID.Values) {
        action(focus);
      }
    }

    /// <summary>
    /// do something for each focus in the level
    /// </summary>
    /// <param name="action"></param>
    public void forEachAperture(Action<ChunkResolutionAperture> action) {
      foreach (ChunkResolutionAperture aperture in apertures) {
        action(aperture);
      }
    }

    /// <summary>
    /// Add a focus to be managed by this level
    /// </summary>
    /// <param name="newFocus"></param>
    public void addFocus(ILevelFocus newFocus) {
      levelFociByID[++currentMaxFocusID] = newFocus;
      newFocus.activate();
    }
  }
}
