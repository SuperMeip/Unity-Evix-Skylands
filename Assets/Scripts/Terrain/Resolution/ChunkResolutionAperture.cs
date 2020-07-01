using Evix.Terrain.Collections;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Jobs;

namespace Evix.Terrain.Resolution {

  /// <summary>
  /// An aperture (area) that manages how resolved/loaded a chunk is within it's managed space.
  /// </summary>
  public abstract class ChunkResolutionAperture {

    /// <summary>
    /// How a chunk has changed in relation to this apeture when a focus moves.
    /// Has the chunk moved in focus, or out of focus?
    /// </summary>
    public enum FocusAdjustmentType { InFocus, OutOfFocus};

    /// <summary>
    /// If this type of apeture has out of focus logic. Defaults to false.
    /// </summary>
    readonly bool HasOutOfFocusLogic;

    /// <summary>
    /// The managed chunk area radius, X and Z. Height may be different.
    /// </summary>
    public int managedChunkRadius {
      get;
      private set;
    }

    /// <summary>
    /// The managed chunk area height
    /// </summary>
    public int managedChunkHeightRadius {
      get;
      private set;
    }

    /// <summary>
    /// The y weight multiplier of this apeture, used for priority and distance skewing
    /// </summary>
    public float yWeightMultiplier = 5.0f;

    /// <summary>
    /// The priority of this apeture for loading in the level
    /// </summary>
    public Level.AperturePriority priority {
      get;
      private set;
    }

    /// <summary>
    /// The level this apeture works for
    /// </summary>
    protected Level level {
      get;
      private set;
    }

    /// <summary>
    /// The bounds this resolution manager manages currently ordered by focus id.
    /// </summary>
    readonly Dictionary<int, Coordinate[]> managedChunkBoundsByFocusID;

    ///// CONSTRUCTORS

    /// <summary>
    /// Default constructor
    /// </summary>
    /// <param name="level"></param>
    /// <param name="managedChunkRadius"></param>
    /// <param name="managedChunkHeight"></param>
    protected ChunkResolutionAperture(int managedChunkRadius, bool hasOutOfFocusLogic = false, int managedChunkHeight = 0) {
      this.managedChunkRadius = managedChunkRadius;
      HasOutOfFocusLogic = hasOutOfFocusLogic;
      managedChunkHeightRadius = managedChunkHeight == 0 ? managedChunkRadius : managedChunkHeight;
      managedChunkBoundsByFocusID = new Dictionary<int, Coordinate[]>();
    }

    ///// PUBLIC FUNCTIONS

    /// <summary>
    /// Create and schedule the child job for this chunk using a unity IJob
    /// </summary>
    /// <param name="chunkID"></param>
    public abstract ApertureJobHandle getJobFor(Chunk.ID chunkID, FocusAdjustmentType adjustmentType);

    /// <summary>
    /// Get the chunks for a new focus point being initilized
    /// </summary>
    /// <param name="newFocalPoint"></param>
    public List<ApetureChunkAdjustment> getAdjustmentsForFocusInitilization(ILevelFocus newFocalPoint) {
      int focusID = level.getFocusID(newFocalPoint);
      if (focusID < 0) {
        throw new System.ArgumentOutOfRangeException("focusID", $"Aperture; {GetType()}, tried to load a new focus of type {newFocalPoint.GetType()} that isn't registered to it's level");
      }

      List<ApetureChunkAdjustment> chunkAdjustments = new List<ApetureChunkAdjustment>();
      Coordinate[] managedChunkBounds = getManagedChunkBounds(newFocalPoint);
      managedChunkBoundsByFocusID[focusID] = managedChunkBounds;

      /// just get the new in focus chunks for the whole managed area
      managedChunkBounds[0].until(managedChunkBounds[1], inFocusChunkLocation => {
        chunkAdjustments.Add(new ApetureChunkAdjustment(inFocusChunkLocation));
      });

      return chunkAdjustments;
    }

