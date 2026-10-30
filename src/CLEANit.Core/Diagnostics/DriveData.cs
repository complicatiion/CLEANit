using System.Buffers.Binary;
using System.Text;

namespace CLEANit.Core.Diagnostics;

/// <summary>Overall state of a drive, derived from SMART / NVMe health data (like the usual disk tools).</summary>
public enum DriveAssessment
{
    Unknown,
    Good,
    Caution,
    Bad
}

/// <summary>One ATA SMART attribute (current/worst normalized values, vendor threshold, 48-bit raw value).</summary>
public sealed record SmartAttribute(byte Id, string Name, int Current, int Worst, int Threshold, long Raw, bool PreFailure)
{
    /// <summary>Normalized value at or below the vendor threshold – the drive itself predicts a failure.</summary>
    public bool IsFailing => Threshold > 0 && Current > 0 && Current <= Threshold;

    public string IdHex => Id.ToString("X2");
}

/// <summary>NVMe SMART / Health Information log page (log identifier 02h, 512 bytes).</summary>
public sealed record NvmeHealthLog(
    byte CriticalWarning,
    int TemperatureCelsius,
    int AvailableSpare,
    int AvailableSpareThreshold,
    int PercentageUsed,
    long DataUnitsRead,
    long DataUnitsWritten,
    long HostReadCommands,
    long HostWriteCommands,
    long ControllerBusyMinutes,
    long PowerCycles,
    long PowerOnHours,
    long UnsafeShutdowns,
    long MediaErrors,
    long ErrorLogEntries,
    long WarningTemperatureMinutes,
    long CriticalTemperatureMinutes)
{
    /// <summary>One NVMe data unit = 1000 × 512 bytes.</summary>
    public const long DataUnitBytes = 512_000;

    public long BytesRead => Saturate(DataUnitsRead, DataUnitBytes);

    public long BytesWritten => Saturate(DataUnitsWritten, DataUnitBytes);

    public bool SpareBelowThreshold => (CriticalWarning & 0x01) != 0 || (AvailableSpareThreshold > 0 && AvailableSpare < AvailableSpareThreshold);

    public bool TemperatureWarning => (CriticalWarning & 0x02) != 0;

    public bool ReliabilityDegraded => (CriticalWarning & 0x04) != 0;

    public bool ReadOnly => (CriticalWarning & 0x08) != 0;

    public bool BackupFailed => (CriticalWarning & 0x10) != 0;

    private static long Saturate(long units, long factor) => units > long.MaxValue / factor ? long.MaxValue : units * factor;
}

/// <summary>Everything CLEANit knows about one physical drive. Read only; values are null when the drive does not report them.</summary>
public sealed record DriveHealthInfo
{
    public int Number { get; init; }

    public string Name { get; init; } = "";

    public string Serial { get; init; } = "";

    public string Firmware { get; init; } = "";

    /// <summary>NVMe, SATA, USB, SAS, RAID … (STORAGE_BUS_TYPE).</summary>
    public string BusType { get; init; } = "";

    /// <summary>SSD, HDD or empty.</summary>
    public string MediaType { get; init; } = "";

    public long Size { get; init; }

    public bool Removable { get; init; }

    public IReadOnlyList<string> Volumes { get; init; } = [];

    /// <summary>HealthStatus reported by Windows (Get-PhysicalDisk): Healthy, Warning, Unhealthy.</summary>
    public string WindowsHealth { get; init; } = "";

    public string OperationalStatus { get; init; } = "";

    public int? Temperature { get; init; }

    public int? TemperatureMax { get; init; }

    public int? TemperatureWarning { get; init; }

    public int? TemperatureCritical { get; init; }

    /// <summary>Wear in percent of the rated life (NVMe "percentage used", Windows reliability counter).</summary>
    public int? Wear { get; init; }

    public int? AvailableSpare { get; init; }

    public long? PowerOnHours { get; init; }

    public long? PowerCycles { get; init; }

    public long? UnsafeShutdowns { get; init; }

    public long? BytesRead { get; init; }

    public long? BytesWritten { get; init; }

    public long? MediaErrors { get; init; }

    public long? ReadErrors { get; init; }

    public long? WriteErrors { get; init; }

    public long? ReallocatedSectors { get; init; }

    public long? PendingSectors { get; init; }

    public long? UncorrectableSectors { get; init; }

    public int? Rpm { get; init; }

    public long? ReadLatencyMaxMs { get; init; }

    public long? WriteLatencyMaxMs { get; init; }

