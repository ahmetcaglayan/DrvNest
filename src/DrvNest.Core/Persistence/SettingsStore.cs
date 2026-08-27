using DrvNest.Core.Diagnostics;
using DrvNest.Core.Models;

namespace DrvNest.Core.Persistence;

/// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private AppSettings? _current;

    /// <summary>Raised after a successful save so live components can react.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>The active settings, loaded on first access.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                if (_current is not null) return _current;

                _current = JsonStore.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
                _current.Normalize();
                SeedPortableRepository(_current);
                return _current;
            }
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();

        lock (_gate) _current = settings;

        if (JsonStore.Write(AppPaths.SettingsFile, settings))
            Log.Info("Settings saved.");

        Changed?.Invoke(settings);
    }

    /// <summary>Restores defaults, keeping nothing.</summary>
    public AppSettings Reset()
    {
        var fresh = new AppSettings();
        SeedPortableRepository(fresh);
        Save(fresh);
        return fresh;
    }

    /// <summary>
    /// A "Drivers" folder next to DrvNest.exe is registered automatically. That is the
    /// USB rescue workflow: copy the exe and a Drivers folder onto a stick, run it on
    /// the freshly formatted machine, and the offline packages are already there.
    /// </summary>
    private static void SeedPortableRepository(AppSettings settings)
    {
        var portable = AppPaths.PortableRepositoryDirectory;

        if (!Directory.Exists(portable)) return;
        if (settings.LocalRepositoryPaths.Contains(portable, StringComparer.OrdinalIgnoreCase)) return;

        settings.LocalRepositoryPaths.Insert(0, portable);
        Log.Info($"Registered portable driver folder: {portable}");
    }
}
