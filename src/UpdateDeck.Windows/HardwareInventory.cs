using System.Text.Json;
using UpdateDeck.Core;

namespace UpdateDeck.Windows;

public static class HardwareInventory
{
    public static string Script
    {
        get{using var stream=typeof(HardwareInventory).Assembly.GetManifestResourceStream("UpdateDeck.Windows.Resources.HardwareInventory.ps1")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    }
    public static IReadOnlyList<HardwareSection> Parse(string json)
    {
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});var root=doc.RootElement;var sections=new List<HardwareSection>();
        IEnumerable<JsonElement> Items(string key)=>root.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.Object).ToArray():[];
        string Text(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.String or JsonValueKind.Number?v.ToString().Trim():"";
        double Number(JsonElement x,string key)=>double.TryParse(Text(x,key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:0;
        void Add(string name,string summary,params HardwareField[] fields)=>sections.Add(new(name,summary.Length>0?summary:"Windows did not report a model",fields.Select(f=>f with{Value=f.Value.Length==0||f.Value=="0"?"Not reported":f.Value}).ToArray()));
        HardwareField F(string label,JsonElement x,string key)=>new(label,Text(x,key));
        string Clock(JsonElement x,string key)=>Number(x,key)>0?$"{Number(x,key):0} MHz":"Not reported";
        foreach(var x in Items("system"))Add("System",Text(x,"Model"),F("Manufacturer",x,"Manufacturer"),F("Model",x,"Model"),F("System type",x,"SystemType"),new("Installed memory",HardwareFormatting.Bytes(Number(x,"TotalPhysicalMemory"))));
        foreach(var x in Items("os"))Add("Windows",Text(x,"Caption"),F("Edition",x,"Caption"),F("Version",x,"Version"),F("Build",x,"BuildNumber"),F("Architecture",x,"OSArchitecture"));
        foreach(var x in Items("board"))Add("Motherboard",Text(x,"Product"),F("Brand",x,"Manufacturer"),F("Model",x,"Product"),F("Revision",x,"Version"));
        foreach(var x in Items("bios"))Add("BIOS / UEFI",Text(x,"SMBIOSBIOSVersion"),F("Vendor",x,"Manufacturer"),F("Version",x,"SMBIOSBIOSVersion"),F("Release date",x,"ReleaseDate"),new("SMBIOS",Text(x,"SMBIOSMajorVersion")+"."+Text(x,"SMBIOSMinorVersion")));
        foreach(var x in Items("cpu"))Add("CPU",Text(x,"Name"),F("Brand",x,"Manufacturer"),F("Model",x,"Name"),F("Socket",x,"SocketDesignation"),F("Cores",x,"NumberOfCores"),F("Logical processors",x,"NumberOfLogicalProcessors"),new("Reported maximum clock",Clock(x,"MaxClockSpeed")),new("Reported current clock",Clock(x,"CurrentClockSpeed")),new("L2 cache",HardwareFormatting.Bytes(Number(x,"L2CacheSize")*1024)),new("L3 cache",HardwareFormatting.Bytes(Number(x,"L3CacheSize")*1024)));
        foreach(var x in Items("memoryArrays"))Add("RAM slots",$"{Items("memory").Count()} populated modules",F("Slots reported by firmware",x,"MemoryDevices"),new("Maximum supported capacity (firmware)",HardwareFormatting.Bytes(Number(x,"MaxCapacityEx")*1024)));
        foreach(var x in Items("memory"))Add("RAM · "+Text(x,"DeviceLocator"),Text(x,"PartNumber"),F("Brand",x,"Manufacturer"),F("Part / model",x,"PartNumber"),F("Slot",x,"DeviceLocator"),F("Bank",x,"BankLabel"),new("Capacity",HardwareFormatting.Bytes(Number(x,"Capacity"))),new("Type",HardwareFormatting.MemoryType((int)Number(x,"SMBIOSMemoryType"))),new("Form factor",HardwareFormatting.MemoryForm((int)Number(x,"FormFactor"))),new("Reported speed (Windows)",Clock(x,"Speed")),new("Configured speed (Windows)",Clock(x,"ConfiguredClockSpeed")),new("Configured voltage",Number(x,"ConfiguredVoltage")>0?$"{Number(x,"ConfiguredVoltage")/1000:0.###} V":"Not reported"));
        foreach(var x in Items("graphics"))Add("GPU",Text(x,"Name"),F("Brand",x,"AdapterCompatibility"),F("Model",x,"Name"),F("Processor",x,"VideoProcessor"),new("Dedicated memory (registry)",HardwareFormatting.Bytes(Number(x,"DedicatedMemoryBytes"))),F("Driver",x,"DriverVersion"),F("Current video mode",x,"VideoModeDescription"),new("Current refresh rate",Number(x,"CurrentRefreshRate")>1?$"{Number(x,"CurrentRefreshRate"):0} Hz":"Not reported"));
        foreach(var x in Items("physicalDisks"))Add("Storage",Text(x,"FriendlyName"),F("Model",x,"FriendlyName"),F("Brand",x,"Manufacturer"),new("Capacity",HardwareFormatting.Bytes(Number(x,"Size"))),new("Connection",HardwareFormatting.BusType((int)Number(x,"BusType"))),new("Media",(int)Number(x,"MediaType") switch{3=>"HDD",4=>"SSD",5=>"Storage class memory",_=>"Not reported"}),F("Firmware",x,"FirmwareVersion"),new("Physical / logical sector size",$"{Text(x,"PhysicalSectorSize")} / {Text(x,"LogicalSectorSize")} bytes"));
        if(!Items("physicalDisks").Any())foreach(var x in Items("disks"))Add("Storage",Text(x,"Model"),F("Brand",x,"Manufacturer"),F("Model",x,"Model"),new("Capacity",HardwareFormatting.Bytes(Number(x,"Size"))),F("Interface",x,"InterfaceType"),F("Media",x,"MediaType"),F("Firmware",x,"FirmwareRevision"));
        foreach(var x in Items("network"))Add("Network adapter",Text(x,"Name"),F("Brand",x,"Manufacturer"),F("Model",x,"Name"),F("Type",x,"AdapterType"),new("Reported link speed",Number(x,"Speed")>0?$"{Number(x,"Speed")/1000000:0.##} Mbps":"Not reported / disconnected"));
        foreach(var x in Items("displays"))Add("Display",Text(x,"Name"),F("Manufacturer",x,"MonitorManufacturer"),F("Name",x,"Name"),F("Type",x,"MonitorType"));
        if(root.TryGetProperty("unavailable",out var missing)&&missing.ValueKind==JsonValueKind.Array&&missing.GetArrayLength()>0)Add("Unavailable sources","Some hardware information could not be read",new HardwareField("Windows inventory classes",string.Join(", ",missing.EnumerateArray().Select(x=>x.GetString()))));
        return sections;
    }
}
