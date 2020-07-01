using Evix.Terrain.Collections;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Jobs;

namespace Evix.Terrain.DataGeneration {

  /// <summary>
  /// Class for accessing level data from files
  /// @todo, this should become an object made in the Level constructor, with file names pre-generated that's grabbed by the apeture from the level {get}
  /// </summary>
  public static class LevelDAO {

    /// <summary>
    /// A regex we can use to remove illegal file name chars.
    /// @TODO: move this to Level's constructor so we only need to construct the level's file safe name once.
    /// </summary>
    static Regex IllegalCharactersForFileName = new Regex(
      string.Format("[{0}]",
      Regex.Escape(new string(Path.GetInvalidFileNameChars()))),
      RegexOptions.Compiled
    );

    /// <summary>
    /// Get a file load job
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static LoadChunkDataFromFileJob GetFileLoadJob(Chunk.ID chunkID, Level level) {
      return new LoadChunkDataFromFileJob(chunkID, level.name);
    }

    /// <summary>
    /// Check if the chunk save file exists
    /// </summary>
    /// <param name=""></param>
    /// <param name="level"></param>
    /// <returns></returns>
    public static bool ChunkFileExists(Chunk.ID chunkID, Level level) {
      return File.Exists(GetChunkVoxelDataFileName(chunkID, level.name));
    }

    /// <summary>
    /// Get the voxeldata for a chunk location from file
    /// </summary>
    /// <returns>False if the chunk is empty</returns>
    static bool GetVoxelDataForChunkFromFile(Chunk.ID chunkId, string levelName, out NativeArray<byte> voxelData) {
      voxelData = default;
      IFormatter formatter = new BinaryFormatter();
      Stream readStream = new FileStream(
        GetChunkVoxelDataFileName(chunkId, levelName),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read
      ) {
        Position = 0
      };
      var fileData = formatter.Deserialize(readStream);
      if (fileData is NativeArray<byte>) {
        voxelData = (NativeArray<byte>)fileData;
        readStream.Close();
        return true;
      }

      return false;
    }

    /// <summary>
    /// Get the file name a chunk is saved to based on it's location
    /// </summary>
    /// <param name="chunkLocation">the location of the chunk</param>
    /// <returns></returns>
    static string GetChunkVoxelDataFileName(Chunk.ID chunk, string levelName) {
      return $"{GetChunkDataFolder(levelName)}{chunk.Coordinate}.evxch";
    }

    /// <summary>
    /// Get the name of the folder we use to store chunk data for this level
    /// </summary>
    /// <param name="levelName"></param>
    /// <returns></returns>
    static string GetChunkDataFolder(string levelName) {
      return $"{GetLevelFolder(levelName)}chunkdata/";
    }

    /// <summary>
    /// Get the save directory for the given level
    /// </summary>
    /// <param name="level"></param>
    /// <returns></returns>
    static string GetLevelFolder(string levelName) {
      return $"{World.Current.GameSaveFilePath}/leveldata/{IllegalCharactersForFileName.Replace(levelName, "")}/";
    }

    /// <summary>
    /// Create the save file directory if it doesn't exist for the level yet
    /// @todo: put this in the save function
    /// </summary>
    static void CheckForSaveDirectory(string levelName) {
      if (Directory.Exists(GetChunkDataFolder(levelName))) {
        return;
      }

      Directory.CreateDirectory(GetChunkDataFolder(levelName));
    }

    /// <summary>
    /// Job for loading chunk voxel data from a file
    /// </summary>
    public struct LoadChunkDataFromFileJob : IJob {

      /// <summary>
      /// The voxel data retreived from file
      /// </summary>
      public NativeArray<byte> outVoxels;

      /// <summary>
      /// If the loaded chunk ended up being empty
      /// </summary>
      public NativeArray<int> solidVoxelCount;

      /// <summary>
      /// The level name, used for finding the file
      /// </summary>
      string levelName;

      /// <summary>
      /// the chunk ID we're getting data for.
      /// </summary>
      Chunk.ID chunkID;

      public LoadChunkDataFromFileJob(Chunk.ID chunkID, string levelName) {
        this.chunkID = chunkID;
        this.levelName = levelName;
        solidVoxelCount = new NativeArray<int>(1, Allocator.TempJob);
        outVoxels = new NativeArray<byte>(Chunk.Diameter * Chunk.Diameter * Chunk.Diameter, Allocator.TempJob);
      }

      /// <summary>
      /// Load the chunk data from file
      /// </summary>
      public void Execute() {
        if (GetVoxelDataForChunkFromFile(chunkID, levelName, out outVoxels)) {
          solidVoxelCount[0] = 1;
        }
      }
    }
  }
}
