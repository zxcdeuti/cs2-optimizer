using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CS2Optimizer.Services;

public record Stats(int? Fps, double CpuLoad, double? CpuTemp, double? GpuLoad, double? GpuTemp);

/// <summary>
/// Samples CPU/GPU load and temperatures once per call. CPU temperature comes from the ACPI thermal zone
/// (no driver needed), GPU from NVML, which ships with the NVIDIA driver.
/// </summary>
public sealed class Telemetry : IDisposable
{
    long _idle, _kernel, _user;
    readonly List<PerformanceCounter> _thermal = [];
    readonly IntPtr _gpu;
    readonly bool _nvml;

    public FpsMonitor? Fps { get; }

    public Telemetry(bool withFps)
    {
        GetSystemTimes(out _idle, out _kernel, out _user);

        try
        {
            foreach (var zone in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
                _thermal.Add(new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", zone, true));
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or Win32Exception) { }

        try { _nvml = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out _gpu) == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }

        if (withFps) Fps = FpsMonitor.TryStart();
    }

    public Stats Sample()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        long busy = kernel - _kernel + (user - _user); // kernel time includes idle time
        double cpu = busy > 0 ? (1 - (idle - _idle) / (double)busy) * 100 : 0;
        (_idle, _kernel, _user) = (idle, kernel, user);

        double? cpuTemp = null;
        foreach (var zone in _thermal)
        {
            try
            {
                double c = zone.NextValue() / 10 - 273.15; // tenths of a kelvin
                if (c is > 5 and < 130) cpuTemp = Math.Max(cpuTemp ?? 0, c);
            }
            catch (InvalidOperationException) { }
        }

        double? gpuLoad = null, gpuTemp = null;
        if (_nvml)
        {
            if (nvmlDeviceGetUtilizationRates(_gpu, out var u) == 0) gpuLoad = u.Gpu;
            if (nvmlDeviceGetTemperature(_gpu, 0, out var t) == 0) gpuTemp = t;
        }
        // AMD / Intel: Windows' own GPU counters give the load; temperature needs vendor drivers, so it stays empty.
        gpuLoad ??= WindowsGpuLoad();

        return new Stats(Fps?.Sample(), Math.Clamp(cpu, 0, 100), cpuTemp, gpuLoad, gpuTemp);
    }

    readonly Dictionary<string, PerformanceCounter> _engines = [];

    /// <summary>Busiest adapter's 3D engine load from the "GPU Engine" counters (any vendor, Windows 10+).</summary>
    double? WindowsGpuLoad()
    {
        try
        {
            var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                .Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
            foreach (var gone in _engines.Keys.Where(k => !names.Contains(k)).ToList())
            {
                _engines[gone].Dispose();
                _engines.Remove(gone);
            }
            foreach (var name in names.Where(n => !_engines.ContainsKey(n)))
                _engines[name] = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);

            // Instances are per process and per adapter ("..._luid_0x..._phys_0_eng_0_engtype_3D"); sum per adapter.
            var perAdapter = new Dictionary<string, double>();
            foreach (var (name, counter) in _engines)
            {
                var luid = System.Text.RegularExpressions.Regex.Match(name, "luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+").Value;
                perAdapter[luid] = perAdapter.GetValueOrDefault(luid) + counter.NextValue();
            }
            return perAdapter.Count == 0 ? null : Math.Min(100, perAdapter.Values.Max());
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or Win32Exception or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Fps?.Dispose();
        foreach (var engine in _engines.Values) engine.Dispose();
        foreach (var zone in _thermal) zone.Dispose();
        if (_nvml) nvmlShutdown();
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    struct NvmlUtilization { public uint Gpu, Memory; }

    [DllImport("nvml.dll")] static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] static extern int nvmlShutdown();
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);
    [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);
}
