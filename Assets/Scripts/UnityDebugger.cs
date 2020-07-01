using UnityEngine;

namespace Evix {

  public class UnityDebugger {
    /// <summary>
    /// If the debugger is enabled.
    /// </summary>
#if DEBUG
    public bool isEnabled {
      get;
      private set;
    } = true;
#else
    public bool isEnabled {
      get;
      private set;
    } = false;
#endif

    /// <summary>
    /// Make a new unity debugger. Override debug mode if you want
    /// </summary>
    public UnityDebugger() {}
    public UnityDebugger(bool isEnabled) {
      this.isEnabled = isEnabled;
    }

    /// <summary>
    /// Log a debug message
    /// </summary>
    /// <param name="debugMessage"></param>
    public void log(string debugMessage) {
      if (isEnabled) {
        Debug.Log(debugMessage);
      }
    }

    /// <summary>
    /// Log a debug error
    /// </summary>
    /// <param name="debugMessage"></param>
    public void logError(string debugMessage) {
      if (isEnabled) {
        Debug.LogError(debugMessage);
      }
    }
  }
}
