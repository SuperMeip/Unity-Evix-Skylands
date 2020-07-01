using System.Collections;

namespace Evix.Jobs {

  /// <summary>
  /// Interface for jobs run on other threads
  /// </summary>
  public interface IThreadedJob {

    /// <summary>
    /// Get if the job is done running
    /// </summary>
    bool isDone {
      get;
    }

    /// <summary>
    /// Get if the job is currently running
    /// </summary>
    bool isRunning {
      get;
    }

    /// <summary>
    /// Start the job
    /// </summary>
    void start();
  }
}