    public long? StartStopCycles { get; init; }

    public long? LoadUnloadCycles { get; init; }

    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = [];

    public NvmeHealthLog? Nvme { get; init; }

    public DriveAssessment Assessment { get; init; }

    /// <summary>Short machine-readable reason for Caution/Bad (e.g. "reallocated", "spare", "threshold:05").</summary>
    public IReadOnlyList<string> Findings { get; init; } = [];

    /// <summary>Where the detailed values came from: "nvme", "smart", "windows" or empty.</summary>
    public string Source { get; init; } = "";
}

/// <summary>Byte-level parsers for the Windows storage IOCTL results (testable without Windows).</summary>
public static class DriveDataParser
{
    /// <summary>Parses the 512-byte NVMe health log. Returns null if the buffer is too small.</summary>
    public static NvmeHealthLog? ParseNvmeHealth(ReadOnlySpan<byte> log)
    {
        if (log.Length < 200)
        {
            return null;
        }

        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log[1..]);
        return new NvmeHealthLog(
            CriticalWarning: log[0],
            TemperatureCelsius: kelvin == 0 ? 0 : kelvin - 273,
            AvailableSpare: log[3],
            AvailableSpareThreshold: log[4],
            PercentageUsed: log[5],
            DataUnitsRead: Uint128(log[32..]),
            DataUnitsWritten: Uint128(log[48..]),
            HostReadCommands: Uint128(log[64..]),
            HostWriteCommands: Uint128(log[80..]),
            ControllerBusyMinutes: Uint128(log[96..]),
            PowerCycles: Uint128(log[112..]),
            PowerOnHours: Uint128(log[128..]),
            UnsafeShutdowns: Uint128(log[144..]),
            MediaErrors: Uint128(log[160..]),
            ErrorLogEntries: Uint128(log[176..]),
            WarningTemperatureMinutes: BinaryPrimitives.ReadUInt32LittleEndian(log[192..]),
            CriticalTemperatureMinutes: BinaryPrimitives.ReadUInt32LittleEndian(log[196..]));
    }

    /// <summary>
    /// Parses ATA SMART READ DATA (512 bytes, 30 entries of 12 bytes from offset 2) and the optional
    /// READ THRESHOLDS sector with the same layout.
    /// </summary>
    public static IReadOnlyList<SmartAttribute> ParseSmartAttributes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds)
    {
        var limits = new Dictionary<byte, int>();
        for (var i = 0; i < 30 && thresholds.Length >= 2 + (i + 1) * 12; i++)
        {
            var entry = thresholds.Slice(2 + i * 12, 12);
            if (entry[0] != 0)
            {
                limits[entry[0]] = entry[1];
            }
        }

        var list = new List<SmartAttribute>();
        for (var i = 0; i < 30 && data.Length >= 2 + (i + 1) * 12; i++)
        {
            var entry = data.Slice(2 + i * 12, 12);
            var id = entry[0];
            if (id == 0)
            {
                continue;
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(entry[1..]);
            long raw = 0;
            for (var b = 5; b >= 0; b--)
            {
                raw = (raw << 8) | entry[5 + b];
            }

            list.Add(new SmartAttribute(id, SmartName(id), entry[3], entry[4], limits.GetValueOrDefault(id), raw, (flags & 0x01) != 0));
        }

        return list;
    }

    /// <summary>STORAGE_DEVICE_DESCRIPTOR: vendor + product, revision, serial number, bus type, removable flag.</summary>
    public static (string Model, string Firmware, string Serial, int BusType, bool Removable)? ParseDeviceDescriptor(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 36)
        {
            return null;
        }

        var removable = buffer[10] != 0;
        var vendor = AsciiAt(buffer, BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]));
        var product = AsciiAt(buffer, BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]));
        var revision = AsciiAt(buffer, BinaryPrimitives.ReadUInt32LittleEndian(buffer[20..]));
        var serial = AsciiAt(buffer, BinaryPrimitives.ReadUInt32LittleEndian(buffer[24..]));
        var bus = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[28..]);

        // Many drives report an empty or generic vendor ("ATA", "NVMe") – keep only meaningful parts.
        var model = vendor.Length == 0 || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) ||
                    vendor is "ATA" or "NVMe" or "NVME" or "SCSI"
            ? product
            : vendor + " " + product;
        return (model.Trim(), revision.Trim(), serial.Trim().TrimEnd('.').Trim(), bus, removable);
    }

    /// <summary>STORAGE_TEMPERATURE_DATA_DESCRIPTOR: current temperature of the first sensor, warning and critical limits (°C).</summary>
    public static (int? Current, int? Warning, int? Critical) ParseTemperatureDescriptor(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 24)
        {
            return (null, null, null);
        }

        var critical = BinaryPrimitives.ReadInt16LittleEndian(buffer[8..]);
        var warning = BinaryPrimitives.ReadInt16LittleEndian(buffer[10..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]);
        int? current = null;
        if (count > 0 && buffer.Length >= 24 + 16)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(buffer[26..]);
            current = value is > -60 and < 200 ? value : null;
        }

        return (current, Plausible(warning), Plausible(critical));
    }

    public static string BusName(int busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => ""
    };

    /// <summary>Common SMART attribute names (vendor specific meanings vary; the raw value is always shown).</summary>
    public static string SmartName(byte id) => id switch
    {
        0x01 => "Read Error Rate",
        0x02 => "Throughput Performance",
        0x03 => "Spin-Up Time",
        0x04 => "Start/Stop Count",
        0x05 => "Reallocated Sectors Count",
        0x07 => "Seek Error Rate",
        0x08 => "Seek Time Performance",
        0x09 => "Power-On Hours",
        0x0A => "Spin Retry Count",
        0x0B => "Recalibration Retries",
        0x0C => "Power Cycle Count",
        0x0D => "Soft Read Error Rate",
        0xAA => "Available Reserved Space",
        0xAB => "Program Fail Count",
        0xAC => "Erase Fail Count",
        0xAD => "Wear Leveling Count",
        0xAE => "Unexpected Power Loss Count",
        0xAF => "Power Loss Protection Failure",
        0xB1 => "Wear Range Delta",
        0xB3 => "Used Reserved Block Count Total",
        0xB4 => "Unused Reserved Block Count Total",
        0xB5 => "Program Fail Count Total",
        0xB6 => "Erase Fail Count",
        0xB7 => "SATA Downshift Error Count",
        0xB8 => "End-to-End Error",
        0xBB => "Reported Uncorrectable Errors",
        0xBC => "Command Timeout",
        0xBD => "High Fly Writes",
        0xBE => "Airflow Temperature",
        0xBF => "G-Sense Error Rate",
        0xC0 => "Power-off Retract Count",
        0xC1 => "Load/Unload Cycle Count",
        0xC2 => "Temperature",
        0xC3 => "Hardware ECC Recovered",
        0xC4 => "Reallocation Event Count",
        0xC5 => "Current Pending Sector Count",
        0xC6 => "Uncorrectable Sector Count",
        0xC7 => "UltraDMA CRC Error Count",
        0xC8 => "Write Error Rate",
        0xCA => "Data Address Mark Errors",
        0xDC => "Disk Shift",
        0xDF => "Load/Unload Retry Count",
        0xE1 => "Load/Unload Cycle Count",
        0xE7 => "SSD Life Left",
        0xE8 => "Available Reserved Space",
        0xE9 => "Media Wearout Indicator",
        0xF0 => "Head Flying Hours",
        0xF1 => "Total Host Writes",
        0xF2 => "Total Host Reads",
        0xF9 => "NAND Writes",
        _ => "Vendor specific"
    };

    private static long Uint128(ReadOnlySpan<byte> value)
    {
        // Upper 64 bits are zero on every real drive; saturate instead of overflowing.
        if (BinaryPrimitives.ReadUInt64LittleEndian(value[8..]) != 0)
        {
            return long.MaxValue;
        }

        var low = BinaryPrimitives.ReadUInt64LittleEndian(value);
        return low > long.MaxValue ? long.MaxValue : (long)low;
    }

    private static string AsciiAt(ReadOnlySpan<byte> buffer, uint offset)
    {
        if (offset == 0 || offset >= buffer.Length)
        {
            return "";
        }

        var span = buffer[(int)offset..];
        var end = span.IndexOf((byte)0);
        if (end >= 0)
        {
            span = span[..end];
        }

        var text = new StringBuilder(span.Length);
        foreach (var b in span)
        {
            text.Append(b is >= 0x20 and < 0x7F ? (char)b : ' ');
        }

        return string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int? Plausible(short value) => value is > 0 and < 200 ? value : null;
}

