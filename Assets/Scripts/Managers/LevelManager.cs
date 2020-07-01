using Evix.Controllers;
using Evix.Events;
using Evix.Terrain.Collections;
using Evix.Terrain.Resolution;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Evix.Managers {

  /// <summary>
  /// A manager manages data on the backend.
  /// This manager manages level data and the generation of the level based on it's foci
  /// </summary>
  public class LevelManager : MonoBehaviour, IObserver {

    /// <summary>
    /// The prefab used to render a chunk in unity.
    /// </summary>
    public GameObject chunkObjectPrefab;

    /// <summary>
    /// The level this is managing
    /// </summary>
    public Level level {
      get;
      private set;
    }

    /// <summary>
    /// The level is loaded enough for the manager to begin working
    /// </summary>
    public bool isLoaded {
      get;
      private set;
    }

    /// <summary>
    /// The pool of prefabs
    /// </summary>
    ChunkController[] chunkControllerPool;

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentPriorityQueue<float, VoxelMeshData> chunkMeshesWaitingForAFreeController 
      = new ConcurrentPriorityQueue<float, VoxelMeshData>();

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentPriorityQueue<float, Coordinate> chunksToActivate 
      = new ConcurrentPriorityQueue<float, Coordinate>();

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentPriorityQueue<float, Coordinate> chunksToDeactivate
      = new ConcurrentPriorityQueue<float, Coordinate>();

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentPriorityQueue<float, ChunkController> chunksToMesh 
      = new ConcurrentPriorityQueue<float, ChunkController>();

    /// <summary>
    /// Chunk controllers waiting for assignement and activation
    /// </summary>
    ConcurrentPriorityQueue<float, ChunkController> chunksToDeMesh 
      = new ConcurrentPriorityQueue<float, ChunkController>();

    /// <summary>
    /// The priority weighted job queue for all apeture jobs being managed by this level
    /// </summary>
    readonly ConcurrentPriorityQueue<ApertureWorkQueuePriority, ChunkResolutionAperture.ApetureChunkAdjustment> apertureJobQueue
      = new ConcurrentPriorityQueue<ApertureWorkQueuePriority, ChunkResolutionAperture.ApetureChunkAdjustment>();

    /// <summary>
    /// The jobs running currently for the appeture. These handles let us see when they finish
    /// </summary>
    readonly ConcurrentDictionary<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture> adjustmentJobsReadyToRun
      = new ConcurrentDictionary<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture>();

    /// <summary>
    /// The jobs running currently for the appeture. These handles let us see when they finish
    /// </summary>
    readonly ConcurrentDictionary<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture.ApertureJobHandle> runningJobs
      = new ConcurrentDictionary<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture.ApertureJobHandle>();

    /// <summary>
    /// The thread running for the apertureJobQueue
    /// </summary>
    Thread apertureJobQueueManagerThread;

    /// <summary>
    /// IF the level manager queue should be running
    /// </summary>
    bool runLevelManagerQueue = true;

    /// <summary>
    /// Initilize the level queue manager to follow the foci and appetures of the level
    /// </summary>
    /// <param name="level"></param>
    public void initializeFor(Level level) {
      if (chunkObjectPrefab == null) {
        World.Debugger.logError("UnityLevelController Missing chunk prefab, can't work");
      } else if (level == null) {
        World.Debugger.logError("No level provided to level controller");
      } else {
        /// init
        this.level = level;

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
            World.Debugger.logError($"No chunk controller on {chunkObject.name}");
          } else {
            chunkControllerPool[index] = chunkController;
            chunkController.levelManager = this;
            chunkObject.SetActive(false);
          }
        }

        /// this controller is now loaded
        isLoaded = true;

        /// add the focus initilization jobs to the queue for each apeture
        level.forEachFocus(focus => {
          level.forEachAperture(aperture => {
            foreach (ChunkResolutionAperture.ApetureChunkAdjustment chunkAdjustment in aperture.getAdjustmentsForFocusInitilization(focus)) {
              apertureJobQueue.Enqueue(getCurrentPriorityForChunk(chunkAdjustment.chunkID, aperture), chunkAdjustment);
            }
          });
        });

        /// start the manager job in a seperate thread
        apertureJobQueueManagerThread = new Thread(() => ManageQueue()) {
          Name = "Level Aperture Queue Manager"
        };
        apertureJobQueueManagerThread.Start();
      }
    }

    #region UpdateLoop

    /// <summary>
    /// Update loop. Get the work from the leve management queue and activate chunks and meshes
    /// </summary>
    void Update() {
      if (!isLoaded) {
        return;
      }

      // first schedule all jobs that are ready from the levelManager thread
      runReadyJobs();
      // check for jobs that finished running
      checkForFinishedJobs();

      // NOTE:: Newly activated chunks goes first so we don't mesh then activate in the same frame
      /// go through the chunk activation queue and activate chunks
      if (chunksToActivate.TryDequeue(out KeyValuePair<float, Coordinate> chunkToActivate)) {
        // if the chunk doesn't have a meshed and baked controller yet, we can't activate it, so wait.
        if (!tryToGetAssignedChunkController(chunkToActivate.Value, out ChunkController assignedController) // has a controller
          || !(assignedController.isActive && assignedController.isMeshed) // is active and meshed
          || !assignedController.checkColliderIsBaked() // colliders are baked
        ) {
          chunksToActivate.Enqueue(chunkToActivate);
        } else assignedController.enableObjectVisible();
      }

      /// try to assign newly mehsed chunks that are waiting on controllers, if we run out.
      if (chunkMeshesWaitingForAFreeController.TryDequeue(out KeyValuePair<float, VoxelMeshData> chunkMeshWaitingForController)) { 
        if (!tryToAssignNewlyMeshedChunkToController(chunkMeshWaitingForController.Value)) {
          chunkMeshesWaitingForAFreeController.Enqueue(chunkMeshWaitingForController);
        }
      }

      /// try to assign meshes to the chunks with newly generated meshes
      if (chunksToMesh.TryDequeue(out KeyValuePair<float, ChunkController> chunkToMesh)) {
        chunkToMesh.Value.updateMeshWithChunkData();
      }

      /// try to remove meshes for the given chunk and reset it's mesh data
      if (chunksToDeMesh.TryDequeue(out KeyValuePair<float, ChunkController> chunkToDemesh)) { 
        chunkToDemesh.Value.deactivateAndClear();
      }

      /// go through the de-activation queue
      if (chunksToDeactivate.TryDequeue(out KeyValuePair<float, Coordinate> deactivatedChunkLocation)) {
        if (tryToGetAssignedChunkController(deactivatedChunkLocation.Value, out ChunkController assignedController)) {
          assignedController.disableObjectVisible();
        }
      }
    }

    /// <summary>
    /// Run any jobs we've decided are ready
    /// Must be called from the main thread
    /// </summary>
    void runReadyJobs() {
      foreach (KeyValuePair<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture> chunkAndApetureForJob in adjustmentJobsReadyToRun) {
        ChunkResolutionAperture.ApertureJobHandle jobHandle = chunkAndApetureForJob.Value.getJobFor(chunkAndApetureForJob.Key.chunkID, chunkAndApetureForJob.Key.type);
        jobHandle.schedule();
        runningJobs.TryAdd(chunkAndApetureForJob.Key, jobHandle);
        adjustmentJobsReadyToRun.TryRemove(chunkAndApetureForJob.Key, out _);
      }
    }

    /// <summary>
    /// Check to see what jobs are finished
    /// only can be called from the main thread
    /// </summary>
    void checkForFinishedJobs() {
      foreach (KeyValuePair<ChunkResolutionAperture.ApetureChunkAdjustment, ChunkResolutionAperture.ApertureJobHandle> jobHandle in runningJobs) {
        if (jobHandle.Value.jobIsComplete) {
          runningJobs.TryRemove(jobHandle.Key, out _);
          jobHandle.Value.aperture.onJobComplete(jobHandle.Key.chunkID,  jobHandle.Value);
        }
      }
    }

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
          chunksToMesh.Enqueue(getDistanceToClosestFocus(meshData.chunkID), unusedChunkController);
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
      return foundUnusedController;
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

    #endregion

    #region ManageLoop

    /// <summary>
    /// Manage which chunks are queued for loading with what level apertures.
    /// </summary>
    void ManageQueue() {
      while (runLevelManagerQueue) {
        /// Step 1: listen for focus changes.
        level.forEachFocus(focus => {
          if (focus.currentChunk != focus.previousChunk) {
            /// Step 2: get all the aperture jobs for the focus changes into the queue.
            level.forEachAperture(aperture => {
              foreach (ChunkResolutionAperture.ApetureChunkAdjustment adjustment in aperture.getAdjustmentsForFocusLocationChange(focus)) {
                apertureJobQueue.Enqueue(getCurrentPriorityForChunk(adjustment.chunkID, aperture, adjustment.type), adjustment);
              }
            });

            // callback, to update the focus' chunk location
            focus.onFocusUpdatedForLevel(focus.currentChunk);
          }
        });

        /// Step 3: Iterate over the next item in the queue and try to schedule a job for it
        if (apertureJobQueue.TryDequeue(out KeyValuePair<ApertureWorkQueuePriority, ChunkResolutionAperture.ApetureChunkAdjustment> queueItemWithPriority)) {
          // if the item was cancled or is invalid, skip it.
          ChunkResolutionAperture apertureForQueueItem = level.getApetureByPriority(queueItemWithPriority.Key.aperturePriority);
          if (!isAValidQueueItem(queueItemWithPriority.Value, apertureForQueueItem)) {
            continue;
          }

          // if the item is ready, offer it up to the running jobs to pick up.
          // we'll try to add it to the running jobs list
          if (itemIsReady(queueItemWithPriority.Value.chunkID, apertureForQueueItem)
            && !runningJobs.ContainsKey(queueItemWithPriority.Value)
          ) {
            adjustmentJobsReadyToRun.TryAdd(queueItemWithPriority.Value, apertureForQueueItem);
            World.Debugger.log($"Apeture Job type {apertureForQueueItem.GetType()} ready for {queueItemWithPriority.Value.chunkID}");
            // if it's not ready, or there's a conflict requeue
            // if there's a conflict, it means a job is already running on this chunk and we should wait for that one to finish
          } else {
            apertureJobQueue.Enqueue(
              getCurrentPriorityForChunk(queueItemWithPriority.Value.chunkID, apertureForQueueItem, queueItemWithPriority.Key.adjustmentType),
              queueItemWithPriority.Value
            );
          }
        }
      }
    }

    /// <summary>
    /// validate queue items
    /// </summary>
    /// <returns></returns>
    bool isAValidQueueItem(ChunkResolutionAperture.ApetureChunkAdjustment adjustment, ChunkResolutionAperture aperture) {
      return aperture.chunkIsValid(adjustment);
    }

    /// <summary>
    /// if the queue item is ready to go, or should be put back in the queue
    /// </summary>
    /// <returns></returns>
    bool itemIsReady(Chunk.ID chunkID, ChunkResolutionAperture aperture) {
      bool isReady = aperture.chunkIsReady(chunkID);
      if (isReady) {
        aperture.prepareChunkJobData(chunkID);
      }

      return isReady;
    }

    /// <summary>
    /// Get the priority object for a given chunk being loaded by the given apeture
    /// </summary>
    /// <param name="chunkID"></param>
    /// <param name="aperture"></param>
    /// <returns></returns>
    ApertureWorkQueuePriority getCurrentPriorityForChunk(
      Chunk.ID chunkID,
      ChunkResolutionAperture aperture,
      ChunkResolutionAperture.FocusAdjustmentType adjustmentType = ChunkResolutionAperture.FocusAdjustmentType.InFocus
    ) {
      return new ApertureWorkQueuePriority(
        aperture.priority,
        (int)getDistanceToClosestFocus(chunkID, aperture.yWeightMultiplier),
        adjustmentType
      );
    }

    /// <summary>
    /// Get the distance from this chunk to the closest level focus
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    float getDistanceToClosestFocus(Chunk.ID chunkID, float yWeightMultiplier = 5.0f) {
      float closestFocusDistance = float.MaxValue;
      level.forEachFocus(focus => {
        float focusDistance = focus.currentChunk.Coordinate.distanceYFlattened(chunkID.Coordinate, yWeightMultiplier);
        closestFocusDistance = focusDistance < closestFocusDistance ? focusDistance : closestFocusDistance;
      });

      return closestFocusDistance;
    }

    #endregion

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
            chunkMeshesWaitingForAFreeController.Enqueue(getDistanceToClosestFocus(cmfle.chunkMeshData.chunkID), cmfle.chunkMeshData);
          }
          break;
        // when the level finishes loading a chunk's mesh. Render it in world
        case ActiveChunkObjectAperture.SetChunkActiveEvent scae:
          chunksToActivate.Enqueue(getDistanceToClosestFocus(scae.chunkID), scae.chunkID.Coordinate);
          break;
        case ActiveChunkObjectAperture.SetChunkInactiveEvent scie:
          chunksToDeactivate.Enqueue(getDistanceToClosestFocus(scie.chunkID), scie.chunkID.Coordinate);
          break;
        case MeshGenerationAperture.RemoveChunkMeshEvent rcme:
          if (tryToGetAssignedChunkController(rcme.chunkID.Coordinate, out ChunkController assignedChunkController)) {
            chunksToDeMesh.Enqueue(getDistanceToClosestFocus(rcme.chunkID), assignedChunkController);
          }
          break;
        default:
          return;
      }
    }

    /// <summary>
    /// Struct for comparing apeture job priority
    /// </summary>
    struct ApertureWorkQueuePriority : IComparable<ApertureWorkQueuePriority> {

      /// <summary>
      /// The priority of the apeture for this job in this level
      /// </summary>
      public readonly Level.AperturePriority aperturePriority;

      /// <summary>
      /// The distance to the closest level focus
      /// </summary>
      readonly int distanceToClosestFocus;

      /// <summary>
      /// The type of adjustment the apeture if preforming
      /// </summary>
      public readonly ChunkResolutionAperture.FocusAdjustmentType adjustmentType;

      /// <summary>
      /// Create a new apeture work queue priority key object
      /// </summary>
      /// <param name="aperturePriority"></param>
      /// <param name="distanceToClosestFocus"></param>
      internal ApertureWorkQueuePriority(
        Level.AperturePriority aperturePriority,
        int distanceToClosestFocus,
        ChunkResolutionAperture.FocusAdjustmentType adjustmentType = ChunkResolutionAperture.FocusAdjustmentType.InFocus
      ) {
        this.aperturePriority = aperturePriority;
        this.distanceToClosestFocus = distanceToClosestFocus;
        this.adjustmentType = adjustmentType;
      }

      /// <summary>
      /// Compare first by rounded distance, then by focus priority type.
      /// </summary>
      /// <param name="other"></param>
      /// <returns></returns>
      public int CompareTo(ApertureWorkQueuePriority other) {
        // get priority by adding the distance a an int, to the apeture type * 3 (for load buffer)
        int thisPriorityValue = distanceToClosestFocus + (int)aperturePriority * 3;
        int otherPriorityValue = other.distanceToClosestFocus + (int)other.aperturePriority * 3;
        // @todo:: test this, not sure if flipping the comparison if either is out of focus will always work
        if (other.adjustmentType == ChunkResolutionAperture.FocusAdjustmentType.OutOfFocus || adjustmentType == ChunkResolutionAperture.FocusAdjustmentType.OutOfFocus) {
          return otherPriorityValue.CompareTo(thisPriorityValue);
        }
        return thisPriorityValue.CompareTo(otherPriorityValue);
      }

      /// <summary>
      /// string override
      /// </summary>
      /// <returns></returns>
      public override string ToString() {
        return $"${aperturePriority}@{distanceToClosestFocus}";
      }
    }

    /// <summary>
    /// Close the level manager thread on destory
    /// </summary>
    private void OnDestroy() {
      runLevelManagerQueue = false;
      if (apertureJobQueueManagerThread != null) {
        apertureJobQueueManagerThread.Abort();
      }
    }
  }
}