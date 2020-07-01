using Evix.Terrain.Collections;
using System;
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
      return File.Exists(GetChunkDataFileName(chunkID, level.name));
    }

    /// <summary>
    /// Get the voxeldata for a chunk location from file
    /// </summary>
    /// <returns>False if the chunk is empty</returns>
    static bool GetDataForChunkFromFile(Chunk.ID chunkId, string levelName, out ChunkSaveData chunkData) {
      chunkData = default;
      IFormatter formatter = new BinaryFormatter();
      Stream readStream = new FileStream(
        GetChunkDataFileName(chunkId, levelName),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read
      ) {
        Position = 0
      };
      var fileData = formatter.Deserialize(readStream);
      if (fileData is ChunkSaveData) {
        chunkData = (ChunkSaveData)fileData;
        readStream.Close();
        return true;
      }

      readStream.Close();
      return false;
    }

    /// <summary>
    /// Only to be used by jobs
    /// Save a chunk to file
    /// </summary>
    /// <param name="chunkLocation"></param>
    static public void SaveChunkDataToFile(Chunk.ID chunkId, string levelName, NativeArray<byte> voxelsToSave, int solidVoxelCount) {
      IFormatter formatter = new BinaryFormatter();
      CheckForSaveDirectory(levelName);
      Stream stream = new FileStream(GetChunkDataFileName(chunkId, levelName), FileMode.Create, FileAccess.Write, FileShare.None);
      formatter.Serialize(stream, new ChunkSaveData(voxelsToSave, solidVoxelCount));
      stream.Close();
    }

    /// <summary>
    /// Get the file name a chunk is saved to based on it's location
    /// </summary>
    /// <param name="chunkLocation">the location of the chunk</param>
    /// <returns></returns>
    static string GetChunkDataFileName(Chunk.ID chunk, string levelName) {
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
    /// A serializable bit of chunk data
    /// </summary>
    [Serializable]
    public struct ChunkSaveData : ISerializable {
      /// <summary>
      /// The voxels to save
      /// </summary>
      byte[] voxels;

      /// <summary>
      /// the solid voxel count
      /// </summary>
      int solidVoxelCount;

      /// <summary>
      /// Make a new set of save data from a job
      /// </summary>
      /// <param name="voxels"></param>
      /// <param name="solidVoxelCount"></param>
      public ChunkSaveData(NativeArray<byte> voxels, int solidVoxelCount) {
        this.voxels = solidVoxelCount == 0 ? null : voxels.ToArray();
        this.solidVoxelCount = solidVoxelCount;
      }

      /// <summary>
      /// deserialize
      /// </summary>
      /// <param name="info"></param>
      /// <param name="context"></param>
      public ChunkSaveData(SerializationInfo info, StreamingContext context) {
        voxels = (byte[])info.GetValue("voxels", typeof(byte[]));
        solidVoxelCount = (int)info.GetValue("voxelCount", typeof(int));
      }

      /// <summary>
      /// serizalize
      /// </summary>
      /// <param name="info"></param>
      /// <param name="context"></param>
      public void GetObjectData(SerializationInfo info, StreamingContext context) {
        info.AddValue("voxels", voxels, typeof(byte[]));
        info.AddValue("voxelCount", solidVoxelCount, typeof(int));
      }

      /// <summary>
      /// Get the voxels and the count
      /// </summary>
      /// <param name="voxels"></param>
      /// <returns></returns>
      public int tryGetVoxels(out NativeArray<byte> voxels) {
        voxels = new NativeArray<byte>(Chunk.Diameter * Chunk.Diameter * Chunk.Diameter, Allocator.Temp);
        if (solidVoxelCount != 0) {
          voxels.CopyFrom(this.voxels);
        }

        return solidVoxelCount;
      }
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
        if (GetDataForChunkFromFile(chunkID, levelName, out ChunkSaveData chunkData)) {
          int solidVoxels = chunkData.tryGetVoxels(out NativeArray<byte> voxels);
          if (solidVoxels != 0) {
            outVoxels = voxels;
            solidVoxelCount[0] = solidVoxels;
          }
        }
      }
    }

    /// <summary>
    /// Job for loading chunk voxel data from a file
    /// </summary>
    public struct SaveChunkDataToFileJob : IJob {

      /// <summary>
      /// the chunk ID we're getting data for.
      /// </summary>
      public readonly Chunk.ID chunkID;

      /// <summary>
      /// The voxel data retreived from file
      /// </summary>
      [DeallocateOnJobCompletion]
      readonly NativeArray<byte> saveVoxels;

      /// <summary>
      /// If the loaded chunk ended up being empty
      /// </summary>
      public int solidVoxelCount;

      /// <summary>
      /// The level name, used for finding the file
      /// </summary>
      string levelName;

      public SaveChunkDataToFileJob(Chunk.ID chunkID, string levelName, NativeArray<byte> inputVoxels, int solidVoxelCount) {
        this.chunkID = chunkID;
        this.levelName = levelName;
        this.solidVoxelCount = solidVoxelCount;
        saveVoxels = inputVoxels;
      }

      /// <summary>
      /// Load the chunk data from file
      /// </summary>
      public void Execute() {
        SaveChunkDataToFile(chunkID, levelName, saveVoxels, solidVoxelCount);
      }
    }
  }
}
