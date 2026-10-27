using System;
using UnityEngine;

namespace BigPuzzleSDK {
    // Stand-ins for Unity's logging calls. The game's Unity can't be called from a creator's DLL the way it was
    // built (Debug.Log and friends are missing at runtime), so the exporter points those calls here when it makes
    // the plugin DLL (see PluginInjector). Creators just write Debug.Log.
    // Names and parameters match Unity's, because the exporter finds the stand-in by them. The Unity SDK has the
    // same class (with the same members) for it to look at.
    public static class CreatorLog {
        private static string Text(object message) { return message == null ? "null" : message.ToString(); }

        private static string Fill(string format, object[] args) {
            if (format == null) return "null";
            if (args == null || args.Length == 0) return format;
            try { return string.Format(format, args); } catch (Exception) { return format; }
        }

        private static void Write(LogType type, object message) {
            string line = "[Creator] " + Text(message);
            switch (type) {
                case LogType.Warning: Plugin.log.LogWarning(line); break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert: Plugin.log.LogError(line); break;
                default: Plugin.log.LogInfo(line); break;
            }
        }

        // Debug.Log...
        public static void Log(object message) { Write(LogType.Log, message); }
        public static void Log(object message, UnityEngine.Object context) { Write(LogType.Log, message); }
        public static void LogWarning(object message) { Write(LogType.Warning, message); }
        public static void LogWarning(object message, UnityEngine.Object context) { Write(LogType.Warning, message); }
        public static void LogError(object message) { Write(LogType.Error, message); }
        public static void LogError(object message, UnityEngine.Object context) { Write(LogType.Error, message); }
        public static void LogAssertion(object message) { Write(LogType.Assert, message); }
        public static void LogAssertion(object message, UnityEngine.Object context) { Write(LogType.Assert, message); }
        public static void LogException(Exception exception) { Write(LogType.Exception, exception); }
        public static void LogException(Exception exception, UnityEngine.Object context) { Write(LogType.Exception, exception); }

        // Fills in the format like string.Format; if the format is wrong it is logged as it is
        public static void LogFormat(string format, object[] args) { Write(LogType.Log, Fill(format, args)); }
        public static void LogFormat(UnityEngine.Object context, string format, object[] args) { Write(LogType.Log, Fill(format, args)); }
        public static void LogWarningFormat(string format, object[] args) { Write(LogType.Warning, Fill(format, args)); }
        public static void LogWarningFormat(UnityEngine.Object context, string format, object[] args) { Write(LogType.Warning, Fill(format, args)); }
        public static void LogErrorFormat(string format, object[] args) { Write(LogType.Error, Fill(format, args)); }
        public static void LogErrorFormat(UnityEngine.Object context, string format, object[] args) { Write(LogType.Error, Fill(format, args)); }
        public static void LogAssertionFormat(string format, object[] args) { Write(LogType.Assert, Fill(format, args)); }
        public static void LogAssertionFormat(UnityEngine.Object context, string format, object[] args) { Write(LogType.Assert, Fill(format, args)); }

        // MonoBehaviour.print
        public static void print(object message) { Write(LogType.Log, message); }
    }
}
