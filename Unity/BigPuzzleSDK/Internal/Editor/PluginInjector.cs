// BigPuzzleSDK: turns the DLL Unity compiled from the creator's scripts folder into a BepInEx plugin.
// BepInEx only loads a DLL that has a plugin class, so a copy of the DLL gets one added, equivalent to:
//
//   namespace BigPuzzleSDK.Generated {
//       [BepInPlugin("puzzle.<assembly name>", "<assembly name>", "1.0.0")]
//       [BepInDependency("markviews.BigPuzzleSDK", BepInDependency.DependencyFlags.HardDependency)]
//       public class BigPuzzleSDKGeneratedPlugin : BasePlugin {
//           public override void Load() { PuzzleSDK.RegisterAssemblyOf(this); }
//       }
//   }
//
// The types are looked up in the compile-only stub DLLs Unity built (PluginBuild/Stubs); the references
// written into the plugin say "BepInEx.Core" / "BepInEx.Unity.IL2CPP" 6.0.0.0, which BepInEx binds to its own.
//
// The copy also gets its calls to Debug.Log and friends (and print) pointed at BigPuzzleSDK.CreatorLog, because the
// game's Unity has no such methods for a DLL built the usual way to call. The scripts in the project are not touched.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace BigPuzzleSDK.GameAssets {

internal static class PluginInjector {
    public const string TypeNamespace = "BigPuzzleSDK.Generated";
    public const string TypeName = "BigPuzzleSDKGeneratedPlugin";
    private const string SdkGuid = "markviews.BigPuzzleSDK";

    private static AssemblyDefinition Read(string path) {
        // InMemory: nothing stays locked in the Library folder. The resolver lets Cecil find the stub
        // assemblies (it looks inside the enum type of the attribute arguments when writing them).
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(path));
        return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
    }

    // Written in one go from memory, so a failure never leaves a half-written DLL behind
    private static void Save(AssemblyDefinition assembly, string targetDll) {
        Directory.CreateDirectory(Path.GetDirectoryName(targetDll));
        using (var stream = new MemoryStream()) {
            assembly.Write(stream);
            File.WriteAllBytes(targetDll, stream.ToArray());
        }
    }

    // Points every call to UnityEngine.Debug's logging methods (and MonoBehaviour.print) at the CreatorLog method with
    // the same name and parameters. A call with no such stand-in is left as it is.
    private static void RedirectLogging(ModuleDefinition module, TypeDefinition creatorLog) {
        if (creatorLog == null) throw new Exception("The SDK's stub assembly has no CreatorLog class (BigPuzzleSDK did not compile?). See the Console.");
        foreach (TypeDefinition type in module.GetTypes()) {
            foreach (MethodDefinition method in type.Methods) {
                if (!method.HasBody) continue;
                foreach (Instruction instruction in method.Body.Instructions) {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                    MethodReference called = instruction.Operand as MethodReference;
                    if (called == null || called.DeclaringType == null) continue;
                    string owner = called.DeclaringType.FullName;
                    if (owner != "UnityEngine.Debug" && !(owner == "UnityEngine.MonoBehaviour" && called.Name == "print")) continue;
                    MethodDefinition stand = FindStandIn(creatorLog, called);
                    if (stand == null) continue;
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = module.ImportReference(stand);
                }
            }
        }
    }

    private static MethodDefinition FindStandIn(TypeDefinition creatorLog, MethodReference called) {
        foreach (MethodDefinition candidate in creatorLog.Methods) {
            if (candidate.Name != called.Name || !candidate.IsStatic || candidate.Parameters.Count != called.Parameters.Count) continue;
            bool same = true;
            for (int i = 0; i < candidate.Parameters.Count && same; i++)
                same = candidate.Parameters[i].ParameterType.FullName == called.Parameters[i].ParameterType.FullName;
            if (same) return candidate;
        }
        return null;
    }

    // Writes 'sourceDll' to 'targetDll' with the plugin class added. Throws with the reason on failure.
    public static void Inject(string sourceDll, string targetDll) {
        string dir = Path.GetDirectoryName(sourceDll);
        string name = Path.GetFileNameWithoutExtension(sourceDll);

        AssemblyDefinition coreStub = Read(Path.Combine(dir, "BepInEx.Core.dll"));
        AssemblyDefinition il2cppStub = Read(Path.Combine(dir, "BepInEx.Unity.IL2CPP.dll"));
        AssemblyDefinition sdkStub = Read(Path.Combine(dir, "BigPuzzleSDK.dll"));
        AssemblyDefinition target = Read(sourceDll);
        ModuleDefinition module = target.MainModule;

        RedirectLogging(module, sdkStub.MainModule.GetType("BigPuzzleSDK.CreatorLog"));

        if (module.GetType(TypeNamespace, TypeName) != null) { Save(target, targetDll); return; }

        TypeDefinition basePlugin = il2cppStub.MainModule.GetType("BepInEx.Unity.IL2CPP.BasePlugin");
        TypeDefinition pluginAttr = coreStub.MainModule.GetType("BepInEx.BepInPlugin");
        TypeDefinition dependencyAttr = coreStub.MainModule.GetType("BepInEx.BepInDependency");
        TypeDefinition sdk = sdkStub.MainModule.GetType("BigPuzzleSDK.PuzzleSDK");
        MethodDefinition register = sdk?.Methods.FirstOrDefault(m => m.Name == "RegisterAssemblyOf");
        if (basePlugin == null || pluginAttr == null || dependencyAttr == null || register == null)
            throw new Exception("The SDK's stub assemblies are out of date or did not compile (BepInEx.Core / BepInEx.Unity.IL2CPP / BigPuzzleSDK). See the Console.");

        MethodDefinition baseCtor = basePlugin.Methods.First(m => m.IsConstructor && !m.IsStatic);
        MethodDefinition pluginCtor = pluginAttr.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 3);
        MethodDefinition dependencyCtor = dependencyAttr.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 2 && m.Parameters[1].ParameterType.Name == "DependencyFlags");

        var type = new TypeDefinition(TypeNamespace, TypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            module.ImportReference(basePlugin));

        // public BigPuzzleSDKGeneratedPlugin() : base() { }
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ILProcessor il = ctor.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(baseCtor));
        il.Emit(OpCodes.Ret);
        type.Methods.Add(ctor);

        // public override void Load() { PuzzleSDK.RegisterAssemblyOf(this); }
        var load = new MethodDefinition("Load", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual, module.TypeSystem.Void);
        il = load.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(register));
        il.Emit(OpCodes.Ret);
        type.Methods.Add(load);

        var plugin = new CustomAttribute(module.ImportReference(pluginCtor));
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "puzzle." + name.ToLowerInvariant()));
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, name));
        plugin.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, "1.0.0"));
        type.CustomAttributes.Add(plugin);

        var dependency = new CustomAttribute(module.ImportReference(dependencyCtor));
        dependency.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, SdkGuid));
        dependency.ConstructorArguments.Add(new CustomAttributeArgument(module.ImportReference(dependencyCtor.Parameters[1].ParameterType), 1)); // HardDependency
        type.CustomAttributes.Add(dependency);

        module.Types.Add(type);
        Save(target, targetDll);
    }
}

}
