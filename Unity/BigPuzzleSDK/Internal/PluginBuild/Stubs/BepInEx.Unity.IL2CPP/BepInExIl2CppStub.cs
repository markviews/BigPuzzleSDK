// COMPILE-ONLY STUB. Never ships. See BepInExCoreStub.cs.
using BepInEx.Logging;
using System.Reflection;

[assembly: AssemblyVersion("6.0.0.0")]

namespace BepInEx.Unity.IL2CPP
{
    public abstract class BasePlugin
    {
        public ManualLogSource Log { get; }
        public abstract void Load();
    }
}
