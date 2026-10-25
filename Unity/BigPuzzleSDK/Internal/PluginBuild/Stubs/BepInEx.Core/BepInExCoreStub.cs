// COMPILE-ONLY STUB. Never ships. It exists so Unity can compile code that uses BepInEx.
// The assembly name and the namespaces/signatures must match the real BepInEx.Core exactly;
// at runtime BepInEx binds the creator's plugin to the real one.
using System;
using System.Reflection;

// Without this Unity builds the stub as 0.0.0.0 and BepInEx warns "targets a wrong version of BepInEx".
[assembly: AssemblyVersion("6.0.0.0")]

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string GUID, string Name, string Version) { }
    }
}

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute
    {
        [Flags]
        public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }

        public BepInDependency(string guid, DependencyFlags flags) { }
        public BepInDependency(string guid, string version) { }
    }
}

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public void LogInfo(object data) { }
        public void LogWarning(object data) { }
        public void LogError(object data) { }
        public void LogMessage(object data) { }
        public void LogDebug(object data) { }
    }
}
