using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace GifDisplay;

public class Helper
{
  public static bool AreEqual(double a, double b, double epsilon = 1e-9)
  {
    return Math.Abs(a - b) < epsilon;
  }

  [Conditional("UNITY_EDITOR")]
  public static void Log(object message)
  {
    Debug.Log(message);
  }

  [Conditional("UNITY_EDITOR")]
  public static void Log(object message, Object context)
  {
    Debug.Log(message, context);
  }
}