    /// <summary>
    /// Adjust the bounds and resolution loading for the given focus.
    /// </summary>
    /// <param name="focus"></param>
    public List<ApetureChunkAdjustment> getAdjustmentsForFocusLocationChange(ILevelFocus focus) {
      int focusID = level.getFocusID(focus);
      if (focusID < 0) {
        throw new System.ArgumentOutOfRangeException("focusID", $"Aperture; {GetType()}, tried to load a new focus of type {focus.GetType()} that isn't registered to it's level");
      }

      List<ApetureChunkAdjustment> chunkAdjustments = new List<ApetureChunkAdjustment>();
      Coordinate[] newManagedChunkBounds = getManagedChunkBounds(focus);

      /// get newly in focus chunks
      newManagedChunkBounds.forEachPointNotWithin(managedChunkBoundsByFocusID[focusID], inFocusChunkLocation => {
        chunkAdjustments.Add(new ApetureChunkAdjustment(inFocusChunkLocation));
      });

      /// see if we should get newly out of focus chunks
      if (HasOutOfFocusLogic) {
        managedChunkBoundsByFocusID[focusID].forEachPointNotWithin(newManagedChunkBounds, inFocusChunkLocation => {
          chunkAdjustments.Add(new ApetureChunkAdjustment(inFocusChunkLocation, FocusAdjustmentType.OutOfFocus));
        });
      }

      managedChunkBoundsByFocusID[focusID] = newManagedChunkBounds;
      return chunkAdjustments;
    }

    /// <summary>
    /// Get if the given chunk is valid for this aperture.
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public bool chunkIsValid(ApetureChunkAdjustment adjustment) {
      bool chunkIsInFocusBounds = isWithinManagedBounds(adjustment.chunkID.Coordinate);
      if ((!chunkIsInFocusBounds && adjustment.type == FocusAdjustmentType.InFocus)
        || (chunkIsInFocusBounds && adjustment.type == FocusAdjustmentType.OutOfFocus)
      ) {
        return false;
      }

      return validateChunk(adjustment.chunkID, out _);
    }

