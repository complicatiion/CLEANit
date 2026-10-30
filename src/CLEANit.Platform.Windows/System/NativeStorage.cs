using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CLEANit.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace CLEANit.Platform.Windows;

/// <summary>
/// Read-only drive queries through documented storage IOCTLs (no PowerShell, no driver):
/// device descriptor, size, seek penalty, temperature, NVMe health log (IOCTL_STORAGE_QUERY_PROPERTY with
/// StorageDeviceProtocolSpecificProperty) and ATA SMART data (SMART_RCV_DRIVE_DATA). Needs administrator rights.
/// </summary>
internal static class NativeStorage
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint SmartRcvDriveData = 0x0007C088;

    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int StorageDeviceTemperatureProperty = 52;

    private const int ProtocolTypeNvme = 3;
    private const int NvmeDataTypeLogPage = 2;
    private const int NvmeLogPageHealthInfo = 2;
    private const int ProtocolSpecificDataSize = 40;
    private const int ProtocolDataDescriptorSize = 48;
    private const int NvmeHealthLogSize = 512;

    private const int BusUsb = 7;
    private const int BusNvme = 17;

    /// <summary>All physical drives \\.\PhysicalDrive0 … 31 that can be opened.</summary>
    public static IReadOnlyList<DriveHealthInfo> ReadAll(CancellationToken ct)
    {
        var volumes = MapVolumes();
        var drives = new List<DriveHealthInfo>();
        for (var number = 0; number < 32 && !ct.IsCancellationRequested; number++)
        {
            using var handle = Open($@"\\.\PhysicalDrive{number}", GenericRead | GenericWrite) ?? Open($@"\\.\PhysicalDrive{number}", 0);
            if (handle is null)
            {
                continue;
            }

            try
            {
                drives.Add(Read(handle, number, volumes.GetValueOrDefault(number) ?? []));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                drives.Add(new DriveHealthInfo { Number = number, Name = $"PhysicalDrive{number}" });
            }
        }

        return drives;
    }

    private static DriveHealthInfo Read(SafeFileHandle handle, int number, IReadOnlyList<string> volumes)
    {
        var info = new DriveHealthInfo { Number = number, Name = $"PhysicalDrive{number}", Volumes = volumes };
        var descriptor = QueryProperty(handle, StorageDeviceProperty, 1024) is { } d ? DriveDataParser.ParseDeviceDescriptor(d) : null;
        if (descriptor is { } desc)
        {
            info = info with
            {
                Name = desc.Model.Length > 0 ? desc.Model : info.Name,
                Firmware = desc.Firmware,
                Serial = desc.Serial,
                BusType = DriveDataParser.BusName(desc.BusType),
                Removable = desc.Removable
            };
        }

        var bus = descriptor?.BusType ?? 0;
        if (Ioctl(handle, IoctlDiskGetLengthInfo, [], 8) is { Length: >= 8 } length)
        {
            info = info with { Size = BinaryPrimitives.ReadInt64LittleEndian(length) };
        }

        if (bus == BusNvme)
        {
            info = info with { MediaType = "SSD" };
        }
        else if (QueryProperty(handle, StorageDeviceSeekPenaltyProperty, 12) is { Length: >= 9 } seek)
        {
            info = info with { MediaType = seek[8] != 0 ? "HDD" : "SSD" };
        }

        if (QueryProperty(handle, StorageDeviceTemperatureProperty, 24 + 16 * 8) is { } temperature)
        {
            var (current, warning, critical) = DriveDataParser.ParseTemperatureDescriptor(temperature);
            info = info with { Temperature = current, TemperatureWarning = warning, TemperatureCritical = critical };
        }

        if (bus == BusNvme)
        {
            if (ReadNvmeHealth(handle) is { } log)
            {
                info = DriveAssessor.ApplyNvme(info, log);
            }
        }
        else if (bus != BusUsb && ReadSmart(handle, number) is { Count: > 0 } attributes)
        {
            info = DriveAssessor.ApplySmart(info, attributes);
        }

        return info;
    }

    private static NvmeHealthLog? ReadNvmeHealth(SafeFileHandle handle)
    {
        var buffer = new byte[8 + ProtocolSpecificDataSize + NvmeHealthLogSize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), StorageDeviceProtocolSpecificProperty);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 0); // PropertyStandardQuery
        var data = buffer.AsSpan(8);
        BinaryPrimitives.WriteInt32LittleEndian(data[0..], ProtocolTypeNvme);
        BinaryPrimitives.WriteInt32LittleEndian(data[4..], NvmeDataTypeLogPage);
        BinaryPrimitives.WriteInt32LittleEndian(data[8..], NvmeLogPageHealthInfo);
        BinaryPrimitives.WriteInt32LittleEndian(data[12..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(data[16..], ProtocolSpecificDataSize);
        BinaryPrimitives.WriteInt32LittleEndian(data[20..], NvmeHealthLogSize);

        if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, buffer.Length, buffer, buffer.Length, out var returned, IntPtr.Zero) ||
            returned < ProtocolDataDescriptorSize)
        {
            return null;
        }

        // STORAGE_PROTOCOL_DATA_DESCRIPTOR: Version, Size, then STORAGE_PROTOCOL_SPECIFIC_DATA (offset relative to it).
        var version = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0));
        var size = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4));
        if (version != ProtocolDataDescriptorSize || size != ProtocolDataDescriptorSize)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8 + 16));
        var count = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(8 + 20));
        if (offset < ProtocolSpecificDataSize || count < NvmeHealthLogSize || 8 + offset + NvmeHealthLogSize > buffer.Length)
        {
            return null;
        }

        return DriveDataParser.ParseNvmeHealth(buffer.AsSpan(8 + offset, NvmeHealthLogSize));
    }

    private static IReadOnlyList<SmartAttribute>? ReadSmart(SafeFileHandle handle, int number)
    {
        var values = SmartCommand(handle, number, 0xD0);
        if (values is null)
        {
            return null;
        }

        var thresholds = SmartCommand(handle, number, 0xD1) ?? [];
        return DriveDataParser.ParseSmartAttributes(values, thresholds);
    }

    /// <summary>SMART READ DATA (0xD0) or READ THRESHOLDS (0xD1) through SMART_RCV_DRIVE_DATA.</summary>
    private static byte[]? SmartCommand(SafeFileHandle handle, int number, byte feature)
    {
        // SENDCMDINPARAMS (packed): cBufferSize, IDEREGS (8), bDriveNumber, reserved (3 + 16).
        var input = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(0), 512);
        input[4] = feature;      // bFeaturesReg
        input[5] = 1;            // bSectorCountReg
        input[6] = 1;            // bSectorNumberReg
        input[7] = 0x4F;         // bCylLowReg  (SMART signature)
        input[8] = 0xC2;         // bCylHighReg (SMART signature)
        input[9] = 0xA0;         // bDriveHeadReg
        input[10] = 0xB0;        // bCommandReg = SMART
        input[12] = (byte)number;

        // SENDCMDOUTPARAMS: cBufferSize (4), DRIVERSTATUS (12), then 512 bytes of data.
        var output = Ioctl(handle, SmartRcvDriveData, input, 16 + 512);
        if (output is not { Length: >= 16 + 512 } || output[4] != 0)
        {
            return null;
        }

        var data = output.AsSpan(16, 512).ToArray();
        return data.Any(b => b != 0) ? data : null;
    }

    private static byte[]? QueryProperty(SafeFileHandle handle, int propertyId, int outputSize)
    {
        var query = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(query.AsSpan(0), propertyId);
        return Ioctl(handle, IoctlStorageQueryProperty, query, outputSize);
    }

    /// <summary>Drive letters per physical disk number (volumes spanning several disks are listed on each).</summary>
    private static Dictionary<int, IReadOnlyList<string>> MapVolumes()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            var letter = drive.Name.TrimEnd('\\');
            using var handle = Open($@"\\.\{letter}", 0);
            if (handle is null || Ioctl(handle, IoctlVolumeGetVolumeDiskExtents, [], 8 + 24 * 8) is not { Length: >= 8 } extents)
            {
                continue;
            }

            var count = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(extents), (extents.Length - 8) / 24);
            for (var i = 0; i < count; i++)
            {
                var disk = BinaryPrimitives.ReadInt32LittleEndian(extents.AsSpan(8 + i * 24));
                if (!map.TryGetValue(disk, out var letters))
                {
                    map[disk] = letters = [];
                }

                if (!letters.Contains(letter))
                {
                    letters.Add(letter);
                }
            }
        }

        return map.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.Order().ToList());
    }

    private static byte[]? Ioctl(SafeFileHandle handle, uint code, byte[] input, int outputSize)
    {
        var output = new byte[outputSize];
        return DeviceIoControl(handle, code, input, input.Length, output, output.Length, out var returned, IntPtr.Zero) && returned > 0
            ? output.AsSpan(0, Math.Min(returned, output.Length)).ToArray()
            : null;
    }

    private static SafeFileHandle? Open(string path, uint access)
    {
        var handle = CreateFile(path, access, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, byte[] inBuffer, int inBufferSize,
        byte[] outBuffer, int outBufferSize, out int bytesReturned, IntPtr overlapped);
}
