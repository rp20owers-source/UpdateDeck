namespace UpdateDeck.Core;

public sealed record HardwareField(string Label,string Value);
public sealed record HardwareSection(string Name,string Summary,IReadOnlyList<HardwareField> Fields);

public static class HardwareFormatting
{
    public static string Bytes(double bytes)=>bytes<=0||!double.IsFinite(bytes)?"Not reported":bytes>=1024*1024*1024?$"{bytes/(1024*1024*1024):0.##} GiB":$"{bytes/(1024*1024):0.##} MiB";
    public static string MemoryType(int type)=>type switch{20=>"DDR",21=>"DDR2",24=>"DDR3",26=>"DDR4",27=>"LPDDR",28=>"LPDDR2",29=>"LPDDR3",30=>"LPDDR4",34=>"DDR5",35=>"LPDDR5",_=>"Not reported / SMBIOS type "+type};
    public static string MemoryForm(int type)=>type switch{8=>"DIMM",12=>"SO-DIMM",_=>"Windows form factor "+type};
    public static string BusType(int type)=>type switch{1=>"SCSI",3=>"ATA",7=>"USB",8=>"RAID",11=>"SATA",15=>"Virtual disk",16=>"Storage Spaces",17=>"NVMe",_=>"Not reported / bus "+type};
}