    /// <summary>
    /// Check if the location is within any of the managed bounds
    /// </summary>
    /// <param name="chunkLocation"></param>
    /// <returns></returns>
    public bool isWithinManagedBounds(Coordinate chunkLocation) {
      foreach (Coordinate[] bounds in managedChunkBoundsByFocusID.Values) {
        if (chunkLocation.isWithin(bounds)) {
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Act on the job handle now that the job is complete
    /// </summary>
    /// <param name="key"></param>
    /// <param name="job"></param>
    public virtual void onJobComplete(Chunk.ID chunkID, ApertureJobHandle finishedJobHandle) {
      finishedJobHandle.markComplete();
      handleFinishedJob(chunkID, ref finishedJobHandle);
    }

    /// <summary>
    /// Get if the given chunk is ready for a job from this aperture
    /// </summary>
    /// <param name="chunkID"></param>
    /// <returns></returns>
    public virtual bool chunkIsReady(Chunk.ID chunkID) {
      return true;
    }

    /// <summary>
    /// prepare data for the chunk job if we need to
    /// </summary>
    /// <param name="chunkID"></param>
    public virtual void prepareChunkJobData(Chunk.ID chunkID) {}

    ///// INTERNAL FUNCTIONS

    /// <summary>
    /// Set the level this resolution apeture is working for, and it's priority in that level
    /// </summary>
    /// <param name="level"></param>
    internal void setLevel(Level level, Level.AperturePriority priority) {
      this.level = level;
      this.priority = priority;
    }

    /// <summary>
    /// Validate the chunk for this aperture.
    /// Should it be being managed still?
    /// </summary>
    /// <param name="chunkID">The id of the chunk to validate</param>
    /// <param name="chunk">The chunk grabbed for validation, used to speed up chains/callbacks</param>
    /// <returns></returns>
    internal virtual bool validateChunk(Chunk.ID chunkID, out Chunk chunk) {
      if (!level.chunks.TryGetValue(chunkID, out chunk)) {
        chunk = null;
      }

      return true;
    }

    /// <summary>
    /// What to do with a job for this aperture when it's done
    /// </summary>
    /// <param name="key"></param>
    /// <param name="job"></param>
    protected abstract void handleFinishedJob(Chunk.ID chunkID, ref ApertureJobHandle finishedJobHandle);

    ///// SUB FUNCTIONS

    /// <summary>
    /// Get the managed chunk bounds for the given focus.
    /// </summary>
    /// <param name="focus"></param>
    /// <returns></returns>
    Coordinate[] getManagedChunkBounds(ILevelFocus focus) {
      Coordinate focusLocation = focus.currentChunk.Coordinate;
      return new Coordinate[] {
        (
          Math.Max(focusLocation.x - managedChunkRadius, 0),
          Math.Max(focusLocation.y - managedChunkHeightRadius, 0),
          Math.Max(focusLocation.z - managedChunkRadius, 0)
        ),
        (
          Math.Min(focusLocation.x + managedChunkRadius, level.chunkBounds.x),
          Math.Min(focusLocation.y + managedChunkHeightRadius, level.chunkBounds.y),
          Math.Min(focusLocation.z + managedChunkRadius, level.chunkBounds.z)
        )
      };
    }

    /// <summary>
    /// A struct representing an adjustment to a chunk in relation to an apeture
    /// Used to say if chunk XYZ has moved in or out of focus
    /// </summary>
    public struct ApetureChunkAdjustment {

      /// <summary>
      /// The chunk being adjusted
      /// </summary>
      readonly public Chunk.ID chunkID;
      /// <summary>
      /// The type of adjustment
      /// </summary>
      readonly public FocusAdjustmentType type;

      /// <summary>
      /// Make a new adjustment
      /// </summary>
      /// <param name="chunkID"></param>
      /// <param name="adjustmentType"></param>
      public ApetureChunkAdjustment(Chunk.ID chunkID, FocusAdjustmentType adjustmentType = FocusAdjustmentType.InFocus) {
        this.chunkID = chunkID;
        type = adjustmentType;
      }

      /// <summary>
      /// string override
      /// </summary>
      /// <returns></returns>
      public override string ToString() {
        return $"{chunkID} is now {type}";
      }
    }

    /// <summary>
    /// A simple job handle skin with aperture data
    /// </summary>
    public struct ApertureJobHandle {

      /// <summary>
      /// if the job has been started yet.
      /// </summary>
      public bool hasBeenScheduled {
        get;
        private set;
      }

      /// <summary>
      /// The apeture this handle is for
      /// </summary>
      public ChunkResolutionAperture aperture {
        get;
      }

      /// <summary>
      /// The job being handled
      /// </summary>
      public IJob job {
        get;
      }

      /// <summary>
      /// The original job handle
      /// </summary>
      public JobHandle jobHandle {
        get;
        private set;
      }

      /// <summary>
      /// Get if the job is complete yet.
      /// Can only be called from the main thread
      /// </summary>
      public bool jobIsComplete {
        get => jobHandle.IsCompleted;
      }

      public ApertureJobHandle(IJob job, ChunkResolutionAperture aperture) {
        jobHandle = default;
        hasBeenScheduled = false;
        this.job = job;
        this.aperture = aperture;
      }

      /// <summary>
      /// Schedule the job using the generic function
      /// </summary>
      public void schedule() {
        var method = typeof(ApertureJobHandle).GetMethod(nameof(ApertureJobHandle.scheduleGeneric), BindingFlags.Instance | BindingFlags.NonPublic);
        method = method.MakeGenericMethod(job.GetType());
        jobHandle = (JobHandle)method.Invoke(this, new object[] {job});
        hasBeenScheduled = true;
      }

      /// <summary>
      /// Mark the job complete
      /// </summary>
      public void markComplete() {
        jobHandle.Complete();
      }

      /// <summary>
      /// Schedule the job to run
      /// </summary>
      JobHandle scheduleGeneric<JobType>(ref IJob job) where JobType : struct, IJob {
        return IJobExtensions.Schedule((JobType)job);
      }
    }
  }
}