/// <summary>Turns SMART / NVMe values into Good / Caution / Bad, close to the rules common disk tools use.</summary>
public static class DriveAssessor
{
    public static DriveHealthInfo Assess(DriveHealthInfo info)
    {
        var findings = new List<string>();
        var bad = false;
        var caution = false;

        if (info.Nvme is { } nvme)
        {
            if (nvme.SpareBelowThreshold)
            {
                bad = true;
                findings.Add("spare");
            }

            if (nvme.ReliabilityDegraded || nvme.ReadOnly)
            {
                bad = true;
                findings.Add(nvme.ReadOnly ? "readonly" : "reliability");
            }

            if (nvme.TemperatureWarning)
            {
                caution = true;
                findings.Add("temperature");
            }

            if (nvme.PercentageUsed >= 100)
            {
                bad = true;
                findings.Add("wear");
            }
            else if (nvme.PercentageUsed >= 90)
            {
                caution = true;
                findings.Add("wear");
            }

            if (nvme.MediaErrors > 0)
            {
                caution = true;
                findings.Add("media");
            }
        }

        foreach (var attribute in info.Attributes.Where(a => a.IsFailing))
        {
            bad = true;
            findings.Add("threshold:" + attribute.IdHex);
        }

        if (info.ReallocatedSectors > 0)
        {
            caution = true;
            findings.Add("reallocated");
        }

        if (info.PendingSectors > 0)
        {
            caution = true;
            findings.Add("pending");
        }

        if (info.UncorrectableSectors > 0)
        {
            caution = true;
            findings.Add("uncorrectable");
        }

        if (info.WindowsHealth.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
        {
            bad = true;
            findings.Add("windows");
        }
        else if (info.WindowsHealth.Equals("Warning", StringComparison.OrdinalIgnoreCase))
        {
            caution = true;
            findings.Add("windows");
        }

        var hasData = info.Nvme is not null || info.Attributes.Count > 0 || info.WindowsHealth.Length > 0;
        var assessment = bad ? DriveAssessment.Bad : caution ? DriveAssessment.Caution : hasData ? DriveAssessment.Good : DriveAssessment.Unknown;
        return info with { Assessment = assessment, Findings = findings.Distinct().ToList() };
    }

    /// <summary>Copies the well-known SMART attributes into the summary fields (temperature, hours, sectors …).</summary>
    public static DriveHealthInfo ApplySmart(DriveHealthInfo info, IReadOnlyList<SmartAttribute> attributes)
    {
        long? Raw(byte id) => attributes.FirstOrDefault(a => a.Id == id)?.Raw;
        int? Temperature()
        {
            foreach (var id in new byte[] { 0xC2, 0xBE })
            {
                if (Raw(id) is { } raw && (raw & 0xFF) is > 0 and < 120)
                {
                    return (int)(raw & 0xFF);
                }
            }

            return null;
        }

        // Power-on hours: lower 32 bits (some vendors put extra data in the upper bytes).
        var hours = Raw(0x09) is { } h ? h & 0xFFFFFFFF : (long?)null;
        var life = attributes.FirstOrDefault(a => a.Id is 0xE7 or 0xE9 or 0xAD && a.Current is > 0 and <= 100);
        return info with
        {
            Attributes = attributes,
            Temperature = info.Temperature ?? Temperature(),
            PowerOnHours = hours ?? info.PowerOnHours,
            PowerCycles = Raw(0x0C) ?? info.PowerCycles,
            UnsafeShutdowns = Raw(0xC0) ?? Raw(0xAE) ?? info.UnsafeShutdowns,
            ReallocatedSectors = Raw(0x05),
            PendingSectors = Raw(0xC5),
            UncorrectableSectors = Raw(0xC6),
            StartStopCycles = Raw(0x04) ?? info.StartStopCycles,
            LoadUnloadCycles = Raw(0xC1) ?? Raw(0xE1) ?? info.LoadUnloadCycles,
            Wear = info.Wear ?? (life is null ? null : 100 - life.Current),
            Source = "smart"
        };
    }

    /// <summary>Copies the NVMe health log into the summary fields.</summary>
    public static DriveHealthInfo ApplyNvme(DriveHealthInfo info, NvmeHealthLog log) => info with
    {
        Nvme = log,
        Temperature = log.TemperatureCelsius is > 0 and < 150 ? log.TemperatureCelsius : info.Temperature,
        Wear = log.PercentageUsed,
        AvailableSpare = log.AvailableSpare,
        PowerOnHours = log.PowerOnHours,
        PowerCycles = log.PowerCycles,
        UnsafeShutdowns = log.UnsafeShutdowns,
        BytesRead = log.BytesRead,
        BytesWritten = log.BytesWritten,
        MediaErrors = log.MediaErrors,
        Source = "nvme"
    };
}
