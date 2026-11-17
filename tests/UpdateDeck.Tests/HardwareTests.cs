using UpdateDeck.Core;
using UpdateDeck.Windows;
using Xunit;

namespace UpdateDeck.Tests;

public class HardwareTests
{
    [Fact]
    public void ParsesReportedBrandsClocksAndMemoryWithoutInventingUnknownSpecs()
    {
        var result=HardwareInventory.Parse("""
        {"board":[{"Manufacturer":"Board brand","Product":"Board model"}],"cpu":[{"Name":"CPU model","NumberOfCores":8,"MaxClockSpeed":3600}],"memory":[{"Manufacturer":"RAM brand","Capacity":17179869184,"SMBIOSMemoryType":34,"ConfiguredClockSpeed":6000,"FormFactor":8}],"graphics":[{"Name":"GPU model","DriverVersion":"1.2"}],"physicalDisks":[{"FriendlyName":"SSD model","BusType":17,"MediaType":4,"Size":1099511627776}],"unavailable":[]}
        """);
        Assert.Equal("Board model",Assert.Single(result,x=>x.Name=="Motherboard").Summary);
        Assert.Contains(Assert.Single(result,x=>x.Name=="CPU").Fields,x=>x.Label=="Reported maximum clock"&&x.Value=="3600 MHz");
        Assert.Contains(Assert.Single(result,x=>x.Name.StartsWith("RAM ·")).Fields,x=>x.Value=="DDR5");
        Assert.Contains(Assert.Single(result,x=>x.Name=="Storage").Fields,x=>x.Value=="NVMe");
        Assert.Contains(Assert.Single(result,x=>x.Name=="GPU").Fields,x=>x.Label=="Dedicated memory (registry)"&&x.Value=="Not reported");
    }
    [Theory]
    [InlineData(26,"DDR4")]
    [InlineData(34,"DDR5")]
    [InlineData(35,"LPDDR5")]
    public void DecodesReportedMemoryTypes(int type,string expected)=>Assert.Equal(expected,HardwareFormatting.MemoryType(type));
    [Fact]
    public void MissingSectionsRemainEmptyAndUnavailableIsExplicit()
    {
        Assert.Empty(HardwareInventory.Parse("{}"));
        Assert.Equal("Unavailable sources",Assert.Single(HardwareInventory.Parse("{\"unavailable\":[\"Win32_BaseBoard\"]}")).Name);
        Assert.Equal("Not reported",HardwareFormatting.Bytes(0));
    }
}
