using Evix.Controllers;
using Evix.EventSystems;
using Evix.Terrain.Collections;
using Evix.Terrain.DataGeneration;
using Evix.Terrain.MeshGeneration;
using Evix.Terrain.Resolution;
using UnityEngine;

namespace Evix.Managers {

  /// <summary>
  /// Manages our test game world
  /// </summary>
  public class TestWorldManager : MonoBehaviour {

    /// <summary>
    /// The current object to focus on.
    /// </summary>
    public FocusManager currentFocus;

    /// <summary>
    /// The controller for the active level.
    /// </summary>
    public LevelManager levelManager;


    ///// SETUP VARS
    public float SeaLevel = 30.0f;
    public Vector3 levelSize = new Vector3(1000, 2, 1000);

    public int activeChunkRadius = 10;
    public int activeChunkHeightOverride = 0;

    public int meshedChunkBuffer = 10;
    public int meshedChunkBufferHeightOverride = 0;

    public int loadedChunkBuffer = 10;
    public int loadedChunkHeightBufferOverride = 0;

    void Awake() {
      setUpTestLevel();
    }

    void setUpTestChunk() {
      // Set up one chunk to load and mesh
      Level storage = new Level((1, 1, 1), null);
      Chunk.ID chunkID = new Chunk.ID(0, 0, 0);
      World.setActiveLevel(storage);
      levelManager.initializeFor(World.Current.activeLevel);
      World.EventSystem.subscribe(
        levelManager,
        WorldEventSystem.Channels.ChunkActivationUpdates
      );

      // run the load job syncly
      BiomeMap.GenerateChunkDataFromSourceJob terrainGenJob = BiomeMap.GetTerrainGenerationJob(chunkID, storage);
      terrainGenJob.Execute();

      // get the data from the load job
      Chunk newlyLoadedChunk = new Chunk();
      if (terrainGenJob.solidVoxelCount[0] > 0) {
        newlyLoadedChunk.setVoxels(
          terrainGenJob.outVoxels,
          terrainGenJob.solidVoxelCount[0]
        );
      }
      terrainGenJob.outVoxels.Dispose();

      // add the loaded chunk to storage
      storage.chunks.Add(chunkID, newlyLoadedChunk);
      newlyLoadedChunk.isLoaded = true;

      // get the mesh gen job and run it syncly
      MarchingTetsMeshGenerator.MarchingTetsMeshGenJob meshGenJob = MarchingTetsMeshGenerator.GetJob(MarchingTetsMeshGenerator.GetVoxelsToMarchOver(chunkID, storage));
      meshGenJob.Execute();

      // set up the mesh data
      bool meshIsEmpty = meshGenJob.outVerticies.Length <= 0;
      VoxelMeshData chunkMeshData = new VoxelMeshData(
        chunkID,
        meshIsEmpty,
        meshGenJob.outVerticies,
        meshGenJob.outTriangles,
        meshGenJob.outColors
      );

      // dispose of the allocated resources
      meshGenJob.outVerticies.Dispose();
      meshGenJob.outTriangles.Dispose();
      meshGenJob.outColors.Dispose();

      // update the chunk data to say if it's meshed
      if (storage.chunks.TryGetValue(chunkID, out Chunk updatedChunk)) {
        updatedChunk.meshIsGenerated = true;
        updatedChunk.meshIsEmpty = meshIsEmpty;
      }

      /// notify the chunk manager
      World.EventSystem.notifyChannelOf(
        new MeshGenerationAperture.ChunkMeshLoadingFinishedEvent(chunkMeshData),
        WorldEventSystem.Channels.ChunkActivationUpdates
      );

      // set the chunk active
      ActiveChunkObjectAperture.ActivateChunkObjectJob activateChunkJob = new ActiveChunkObjectAperture.ActivateChunkObjectJob(chunkID);
      activateChunkJob.Execute();
    }

    void setUpTestLevel() {
      // set up player 1
      World.SetPlayer(new Player(), 1);

      // set up the level
      Level level = new Level(
        levelSize,
        new ChunkResolutionAperture[] {
          new VoxelDataAperture(
            activeChunkRadius + meshedChunkBuffer + loadedChunkBuffer,
            activeChunkHeightOverride + meshedChunkBufferHeightOverride + loadedChunkHeightBufferOverride
          ),
          new MeshGenerationAperture(
            activeChunkRadius + meshedChunkBuffer,
            activeChunkHeightOverride + meshedChunkBufferHeightOverride
          ),
          new ActiveChunkObjectAperture(
            activeChunkRadius,
            activeChunkHeightOverride
          )
        }
      );
      World.setActiveLevel(level);

      // initialize the focus
      currentFocus.setPosition((level.chunkBounds) / 2 * Chunk.Diameter);
      level.addFocus(currentFocus);

      // set up the level controller
      World.EventSystem.subscribe(
        levelManager,
        WorldEventSystem.Channels.ChunkActivationUpdates
      );
      levelManager.initializeFor(level);
    }
  }
}