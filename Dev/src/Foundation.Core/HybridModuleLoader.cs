// @module: Foundation.Core.Modularity
// @purpose: Discovers and initializes both in-assembly Folder Slices and external app\modules\*.dll plugins implementing IFoundationModule.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Reflection, Foundation.Core.Contracts
// @exports: DiscoveredModuleRecord, HybridModuleLoader

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Foundation.Core.Contracts;

namespace Foundation.Core;

public sealed record DiscoveredModuleRecord(
    string ModuleId,
    string DisplayName,
    ModuleType ModuleType,
    string Category,
    string IconName,
    string AssemblyName,
    IFoundationModule Instance
);

/// <summary>
/// Discovers and instantiates both lightweight in-assembly Folder Slices (Features\&lt;Slice&gt;\)
/// and compiled external .dll plugin modules (app\modules\*.dll) implementing IFoundationModule (Standard 08 §8).
/// </summary>
public sealed class HybridModuleLoader
{
    private readonly Dictionary<string, DiscoveredModuleRecord> _modules = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiscoveredModuleRecord> Modules =>
        _modules.Values.OrderBy(m => m.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void RegisterModule(IFoundationModule module, string? assemblyName = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(module.ModuleId);

        var record = new DiscoveredModuleRecord(
            module.ModuleId,
            module.DisplayName,
            module.ModuleType,
            module.Category,
            module.IconName,
            assemblyName ?? module.GetType().Assembly.GetName().Name ?? "InProcess",
            module);

        _modules[module.ModuleId] = record;
    }

    public int DiscoverFromAssemblies(
        IEnumerable<Assembly> assemblies,
        Func<Type, IFoundationModule?>? factory = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        int count = 0;

        foreach (Assembly asm in assemblies)
        {
            count += RegisterAssemblyModules(asm, factory);
        }

        return count;
    }

    public int DiscoverExternalDllModules(
        string? modulesDirectory,
        string filePattern = "*.Module.*.dll",
        Func<Type, IFoundationModule?>? factory = null)
    {
        if (string.IsNullOrWhiteSpace(modulesDirectory) || !Directory.Exists(modulesDirectory))
        {
            return 0;
        }

        int count = 0;
        foreach (string dllPath in Directory.EnumerateFiles(modulesDirectory, filePattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                string fullPath = Path.GetFullPath(dllPath);
                Assembly asm = Assembly.LoadFrom(fullPath);
                count += RegisterAssemblyModules(asm, factory);
            }
            catch
            {
                // Ignore non-managed or incompatible plugin binaries during discovery
            }
        }

        return count;
    }

    public IFoundationModule? TryGetModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }
        return _modules.TryGetValue(moduleId.Trim(), out DiscoveredModuleRecord? rec) ? rec.Instance : null;
    }

    public IReadOnlyList<SchemaSettingDescriptor> GetAggregatedSettingsSchema()
    {
        var list = new List<SchemaSettingDescriptor>();
        foreach (DiscoveredModuleRecord record in Modules)
        {
            if (record.Instance.SettingsSchema is not null)
            {
                list.AddRange(record.Instance.SettingsSchema);
            }
        }
        return list;
    }

    private int RegisterAssemblyModules(Assembly assembly, Func<Type, IFoundationModule?>? factory)
    {
        int registered = 0;
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch
        {
            return 0;
        }

        foreach (Type type in types)
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IFoundationModule).IsAssignableFrom(type))
            {
                continue;
            }

            try
            {
                IFoundationModule? instance = factory?.Invoke(type);
                if (instance is null && type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    instance = Activator.CreateInstance(type) as IFoundationModule;
                }

                if (instance is not null)
                {
                    RegisterModule(instance, assembly.GetName().Name);
                    registered++;
                }
            }
            catch
            {
                // Skip modules requiring custom DI when parameterless creation is not applicable
            }
        }

        return registered;
    }
}
