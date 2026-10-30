using System.Buffers.Binary;
using System.Text;
using CLEANit.Core.Diagnostics;
using CLEANit.Platform.Windows;
using Xunit;

namespace CLEANit.Core.Tests;

/// <summary>v0.0.4: NVMe health log, SMART attributes, storage descriptors and the drive assessment.</summary>
public class DriveDataTests
{
    [Fact]
    public void ParsesNvmeHealthLog()
    {
        var log = new byte[512];
        log[0] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), 273 + 41);
        log[3] = 100;
        log[4] = 10;
        log[5] = 7;
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(32), 2_000_000);  // data units read
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48), 4_000_000);  // data units written
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(112), 1234);      // power cycles
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(128), 5678);      // power-on hours
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(144), 12);        // unsafe shutdowns
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(160), 0);         // media errors
        BinaryPrimitives.WriteUInt32LittleEndian(log.AsSpan(192), 3);         // warning temperature minutes

        var health = DriveDataParser.ParseNvmeHealth(log);
        Assert.NotNull(health);
        Assert.Equal(41, health.TemperatureCelsius);
        Assert.Equal(100, health.AvailableSpare);
        Assert.Equal(7, health.PercentageUsed);
        Assert.Equal(4_000_000L * 512_000, health.BytesWritten);
        Assert.Equal(2_000_000L * 512_000, health.BytesRead);
        Assert.Equal(1234, health.PowerCycles);
        Assert.Equal(5678, health.PowerOnHours);
        Assert.Equal(12, health.UnsafeShutdowns);
        Assert.Equal(3, health.WarningTemperatureMinutes);
        Assert.False(health.SpareBelowThreshold);

        var info = DriveAssessor.Assess(DriveAssessor.ApplyNvme(new DriveHealthInfo { Number = 0 }, health));
        Assert.Equal(DriveAssessment.Good, info.Assessment);
        Assert.Equal(7, info.Wear);
        Assert.Equal("nvme", info.Source);
    }

    [Fact]
    public void NvmeCriticalWarningAndWearAreBad()
    {
        var log = new byte[512];
        log[0] = 0x01; // spare below threshold
        log[3] = 5;
        log[4] = 10;
        log[5] = 101;
        var info = DriveAssessor.Assess(DriveAssessor.ApplyNvme(new DriveHealthInfo(), DriveDataParser.ParseNvmeHealth(log)!));
        Assert.Equal(DriveAssessment.Bad, info.Assessment);
        Assert.Contains("spare", info.Findings);
        Assert.Contains("wear", info.Findings);
    }

    [Fact]
    public void ShortNvmeBufferIsRejected() => Assert.Null(DriveDataParser.ParseNvmeHealth(new byte[100]));

    [Fact]
    public void ParsesSmartAttributesWithThresholds()
    {
        var data = new byte[512];
        var limits = new byte[512];
        Attribute(data, 0, 0x05, 0x33, 100, 100, 3);          // reallocated sectors = 3
        Attribute(data, 1, 0x09, 0x32, 95, 95, 12345);        // power-on hours
        Attribute(data, 2, 0xC2, 0x22, 64, 50, 0x0028_0014_0024); // temperature 36 °C, min/max in the upper bytes
        Attribute(data, 3, 0x0C, 0x32, 99, 99, 777);          // power cycles
        Attribute(data, 4, 0xC5, 0x32, 100, 100, 0);          // pending sectors
        Threshold(limits, 0, 0x05, 10);
        Threshold(limits, 1, 0x09, 0);

        var attributes = DriveDataParser.ParseSmartAttributes(data, limits);
        Assert.Equal(5, attributes.Count);
        var reallocated = Assert.Single(attributes, a => a.Id == 0x05);
        Assert.Equal("Reallocated Sectors Count", reallocated.Name);
        Assert.Equal(10, reallocated.Threshold);
        Assert.Equal(3, reallocated.Raw);
        Assert.True(reallocated.PreFailure);
        Assert.False(reallocated.IsFailing);

        var info = DriveAssessor.Assess(DriveAssessor.ApplySmart(new DriveHealthInfo(), attributes));
        Assert.Equal(36, info.Temperature);
        Assert.Equal(12345, info.PowerOnHours);
        Assert.Equal(777, info.PowerCycles);
        Assert.Equal(3, info.ReallocatedSectors);
        Assert.Equal(DriveAssessment.Caution, info.Assessment);
        Assert.Contains("reallocated", info.Findings);
        Assert.Equal("smart", info.Source);
    }

    [Fact]
    public void SmartValueAtThresholdIsBad()
    {
        var data = new byte[512];
        var limits = new byte[512];
        Attribute(data, 0, 0x01, 0x0B, 40, 40, 0);
        Threshold(limits, 0, 0x01, 51);
        var attributes = DriveDataParser.ParseSmartAttributes(data, limits);
        Assert.True(Assert.Single(attributes).IsFailing);
        var info = DriveAssessor.Assess(DriveAssessor.ApplySmart(new DriveHealthInfo(), attributes));
        Assert.Equal(DriveAssessment.Bad, info.Assessment);
        Assert.Contains("threshold:01", info.Findings);
    }

    [Fact]
    public void NoDataIsUnknownAndWindowsWarningIsCaution()
    {
        Assert.Equal(DriveAssessment.Unknown, DriveAssessor.Assess(new DriveHealthInfo()).Assessment);
        Assert.Equal(DriveAssessment.Good, DriveAssessor.Assess(new DriveHealthInfo { WindowsHealth = "Healthy" }).Assessment);
        Assert.Equal(DriveAssessment.Caution, DriveAssessor.Assess(new DriveHealthInfo { WindowsHealth = "Warning" }).Assessment);
        Assert.Equal(DriveAssessment.Bad, DriveAssessor.Assess(new DriveHealthInfo { WindowsHealth = "Unhealthy" }).Assessment);
    }

    [Fact]
    public void ParsesStorageDeviceDescriptor()
    {
        var buffer = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 256);
        buffer[10] = 0; // not removable
        Write(buffer, 12, 64, "Samsung");
        Write(buffer, 16, 80, "SSD 980 PRO 1TB");
        Write(buffer, 20, 120, "5B2QGXA7");
        Write(buffer, 24, 140, "S5GXNX0T123456  .");
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 17);

        var parsed = DriveDataParser.ParseDeviceDescriptor(buffer);
        Assert.NotNull(parsed);
        Assert.Equal("Samsung SSD 980 PRO 1TB", parsed.Value.Model);
        Assert.Equal("5B2QGXA7", parsed.Value.Firmware);
        Assert.Equal("S5GXNX0T123456", parsed.Value.Serial);
        Assert.Equal("NVMe", DriveDataParser.BusName(parsed.Value.BusType));
        Assert.False(parsed.Value.Removable);

        // Generic "ATA" vendor is dropped
        var ata = new byte[256];
        Write(ata, 12, 64, "ATA");
        Write(ata, 16, 80, "WDC WD40EFRX-68N32N0");
        BinaryPrimitives.WriteUInt32LittleEndian(ata.AsSpan(28), 11);
        Assert.Equal("WDC WD40EFRX-68N32N0", DriveDataParser.ParseDeviceDescriptor(ata)!.Value.Model);
    }

    [Fact]
    public void ParsesTemperatureDescriptor()
    {
        var buffer = new byte[24 + 16];
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(8), 84);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(10), 75);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(26), 38);
        var (current, warning, critical) = DriveDataParser.ParseTemperatureDescriptor(buffer);
        Assert.Equal(38, current);
        Assert.Equal(75, warning);
        Assert.Equal(84, critical);
    }

    [Fact]
    public void ParsesWindowsDiskJson()
    {
        const string json = """
            [{"Number":0,"Name":"Samsung SSD","MediaType":"SSD","BusType":"NVMe","Health":"Healthy","Status":"OK","Size":1000204886016,
              "Rpm":0,"Temperature":40,"TemperatureMax":70,"Wear":2,"PowerOnHours":100,"ReadErrors":0,"WriteErrors":0,
              "ReadLatencyMax":12,"WriteLatencyMax":30,"StartStop":null,"LoadUnload":null},
             {"Number":1,"Name":"USB Disk","Health":"Warning"}]
            """;
        var disks = DriveHealth.Parse(json);
        Assert.Equal(2, disks.Count);
        Assert.Equal("Healthy", disks[0].Health);
        Assert.Equal(70, disks[0].TemperatureMax);
        Assert.Equal(30, disks[0].WriteLatencyMaxMs);
        Assert.Equal("Warning", disks[1].Health);
        Assert.Empty(DriveHealth.Parse("garbage"));
    }

    private static void Attribute(byte[] data, int index, byte id, ushort flags, byte current, byte worst, long raw)
    {
        var at = 2 + index * 12;
        data[at] = id;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at + 1), flags);
        data[at + 3] = current;
        data[at + 4] = worst;
        for (var b = 0; b < 6; b++)
        {
            data[at + 5 + b] = (byte)(raw >> (8 * b));
        }
    }

    private static void Threshold(byte[] data, int index, byte id, byte value)
    {
        data[2 + index * 12] = id;
        data[2 + index * 12 + 1] = value;
    }

    private static void Write(byte[] buffer, int offsetField, int offset, string text)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offsetField), (uint)offset);
        Encoding.ASCII.GetBytes(text).CopyTo(buffer, offset);
    }
}
