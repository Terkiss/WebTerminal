using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace WebPowerShell.Infrastructure.Services;

[SupportedOSPlatform("windows")]
public class SystemMetricsService
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _ramCounter;

    public SystemMetricsService()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
        
        // Initial call to get valid data for next call
        _cpuCounter.NextValue();
    }

    public object GetMetrics()
    {
        return new
        {
            cpuUsagePercent = Math.Round(_cpuCounter.NextValue(), 2),
            availableRamMb = _ramCounter.NextValue(),
            totalRamMb = GetTotalRamMb(),
            diskUsage = GetDiskUsage()
        };
    }

    private float GetTotalRamMb()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        return (float)(gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0);
    }

    private object GetDiskUsage()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\");
        if (drive.IsReady)
        {
            return new
            {
                driveName = drive.Name,
                totalSizeMb = drive.TotalSize / 1024 / 1024,
                freeSpaceMb = drive.AvailableFreeSpace / 1024 / 1024
            };
        }
        return null!;
    }
}
