using Evix.Terrain.Collections;
using Evix.Terrain.Resolution;
using Evix.Events;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Evix.Controllers {

  /// <summary>
  /// Used to control a level in the game world
  /// </summary>
  public class LevelController : MonoBehaviour, IObserver {

    /// <summary>
    /// The prefab used to render a chunk in unity.
    /// </summary>
    public GameObject chunkObjectPrefab;

    /// <summary>
    /// The level is loaded enough for the manager to begin working
    /// </summary>
    [HideInInspector] public bool isLoaded;

    /// <summary>
    /// The level this is managing
    /// </summary>
    public Level level {
      get;
      private set;
    }

    /// <summary>
    /// The pool of prefabs
    /// </summary>
    ChunkController[] chunkControllerPool;

    /// <summary>
    /// counter for new chunks waiting for controllers
    /// </summary>
    int newChunkCount = 0;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentBag<VoxelMeshData> chunksWaitingForAFreeController;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    List<VoxelMeshData> chunkControllerAssignmentWaitQueue;

    /// <summary>
    /// counter for newly activated chunks waiting to enqueue
    /// </summary>
    int newlyActivatedChunksCount = 0;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentBag<Coordinate> newlyActivatedChunks;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    List<Coordinate> chunksToActivate;

    /// <summary>
    /// The counter for newly deactivating chunks waiting to queue
    /// </summary>
    int newlyDeactivatedChunksCount = 0;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentBag<Coordinate> newlyDeactivatedChunks;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    List<Coordinate> chunksToDeactivate;

    /// <summary>
    /// The counter for newly gen'd meshes.
    /// </summary>
    int newlyGeneratedMeshesCount = 0;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentBag<ChunkController> chunksWithNewlyGeneratedMeshes;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    List<ChunkController> chunksToMesh;

    /// <summary>
    /// counter for out of focus meshes waiting to queue
    /// </summary>
    int outOfFocusMeshesCount = 0;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentBag<ChunkController> chunksWithOutOfFocusMeshes;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    List<ChunkController> chunksToDeMesh;

    ///// UNITY FUNCTIONS

    void Update() {
      if (!isLoaded) {
        return;
      }

      // NOTE:: Newly activated chunks goes first so we don't mesh then activate in the same frame
      /// go through the chunk activation queue and activate chunks
      queueNewlyActivatedChunks();
      chunksToActivate.RemoveAll(activatedChunkLocation => {
        // if the chunk doesn't have a meshed and baked controller yet, we can't activate it, so wait.
        if (!tryToGetAssignedChunkController(activatedChunkLocation, out ChunkController assignedController) // has a controller
          || !(assignedController.isActive && assignedController.isMeshed) // is active and meshed
          || !assignedController.checkColliderIsBaked() // colliders are baked
        ) {
          return false;
        }

        assignedController.enableObjectVisible();
        return true;
      });

      /// try to assign newly mehsed chunks that are waiting on controllers, if we run out.
      queueNewChunksWaitingForControllers();
      chunkControllerAssignmentWaitQueue.RemoveAll(chunkLocationWaitingForController => {
        return tryToAssignNewlyMeshedChunkToController(chunkLocationWaitingForController);
      });

      /// try to assign meshes to the chunks with newly generated meshes
      queueChunksWithNewlyGeneratedMeshes();
      chunksToMesh.RemoveAll(chunkToMesh => {
        chunkToMesh.updateMeshWithChunkData();

        return true;
      });

      /// try to remove meshes for the given chunk and reset it's mesh data
      queueChunksWithOutOfFocusMeshes();
      chunksToDeMesh.RemoveAll(chunkToMesh => {
        chunkToMesh.deactivateAndClear();

        return true;
      });

      /// go through the de-activation queue
      queueNewlyDeactivatedChunks();
      chunksToDeactivate.RemoveAll(deactivatedChunkLocation => {
        if (tryToGetAssignedChunkController(deactivatedChunkLocation, out ChunkController assignedController)) {
          assignedController.disableObjectVisible();
        }

        return true;
      });
    }

    ///// PUBLIC FUNCTIONS

    /// <summary>
    /// Initilize this chunk controller for it's provided level.
    /// </summary>
    public void initializeFor(Level level) {
      if (chunkObjectPrefab == null) {
        World.Debugger.logError("UnityLevelController Missing chunk prefab, can't work");
      } else if (level == null) {
        World.Debugger.logError("No level provided to level controller");
      } else {
        /// init
        this.level = level;
        chunksWaitingForAFreeController = new ConcurrentBag<VoxelMeshData>();
        chunkControllerAssignmentWaitQueue = new List<VoxelMeshData>();
        chunksWithNewlyGeneratedMeshes = new ConcurrentBag<ChunkController>();
        chunksToMesh = new List<ChunkController>();
        newlyActivatedChunks = new ConcurrentBag<Coordinate>();
        chunksToActivate = new List<Coordinate>();
        chunksWithOutOfFocusMeshes = new ConcurrentBag<ChunkController>();
        chunksToDeMesh = new List<ChunkController>();
        newlyDeactivatedChunks = new ConcurrentBag<Coordinate>();
        chunksToDeactivate = new List<Coordinate>();

        // build the controller pool based on the maxed meshed chunk area we should ever have:
        ChunkResolutionAperture meshResolutionAperture = level.getApetureByPriority(Level.AperturePriority.Meshed);
        int diameterToMeshManager = meshResolutionAperture != null ? meshResolutionAperture.managedChunkRadius : level.chunkBounds.x;
        chunkControllerPool = new ChunkController[diameterToMeshManager * diameterToMeshManager * level.chunkBounds.y * 2];
        for (int index = 0; index < chunkControllerPool.Length; index++) {
          // for each chunk we want to be able to render at once, create a new pooled gameobject for it with the prefab that has a unitu chunk controller on it
          GameObject chunkObject = Instantiate(chunkObjectPrefab);
          chunkObject.transform.parent = gameObject.transform;
          ChunkController chunkController = chunkObject.GetComponent<ChunkController>();
          if (chunkController == null) {
            //@TODO: make a queue for these maybe, just in case?
            World.Debugger.logError($"No chunk controller on {chunkObject.name}");
          } else {
            chunkControllerPool[index] = chunkController;
            //chunkController.levelController = this;
            chunkObject.SetActive(false);
          }
        }

        /// this controller is now loaded
        isLoaded = true;
      }
    }

    /// <summary>
    /// Clear all rendered and stored level data that we have.
    /// </summary>
    public void clearAll() {
      level = null;
      isLoaded = false;
      foreach (ChunkController chunkController in chunkControllerPool) {
        if (chunkController != null) {
          Destroy(chunkController.gameObject);
        }
      }
    }

    /// <summary>
    /// Get notifications from other observers, EX:
    ///   block breaking and placing
    ///   player chunk location changes
    /// </summary>
    /// <param name="event">The event to notify this observer of</param>
    /// <param name="origin">(optional) the source of the event</param>
    public void notifyOf(IEvent @event, IObserver origin = null) {
      // ignore events if we have no level to control
      if (!isLoaded || level == null) {
        return;
      }

      switch (@event) {
        // when a chunk mesh comes into focus, or loads, set the mesh to a chunkManager
        case MeshGenerationAperture.ChunkMeshLoadingFinishedEvent cmfle:
          if (!tryToAssignNewlyMeshedChunkToController(cmfle.chunkMeshData)) {
            chunksWaitingForAFreeController.Add(cmfle.chunkMeshData);
            newChunkCount++;
          }
          break;
        // when the level finishes loading a chunk's mesh. Render it in world
        case ActiveChunkObjectAperture.SetChunkActiveEvent scae:
          newlyActivatedChunks.Add(scae.chunkID.Coordinate);
          newlyActivatedChunksCount++;
          break;
        /*case ActivateGameobjectResolutionAperture.SetChunkObjectInactiveEvent scoie:
          newlyDeactivatedChunks.Add(scoie.chunkLocation);
          newlyDeactivatedChunksCount++;
          break;
        case LoadedChunkMeshDataResolutionAperture.ChunkMeshMovedOutOfFocusEvent smmoof:
          if (tryToGetAssignedChunkController(smmoof.chunkLocation, out ChunkController assignedChunkController)) {
            chunksWithOutOfFocusMeshes.Add(assignedChunkController);
            outOfFocusMeshesCount++;
          }
          break;*/
        default:
          return;
      }
    }

    ///// SUB FUNCTIONS

    /// <summary>
    /// Try to assign a chunk to an unused controller.
    /// </summary>
    /// <param name="chunkLocation"></param>
    /// <returns>A bool for being used in Removeall, if the chunk should be removed from the wait queue.</returns>
    bool tryToAssignNewlyMeshedChunkToController(VoxelMeshData meshData) {
      if (!meshData.isEmpty) {
        // try to find an unused chunk controller and add it to the queue if it's valid
        if (getUnusedChunkController(meshData.chunkID.Coordinate, out ChunkController unusedChunkController)) {
          unusedChunkController.setChunkToRender(meshData);
          chunksWithNewlyGeneratedMeshes.Add(unusedChunkController);
          newlyGeneratedMeshesCount++;
          return true;
        // don't drop it yet, we didn't find a chunk controller.
        } else {
          return false;
        }
      // if the chunk isn't meshable, just drop it from the queue
      } else {
        return true;
      }
    }

    /// <summary>
    /// Get an unused chunk controller from the pool we made, while also making sure the chunk isn't already part of said pool.
    /// </summary>
    /// <returns></returns>
    bool getUnusedChunkController(Coordinate chunkLocationToSet, out ChunkController unusedChunkController) {
      unusedChunkController = null;
      bool foundUnusedController = false;
      foreach (ChunkController chunkController in chunkControllerPool) {
        // if the chunk is active and already has the location we're looking for, we return false
        if (chunkController.isActive) { // these ifs are seprate because of the else if below.
          if (chunkController.chunkLocation == chunkLocationToSet) {
            if (unusedChunkController != null) {
              unusedChunkController.isActive = false;
              unusedChunkController = null;
            }

            return false;
          }
          // if we found an inactive controller, and we're still looking for that, lets snag it and stop looking.
        } else if (!foundUnusedController) {
          chunkController.isActive = true;
          unusedChunkController = chunkController;
          foundUnusedController = true;
        }
      }

      // return true if we never found a chunkController already set to this new chunk location.
      return true;
    }

    /// <summary>
    /// Get the chunk controller that's already assigned to the given chunk location
    /// </summary>
    /// <param name="chunkLocation"></param>
    /// <returns></returns>
    bool tryToGetAssignedChunkController(Coordinate chunkLocation, out ChunkController assignedChunkController) {
      assignedChunkController = null;
      foreach (ChunkController chunkController in chunkControllerPool) {
        if (chunkController.isActive && chunkController.chunkLocation == chunkLocation.vec3) {
          assignedChunkController = chunkController;
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Attempt to queue newly assigned chunk controllers from the bag
    /// </summary>
    void queueNewChunksWaitingForControllers() {
      // get the # of assigned controllers at this moment in the bag.
      int newChunkCurrentCount = newChunkCount;

      // we'll try to take that many items out this loop around.
      while (0 < newChunkCurrentCount--) {
        if (chunksWaitingForAFreeController.TryTake(out VoxelMeshData newChunkLocation)
          && !chunkControllerAssignmentWaitQueue.Contains(newChunkLocation)
        ) {
          chunkControllerAssignmentWaitQueue.Add(newChunkLocation);
          newChunkCount--;
        }
      }
    }

    /// <summary>
    /// Attempt to queue newly assigned chunk controllers from the bag
    /// </summary>
    void queueChunksWithNewlyGeneratedMeshes() {
      // get the # of assigned controllers at this moment in the bag
      int newlyGeneratedMeshesCurrentCount = newlyGeneratedMeshesCount;

      // we'll try to take that many items out this loop around.
      while (0 < newlyGeneratedMeshesCurrentCount--) {
        if (chunksWithNewlyGeneratedMeshes.TryTake(out ChunkController chunkWithNewMeshLocation)
          && !chunksToMesh.Contains(chunkWithNewMeshLocation)
        ) {
          chunksToMesh.Add(chunkWithNewMeshLocation);
          newlyGeneratedMeshesCount--;
        }
      }
    }

    /// <summary>
    /// Attempt to queue newly assigned chunk controllers from the bag
    /// </summary>
    void queueChunksWithOutOfFocusMeshes() {
      // get the # of assigned controllers at this moment in the bag.
      int outOfFocusMeshesCurrentCount = outOfFocusMeshesCount;

      // we'll try to take that many items out this loop around.
      while (0 < outOfFocusMeshesCurrentCount--) {
        if (chunksWithOutOfFocusMeshes.TryTake(out ChunkController chunkWithNewMeshLocation)
          && !chunksToDeMesh.Contains(chunkWithNewMeshLocation)
        ) {
          chunksToDeMesh.Add(chunkWithNewMeshLocation);
          outOfFocusMeshesCount--;
        }
      }
    }

    /// <summary>
    /// Attempt to queue newly assigned chunk controllers from the bag
    /// </summary>
    void queueNewlyActivatedChunks() {
      // get the # of assigned controllers at this moment in the bag.
      int newlyActivatedChunksCurrentCount = newlyActivatedChunksCount;

      // we'll try to take that many items out this loop around.
      while (0 < newlyActivatedChunksCurrentCount--) {
        if (newlyActivatedChunks.TryTake(out Coordinate newlyDeactivatedChunkLocation)
          && !chunksToActivate.Contains(newlyDeactivatedChunkLocation)
        ) {
          chunksToActivate.Add(newlyDeactivatedChunkLocation);
          newlyActivatedChunksCount--;
        }
      }
    }

    /// <summary>
    /// Attempt to queue newly assigned chunk controllers from the bag
    /// </summary>
    void queueNewlyDeactivatedChunks() {
      // get the # of assigned controllers at this moment in the bag.
      int newlyDeactivatedChunksCurrentCount = newlyDeactivatedChunksCount;

      // we'll try to take that many items out this loop around.
      while (0 < newlyDeactivatedChunksCurrentCount--) {
        if (newlyDeactivatedChunks.TryTake(out Coordinate newlyDeactivatedChunkLocation)
          && !chunksToDeactivate.Contains(newlyDeactivatedChunkLocation)
        ) {
          chunksToDeactivate.Add(newlyDeactivatedChunkLocation);
          newlyDeactivatedChunksCount--;
        }
      }
    }
  }
}
