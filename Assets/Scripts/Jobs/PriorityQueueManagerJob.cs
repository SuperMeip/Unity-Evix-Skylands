using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Evix.Jobs {
  public abstract class PriorityQueueManagerJob<PriorityType, QueueItemType> : ThreadedJob
    where PriorityType : IComparable<PriorityType>
    where QueueItemType : IComparable<QueueItemType> {

    /// <summary>
    /// The item queue
    /// </summary>
    ConcurrentPriorityQueue<PriorityType, QueueItemType> queue;

    /// <summary>
    /// If an item has been canceled, we just skip it when the queue runs.
    /// </summary>
    protected ConcurrentDictionary<QueueItemType, bool> canceledItems;

    /// <summary>
    /// Used for reporting what items this job manager is currently running on
    /// </summary>
    ConcurrentDictionary<QueueItemType, bool> runningChildJobs;

    ///// CONSTRUCTORS
    
    public PriorityQueueManagerJob() {
      queue = new ConcurrentPriorityQueue<PriorityType, QueueItemType>();
      canceledItems = new ConcurrentDictionary<QueueItemType, bool>();
      runningChildJobs = new ConcurrentDictionary<QueueItemType, bool>();
    }

    ///// PUBLIC FUNCTIONS

    /// <summary>
    /// Add a bunch of objects to the queue for processing
    /// </summary>
    /// <param name="queueItems"></param>
    /// <param name="sortQueue">whether or not to sort the queue on add.</param>
    public void enqueue(QueueItemType[] queueItems) {
      foreach (QueueItemType queueItem in queueItems) {
        queue.Enqueue(getPriorityAndPackageItem(queueItem));
        // if the chunk was canceled in the past, remove the cancelation token when we add it back
        if (canceledItems.TryGetValue(queueItem, out _)) {
          canceledItems.TryRemove(queueItem, out _);
        }
      }

      // if the queue manager job isn't running, start it
      if (!isRunning) {
        start();
      }
    }

    /// <summary>
    /// if there's any child jobs running for the given ojects, stop them and dequeue
    /// </summary>
    /// <param name="queueObject"></param>
    /// <param name="sortQueue">whether or not to sort the queue on add.</param>
    public void dequeue(QueueItemType[] queueItems) {
      if (isRunning) {
        foreach (QueueItemType queueItem in queueItems) {
          canceledItems.TryAdd(queueItem, true);
        }
      }
    }

    /// <summary>
    /// Get all the queue items in an array
    /// </summary>
    /// <returns></returns>
    public QueueItemType[] getAllQueuedItems() {
      return queue.ToArray().Select(item => item.Value).ToArray();
    }

    /// <summary>
    /// Get all the items this job manager is currently running on
    /// </summary>
    /// <returns></returns>
    public QueueItemType[] getAllItemsWithRunningJobs() {
      return runningChildJobs.Keys.ToArray();
    }

    ///// INTERNAL FUNCTIONS

    /// <summary>
    /// Get the priority for the given queue item
    /// </summary>
    /// <param name="queueItem"></param>
    /// <returns></returns>
    protected abstract PriorityType getPriority(QueueItemType queueItem);

    /// <summary>
    /// The job to run on the queue item when it's time comes
    /// </summary>
    /// <param name="queueItem"></param>
    protected abstract void childJob(QueueItemType queueItem);

    /// <summary>
    /// validate queue items
    /// </summary>
    /// <param name="queueItem"></param>
    /// <returns></returns>
    protected virtual bool isAValidQueueItem(QueueItemType queueItem) {
      return true;
    }

    /// <summary>
    /// if the queue item is ready to go, or should be put back in the queue
    /// </summary>
    /// <param name="queueItem"></param>
    /// <returns></returns>
    protected virtual bool itemIsReady(QueueItemType queueItem) {
      return true;
    }

    ///// SUB FUNCTIONS

    /// <summary>
    /// Run the function on the queue
    /// </summary>
    protected override void jobFunction() {
      while (queue.Count > 0) {
        if (queue.TryDequeue(out KeyValuePair<PriorityType, QueueItemType> queueItemWithPriority)) {
          // if the item was cancled or is invalid, skip it.
          if (itemIsCanceled(queueItemWithPriority.Value) || !isAValidQueueItem(queueItemWithPriority.Value)) {
            continue;
          }

          // if the item is ready, offer it up to the running jobs to pick up.
          if (itemIsReady(queueItemWithPriority.Value)) {
            queueJobFor(queueItemWithPriority.Value);
          // update priority and requeue
          } else {
            queue.Enqueue(getPriorityAndPackageItem(queueItemWithPriority.Value));
          }
        }
      }
    }

    /// <summary>
    /// Check if the queue item has been canceled.
    /// </summary>
    /// <param name="queueItem"></param>
    /// <returns></returns>
    bool itemIsCanceled(QueueItemType queueItem) {
      if (canceledItems.TryRemove(queueItem, out _)) {
        // if it had a cancelation token stored, it's canceled
        return true;
      }

      return false;
    }

    /// <summary>
    /// Get the priority and queue item packaged for storage.
    /// </summary>
    /// <param name="queueItem"></param>
    /// <returns></returns>
    KeyValuePair<PriorityType, QueueItemType> getPriorityAndPackageItem(QueueItemType queueItem) {
      return new KeyValuePair<PriorityType, QueueItemType>(getPriority(queueItem), queueItem);
    }

    /// <summary>
    /// Run the child job, and do stuff around it.
    /// </summary>
    /// <param name="queueItem"></param>
    void runChildJob(QueueItemType queueItem) {
      runningChildJobs.TryAdd(queueItem, true);
      childJob(queueItem);
      runningChildJobs.TryRemove(queueItem, out _);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="queueItem"></param>
    void queueJobFor(QueueItemType queueItem) {
      ThreadPool.QueueUserWorkItem(state => runChildJob(queueItem) , null);
    }
  }
}
