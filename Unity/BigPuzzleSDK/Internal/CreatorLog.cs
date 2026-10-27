using System;
using UnityEngine;

namespace BigPuzzleSDK {
    // Not for your own code. In the game, Unity's own logging calls (Debug.Log and friends) are missing, so when the
    // exporter builds your scripts' plugin DLL it points every call to them at the SDK's class of this name (the real
    // one is in the mod, with the same members). Creators just write Debug.Log. These stand-ins only exist so the
    // exporter can find the members; they forward to Unity's own calls.
    public static class CreatorLog {
        public static void Log(object message) { Debug.Log(message); }
        public static void Log(object message, UnityEngine.Object context) { Debug.Log(message, context); }
        public static void LogWarning(object message) { Debug.LogWarning(message); }
        public static void LogWarning(object message, UnityEngine.Object context) { Debug.LogWarning(message, context); }
        public static void LogError(object message) { Debug.LogError(message); }
        public static void LogError(object message, UnityEngine.Object context) { Debug.LogError(message, context); }
        public static void LogAssertion(object message) { Debug.LogAssertion(message); }
        public static void LogAssertion(object message, UnityEngine.Object context) { Debug.LogAssertion(message, context); }
        public static void LogException(Exception exception) { Debug.LogException(exception); }
        public static void LogException(Exception exception, UnityEngine.Object context) { Debug.LogException(exception, context); }

        public static void LogFormat(string format, object[] args) { Debug.LogFormat(format, args); }
        public static void LogFormat(UnityEngine.Object context, string format, object[] args) { Debug.LogFormat(context, format, args); }
        public static void LogWarningFormat(string format, object[] args) { Debug.LogWarningFormat(format, args); }
        public static void LogWarningFormat(UnityEngine.Object context, string format, object[] args) { Debug.LogWarningFormat(context, format, args); }
        public static void LogErrorFormat(string format, object[] args) { Debug.LogErrorFormat(format, args); }
        public static void LogErrorFormat(UnityEngine.Object context, string format, object[] args) { Debug.LogErrorFormat(context, format, args); }
        public static void LogAssertionFormat(string format, object[] args) { Debug.LogAssertionFormat(format, args); }
        public static void LogAssertionFormat(UnityEngine.Object context, string format, object[] args) { Debug.LogAssertionFormat(context, format, args); }

        public static void print(object message) { Debug.Log(message); }
    }
}
