$ErrorActionPreference='Stop'
$failures=[Collections.Generic.List[string]]::new()
function Read-Hardware([string]$class,[string[]]$properties,[int]$limit=32,[string]$namespace='root/cimv2') {
    try { @(Get-CimInstance -Namespace $namespace -ClassName $class -Property $properties -OperationTimeoutSec 3 -ErrorAction Stop | Select-Object -First $limit -Property $properties) }
    catch { $failures.Add($class); @() }
}
$data=[ordered]@{
    system=@(Read-Hardware 'Win32_ComputerSystem' @('Manufacturer','Model','SystemType','TotalPhysicalMemory') 1)
    os=@(Read-Hardware 'Win32_OperatingSystem' @('Caption','Version','BuildNumber','OSArchitecture') 1)
    board=@(Read-Hardware 'Win32_BaseBoard' @('Manufacturer','Product','Version') 4)
    bios=@(Read-Hardware 'Win32_BIOS' @('Manufacturer','SMBIOSBIOSVersion','ReleaseDate','SMBIOSMajorVersion','SMBIOSMinorVersion') 1)
    cpu=@(Read-Hardware 'Win32_Processor' @('Name','Manufacturer','SocketDesignation','NumberOfCores','NumberOfLogicalProcessors','MaxClockSpeed','CurrentClockSpeed','L2CacheSize','L3CacheSize') 8)
    memory=@(Read-Hardware 'Win32_PhysicalMemory' @('Manufacturer','PartNumber','DeviceLocator','BankLabel','Capacity','SMBIOSMemoryType','FormFactor','Speed','ConfiguredClockSpeed','ConfiguredVoltage') 32)
    memoryArrays=@(Read-Hardware 'Win32_PhysicalMemoryArray' @('MemoryDevices','MaxCapacityEx') 4)
    graphics=@(Read-Hardware 'Win32_VideoController' @('Name','AdapterCompatibility','VideoProcessor','DriverVersion','CurrentHorizontalResolution','CurrentVerticalResolution','CurrentRefreshRate','VideoModeDescription') 16)
    disks=@(Read-Hardware 'Win32_DiskDrive' @('Model','Manufacturer','InterfaceType','MediaType','Size','FirmwareRevision') 32)
    physicalDisks=@(Read-Hardware 'MSFT_PhysicalDisk' @('FriendlyName','Manufacturer','Size','BusType','MediaType','FirmwareVersion','PhysicalSectorSize','LogicalSectorSize') 32 'root/Microsoft/Windows/Storage')
    network=@(Read-Hardware 'Win32_NetworkAdapter' @('Name','Manufacturer','AdapterType','Speed','PhysicalAdapter') 64 | Where-Object PhysicalAdapter)
    displays=@(Read-Hardware 'Win32_DesktopMonitor' @('Name','MonitorManufacturer','MonitorType','ScreenWidth','ScreenHeight') 16)
}
foreach($gpu in $data.graphics) {
    $vram=$null
    try {
        $classPath='HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
        foreach($index in 0..31) {
            try {
                $values=Get-ItemProperty -LiteralPath ($classPath+'\'+$index.ToString('0000')) -ErrorAction Stop
                if($values.DriverDesc -eq $gpu.Name -and $values.'HardwareInformation.qwMemorySize' -gt 0){$vram=[double]$values.'HardwareInformation.qwMemorySize';break}
            } catch {}
        }
    } catch {}
    $gpu | Add-Member -NotePropertyName DedicatedMemoryBytes -NotePropertyValue $vram
}
foreach($entry in $data.bios){if($entry.ReleaseDate){$entry.ReleaseDate=$entry.ReleaseDate.ToString('yyyy-MM-dd')}}
$data['unavailable']=$failures.ToArray()
$data | ConvertTo-Json -Depth 6 -Compress
