using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Xml;

namespace specify_client.data;

public static partial class Cache
{

    public static async System.Threading.Tasks.Task MakeHardwareData()
    {
        try
        {
            DebugLog.Region region = DebugLog.Region.Hardware;
            await DebugLog.StartRegion(region);

            var TemperatureTask = GetTemps();

            Cpu = Utils.GetWmi("Win32_Processor",
                "CurrentClockSpeed, Manufacturer, Name, SocketDesignation, NumberOfEnabledCore, ThreadCount").First();
            Gpu = Utils.GetWmi("Win32_VideoController",
                "Description, AdapterRam, CurrentHorizontalResolution, CurrentVerticalResolution, "
                + "CurrentRefreshRate, CurrentBitsPerPixel");
            Motherboard = Utils.GetWmi("Win32_BaseBoard", "Manufacturer, Product, SerialNumber").FirstOrDefault();
            AudioDevices = Utils.GetWmi("Win32_SoundDevice", "Name, Manufacturer, Status, DeviceID");
            Drivers = Utils.GetWmi("Win32_PnpSignedDriver", "FriendlyName,Manufacturer,DeviceID,DeviceName,DriverVersion");
            Devices = Utils.GetWmi("Win32_PnpEntity", "DeviceID,Name,Description,Status");
            BiosInfo = Utils.GetWmi("Win32_bios");
            await DebugLog.LogEventAsync("Hardware WMI Information Retrieved.", region);

            MonitorInfo = GetMonitorInfo();
            await DebugLog.LogEventAsync("Monitor Information Retrieved.", region);

            Ram = GetSMBiosMemoryInfo();
            await DebugLog.LogEventAsync("SMBios Information Retrieved.", region);

            Disks = GetDiskDriveData();
            await DebugLog.LogEventAsync("Drive Data Retrieved.", region);


            Batteries = GetBatteryData();
            await DebugLog.LogEventAsync("Battery Data Retrieved.", region);

            Temperatures = await TemperatureTask;
            await DebugLog.LogEventAsync("Temperature Data Retrieved.", region);

            await DebugLog.EndRegion(DebugLog.Region.Hardware);
        }
        catch (Exception ex)
        {
            await DebugLog.LogEventAsync("UNEXPECTED FATAL EXCEPTION", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            await DebugLog.LogEventAsync($"{ex}", DebugLog.Region.Hardware);
            Environment.Exit(-1);
        }
    }

    // RAM
    private static List<RamStick> GetSMBiosMemoryInfo()
    {
        DateTime start = DateTime.Now;
        DebugLog.LogEvent("GetSMBiosMemoryInfo() started.", DebugLog.Region.Hardware);

        var SMBiosObj = Utils.GetWmiObj("MSSMBios_RawSMBiosTables", "*", "root\\WMI");

        // If no data is received, stop before it excepts. Add error message?
        if (SMBiosObj == null)
        {
            DebugLog.LogEvent($"SMBios information not retrieved.", DebugLog.Region.Hardware, DebugLog.EventType.WARNING);
            Issues.Add("Hardware Data: Could not get SMBios info for RAM.");
            return null;
        }

        // Store raw SMBios Data
        byte[] SMBios = null;
        foreach (ManagementObject obj in SMBiosObj)
        {
            SMBios = (byte[])obj["SMBiosData"];
        }

        var offset = 0;
        var type = SMBios[offset];

        var SMBiosMemoryInfo = new List<RamStick>();

        while (offset + 4 < SMBios.Length && type != 127)
        {
            type = SMBios[offset];
            var dataLength = SMBios[offset + 1];

            // If the data extends the bounds of the SMBios Data array, stop.
            if (offset + dataLength > SMBios.Length)
            {
                break;
            }

            var data = new byte[dataLength];
            Array.Copy(SMBios, offset, data, 0, dataLength);
            offset += dataLength;

            var smbStringsList = new List<string>();

            if (offset < SMBios.Length && SMBios[offset] == 0)
                offset++;

            // Iterate the byte array to build a list of SMBios structures.
            while (offset < SMBios.Length && SMBios[offset] != 0)
            {
                var smbDataString = new System.Text.StringBuilder();
                while (offset < SMBios.Length && SMBios[offset] != 0)
                {
                    smbDataString.Append((char)SMBios[offset]);
                    offset++;
                }
                offset++;
                smbStringsList.Add(smbDataString.ToString());
            }
            offset++;

            // This is the only type we care about; Type 17. If the type is anything else, it simply loops again.
            if (type != 0x11) continue;

            var stick = new RamStick();
            // These if statements confirm the data received is valid data.
            if (0x10 < data.Length && data[0x10] > 0 && data[0x10] <= smbStringsList.Count)
            {
                stick.DeviceLocation = smbStringsList[data[0x10] - 1].Trim();
            }

            if (0x11 < data.Length && data[0x11] > 0 && data[0x11] <= smbStringsList.Count)
            {
                stick.BankLocator = smbStringsList[data[0x11] - 1].Trim();
            }

            if (0x17 < data.Length && data[0x17] > 0 && data[0x17] <= smbStringsList.Count)
            {
                stick.Manufacturer = smbStringsList[data[0x17] - 1].Trim();
            }

            if (0x18 < data.Length && data[0x18] > 0 && data[0x18] <= smbStringsList.Count)
            {
                stick.SerialNumber = smbStringsList[data[0x18] - 1].Trim();
            }

            if (0x1A < data.Length && data[0x1A] > 0 && data[0x1A] <= smbStringsList.Count)
            {
                stick.PartNumber = smbStringsList[data[0x1A] - 1].Trim();
            }

            if (0x15 + 1 < data.Length)
            {
                stick.ConfiguredSpeed = (data[0x15 + 1] << 8) | data[0x15];
            }

            if (0xC + 1 < data.Length)
            {
                stick.Capacity = (data[0xC + 1] << 8) | data[0xC];
            }
            SMBiosMemoryInfo.Add(stick);
        }
        DebugLog.LogEvent($"GetSMBiosMemoryInfo() completed - Total Runtime: {(DateTime.Now - start).TotalMilliseconds}", DebugLog.Region.Hardware);
        return SMBiosMemoryInfo;
    }

    //MONITORS
    /*private static string MonitorFriendlyName(Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName)
    {
        /*Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName = new Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME();
        deviceName.header.size = (uint)Marshal.SizeOf(typeof(Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME));
        deviceName.header.adapterId = adapterId;
        deviceName.header.id = targetId;
        deviceName.header.type = Interop.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        int error = Interop.DisplayConfigGetDeviceInfo(ref deviceName);
        if (error != Interop.ERROR_SUCCESS)
        {
            DebugLog.LogEvent($"Interop Failure in MonitorFriendlyName() {error}", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            Issues.Add($"Interop failure during monitor data collection {error}");
        }*/
        /*return deviceName.monitorFriendlyDeviceName;
    }*/
    private static Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME GetDisplayDevice(Interop.LUID adapterId, uint targetId)
    {
        Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName = new Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME();
        deviceName.header.size = (uint)Marshal.SizeOf(typeof(Interop.DISPLAYCONFIG_TARGET_DEVICE_NAME));
        deviceName.header.adapterId = adapterId;
        deviceName.header.id = targetId;
        deviceName.header.type = Interop.DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        int error = Interop.DisplayConfigGetDeviceInfo(ref deviceName);
        if (error != Interop.ERROR_SUCCESS)
        {
            DebugLog.LogEvent($"Interop Failure in MonitorFriendlyName() {error}", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            Issues.Add($"Interop failure during monitor data collection {error}");
        }
        return deviceName;
    }
    private static List<Monitor> GetMonitorInfo()
    {
        DateTime start = DateTime.Now;
        DebugLog.LogEvent("GetMonitorInfo() started", DebugLog.Region.Hardware);
        List<Monitor> monitors = new();
        uint PathCount, ModeCount;
        int error = Interop.GetDisplayConfigBufferSizes(Interop.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            out PathCount, out ModeCount);
        if (error != Interop.ERROR_SUCCESS)
        {
            DebugLog.LogEvent($"Interop Failure in MonitorFriendlyName() {error}", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            Issues.Add($"Interop failure during monitor data collection {error}");
        }
        Interop.DISPLAYCONFIG_PATH_INFO[] DisplayPaths = new Interop.DISPLAYCONFIG_PATH_INFO[PathCount];
        Interop.DISPLAYCONFIG_MODE_INFO[] DisplayModes = new Interop.DISPLAYCONFIG_MODE_INFO[ModeCount];
        error = Interop.QueryDisplayConfig(Interop.QUERY_DEVICE_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            ref PathCount, DisplayPaths, ref ModeCount, DisplayModes, IntPtr.Zero);
        if (error != Interop.ERROR_SUCCESS)
        {
            DebugLog.LogEvent($"Interop Failure in GetMonitorInfo() {error}", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            Issues.Add($"Interop failure during monitor data collection {error}");
        }

        for (int i = 0; i < ModeCount; i++)
        {
            if (DisplayModes[i].infoType == Interop.DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
            {
                // unique display adapter UID, the LUID struct contains a low part and a high part, these are already combined in the registry so we do so here for ease of use - arc
                Int64 luid = (long)DisplayModes[i].adapterId.LowPart + (long)DisplayModes[i].adapterId.HighPart;
                RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\DirectX\\");
                if (key != null)
                {
                    // iterate over the DX registry entries and look for entries that match the DX luid, if they match, update the relevant monitor struct
                    foreach (var k in key.GetSubKeyNames())
                    {

                        RegistryKey subKey = Registry.LocalMachine.OpenSubKey($"SOFTWARE\\Microsoft\\DirectX\\{k}");
                        if (subKey != null)
                        {
                            try
                            {
                                Int64? rluid = (Int64?)subKey.GetValue("AdapterLuid");
                                // we also ensure the key isn't empty -arc
                                // move the null check to wrap this if statement if it excepts when comparing luid and rluid -arc
                                if (luid == rluid)
                                {
                                    string adapterName = (string)subKey.GetValue("Description");
                                    Int64 dedicatedMemory = (Int64)subKey.GetValue("DedicatedVideoMemory");

                                    Monitor monitor = new();

                                    monitor.Name = adapterName;
                                    var monitorInfo = GetDisplayDevice(DisplayModes[i].adapterId, DisplayModes[i].id);
                                    monitor.MonitorModel = monitorInfo.monitorFriendlyDeviceName;

                                    var cableType = monitorInfo.outputTechnology.ToString();
                                    // snip "DISPLAYCONFIG_OUTPUT_TECHNOLOGY_" from the resulting string leaving just "HDMI" or "DISPLAYPORT_EXTERNAL", etc.
                                    monitor.ConnectionType = cableType.Substring(32);

                                    // this value is given in bytes, so we convert to kilobytes, then convert those kilobytes to megabytes - arc
                                    var memory = dedicatedMemory / 1024 / 1024;
                                    monitor.DedicatedMemory = $"{memory} MB";

                                    string mode = "";
                                    // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmdt/ns-d3dkmdt-_d3dkmdt_video_signal_info -arc
                                    var targetMode = DisplayModes[i].modeInfo.targetMode.targetVideoSignalInfo;

                                    // ex: 1920 x 1080 @ 59.551 Hz
                                    // Active size specifies the active width (cx) and height (cy) of the video signal -arc
                                    mode += $"{targetMode.activeSize.cx} x {targetMode.activeSize.cy} @ " +
                                        $"{Math.Round(targetMode.vSyncFreq.Numerator / (double)targetMode.vSyncFreq.Denominator, 3)} Hz";

                                    monitor.CurrentMode = mode;
                                    monitors.Add(monitor);
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                DebugLog.LogEvent("Registry Read Error in GetMonitorInfo()", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
                                DebugLog.LogEvent($"{e}", DebugLog.Region.Hardware);
                                Issues.Add("Registry read error during monitor data collection.");
                            }
                        }
                    }
                }
            }
        }
        DebugLog.LogEvent($"GetMonitorInfo() completed - Total Runtime: {(DateTime.Now - start).TotalMilliseconds}", DebugLog.Region.Hardware);
        return monitors;
    }
    private static List<Monitor> GetMonitorInfoDXDiag()
    {
        var monitorInfo = new List<Monitor>();
        //String path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        var cmd = new Process
        {
            StartInfo =
            {
                FileName = "cmd",
                WorkingDirectory = path,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                Arguments = "/Q /C dxdiag /x dxinfo.xml"
            }
        };

        if (File.Exists(Path.Combine(path, "dxinfo.xml")))
        {
            File.Delete(Path.Combine(path, "dxinfo.xml"));
        }

        cmd.Start();

        var timer = Stopwatch.StartNew();
        var timeout = new TimeSpan().Add(TimeSpan.FromSeconds(60));

        while (timer.Elapsed < timeout)
        {
            if (!File.Exists(Path.Combine(path, "dxinfo.xml")) ||
                Process.GetProcessesByName("dxdiag").Length != 0) continue;
            var doc = new XmlDocument();
            doc.Load(Path.Combine(path, "dxinfo.xml"));
            var monitor = JObject.Parse(JsonConvert.SerializeXmlNode(doc))["DxDiag"]["DisplayDevices"]
                .Children().Children().ToList();
            var videoId = 0;

            // very inefficient while loop right here, but as long as it works, thats what matters -K97i
            while (true)
            {
                try
                {
                    foreach (var displayDevice in monitor.Where(e => e.HasValues))
                    {
                        monitorInfo.Add(new Monitor
                        {
                            Name = (string)displayDevice[videoId]["CardName"],
                            ChipType = (string)displayDevice[videoId]["ChipType"],
                            DedicatedMemory = (string)displayDevice[videoId]["DedicatedMemory"],
                            MonitorModel = (string)displayDevice[videoId]["MonitorModel"],
                            CurrentMode = (string)displayDevice[videoId]["CurrentMode"]
                        });
                        videoId++;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }
                catch (ArgumentException)
                {
                    foreach (var displayDevice in monitor.Where(e => e.HasValues))
                    {
                        monitorInfo.Add(new Monitor
                        {
                            Name = (string)displayDevice["CardName"],
                            ChipType = (string)displayDevice["ChipType"],
                            DedicatedMemory = (string)displayDevice["DedicatedMemory"],
                            MonitorModel = (string)displayDevice["MonitorModel"],
                            CurrentMode = (string)displayDevice["CurrentMode"]
                        });
                        break;
                    }
                    break;
                }
            }
            break;
        }

        if (timer.Elapsed > timeout)
            Issues.Add("Monitor report was not generated before the timeout!");

        timer.Stop();
        cmd.Close();

        File.Delete(Path.Combine(path, "dxinfo.xml"));

        return monitorInfo;
    }


    // STORAGE
    private static List<DiskDrive> GetDiskDriveData()
    {
        DateTime start = DateTime.Now;
        DebugLog.LogEvent("GetDiskDriveData() started", DebugLog.Region.Hardware);
        List<DiskDrive> drives = new List<DiskDrive>();

        var driveWmiInfo = Utils.GetWmiObj("Win32_DiskDrive");

        // This assumes the WMI info reports disks in order by drive number. I'm not certain this is a safe assumption.
        var diskNumber = 0;
        foreach (var driveWmi in driveWmiInfo)
        {
            DiskDrive drive = new();
            try
            {
                drive.DeviceName = ((string)driveWmi["Model"]).Trim();
            }
            catch (NullReferenceException)
            {
                drive.DeviceName = null;
                Issues.Add($"Could not retrieve device name of drive @ index {diskNumber}");
            }
            try
            {
                drive.SerialNumber = ((string)driveWmi["SerialNumber"]).Trim();
            }
            catch (NullReferenceException)
            {
                drive.SerialNumber = null;
                Issues.Add($"Could not retrieve serial number of drive @ index {diskNumber}");
            }

            drive.DiskNumber = (UInt32)driveWmi["Index"];

            try
            {
                drive.DiskCapacity = (ulong)driveWmi["Size"];
            }
            catch (NullReferenceException)
            {
                drive.DiskCapacity = null;
                Issues.Add($"Could not retrieve capacity of drive @ index {diskNumber}");
            }
            try
            {
                drive.InstanceId = (string)driveWmi["PNPDeviceID"];
            }
            catch (NullReferenceException)
            {
                drive.InstanceId = null;
                Issues.Add($"Could not retrieve Instance ID of drive @ index {diskNumber}");
            }

            drive.Partitions = new List<Partition>();

            diskNumber++;
            drives.Add(drive);
        }

        var partitionWmiInfo = Utils.GetWmiObj("Win32_DiskPartition");
        foreach (var partitionWmi in partitionWmiInfo)
        {
            var partition = new Partition()
            {
                PartitionCapacity = (UInt64)partitionWmi["Size"],
                Caption = (string)partitionWmi["Caption"]
            };
            var diskIndex = (UInt32)partitionWmi["DiskIndex"];

            foreach (var disk in drives)
            {
                if (disk.DiskNumber == diskIndex)
                {
                    disk.Partitions.Add(partition);
                    break;
                }
            }
        }
        try
        {
            var queryCollection = Utils.GetWmiObj("MSStorageDriver_FailurePredictData", "*", "\\\\.\\root\\wmi");
            foreach (var m in queryCollection)
            {

                // The following lines up to the attribute list creationlink smart data to its corresponding drive.
                // It makes the assumption that the PNPDeviceID in Wmi32_DiskDrive has a matching identification code to MSStorageDriver_FailurePredictData's InstanceID, and that these identification codes are unique.
                // This is not a safe assumption, testing will be required.
                var instanceId = (string)m["InstanceName"];
                instanceId = instanceId.Substring(0, instanceId.Length - 2);
                var splitID = instanceId.Split('\\');
                instanceId = splitID[splitID.Count() - 1];

                var driveIndex = -1;
                for (var i = 0; i < drives.Count; i++)
                {
                    var drive = drives[i];
                    if (!drive.InstanceId.ToLower().Contains(instanceId.ToLower())) continue;
                    driveIndex = i;
                    break;
                }

                if (driveIndex == -1)
                {
                    Issues.Add($"Smart Data found for {instanceId} with no matching drive. This is a Specify error");
                    break;
                }

                var diskAttributes = new List<SmartAttribute>();

                var vs = (byte[])m["VendorSpecific"];
                // Every 12th byte starting at byte index 2 is a smart identifier.
                for (var i = 2; i < vs.Length; i += 12)
                {
                    var c = new byte[12];
                    // Copy 12 bytes into a new array.
                    Array.Copy(vs, i, c, 0, 12);

                    // Once we reach the zeroes, we're past the smart attributes.
                    if (c[0] == 0)
                    {
                        break;
                    }
                    diskAttributes.Add(GetAttribute(c));
                }
                drives[driveIndex].SmartData = diskAttributes;
            }
        }
        catch (ManagementException e)
        {
            Issues.Add("Error retrieving SMART Utils." + e.Message);
        }
        catch (Exception e)
        {
            DebugLog.LogEvent("Unexpected exception thrown during SMART Data Retrieval.", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
            DebugLog.LogEvent($"{e}", DebugLog.Region.Hardware);
        }
        var LDtoP = Utils.GetWmiObj("Win32_LogicalDiskToPartition");
        for (var di = 0; di < drives.Count(); di++)
        {
            for (var pi = 0; pi < drives[di].Partitions.Count(); pi++)
            {
                foreach (var logicalDisk in LDtoP)
                {
                    try
                    {
                        if (((string)logicalDisk["Antecedent"]).Contains(drives[di].Partitions[pi].Caption))
                        {
                            var dependent = (string)logicalDisk["Dependent"];
                            var whatIActuallyWantFromDependent = dependent.Split('"')[1].Replace("\\", string.Empty);
                            var runningOutOfVariableNames = Utils.GetWmiObj("Win32_LogicalDisk");
                            foreach (var lackingCreativity in runningOutOfVariableNames)
                            {
                                if (whatIActuallyWantFromDependent == (string)lackingCreativity["DeviceID"])
                                {
                                    drives[di].Partitions[pi].PartitionLabel = whatIActuallyWantFromDependent;
                                    drives[di].Partitions[pi].PartitionFree = (UInt64)lackingCreativity["FreeSpace"];
                                    drives[di].Partitions[pi].Filesystem = (string)lackingCreativity["FileSystem"];
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog.LogEvent("Unexpected exception thrown during paritition linking", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
                        DebugLog.LogEvent($"{ex}", DebugLog.Region.Hardware);
                    }
                }
            }
        }
        var partitionInfo = Utils.GetWmiObj("Win32_Volume");
        foreach (var partition in partitionInfo)
        {
            // Check if partition drive size is identical to exactly one partition drive size in the list of disks. If it is, add win32_volume data to it.
            // If it is not, create an issue for the failed link.
            ulong partitionSize = 0;
            ulong blockSize = 0;
            try
            {
                partitionSize = (ulong)partition["Capacity"];
                blockSize = (ulong)partition["BlockSize"];
            }
            catch (NullReferenceException)
            {
                Issues.Add("Failure to parse partition information - No capacity found. This is likely a virtual or unallocated drive.");
                continue;
            }

            // Drive and Partition indices.
            var dIndex = -1;
            var pIndex = -1;

            // Indicators; If found and unique, store partition information with the drive under dIndex/pIndex.
            // Otherwise, store into non-specific partition list.
            var found = false;
            var unique = true;
            //var LDtoP = Utils.GetWmiObj("Win32_LogicalDiskToPartition");
            for (var di = 0; di < drives.Count(); di++)
            {
                for (var pi = 0; pi < drives[di].Partitions.Count(); pi++)
                {
                    if (drives[di].Partitions[pi].Filesystem != null)
                    {
                        continue;
                    }
                    var fileSystem = (string)partition["FileSystem"];
                    if (fileSystem.ToLower().Equals("ntfs"))
                    {
                        if (Math.Abs((float)partitionSize - drives[di].Partitions[pi].PartitionCapacity) > 8192)
                            continue;
                        // If it hasn't been found yet, this is a potential match.
                        if (!found)
                        {
                            pIndex = pi;
                            dIndex = di;
                            found = true;
                        }
                        // If it has been found, there are two matches, it is not unique, stop the check.
                        else
                        {
                            unique = false;
                            break;
                        }
                    }
                    else if (fileSystem.ToLower().Equals("fat32") || fileSystem.ToLower().Equals("exfat32"))
                    {
                        if (partitionSize != drives[di].Partitions[pi].PartitionCapacity &&
                            partitionSize + (2048 * 2048) != drives[di].Partitions[pi].PartitionCapacity &&
                            partitionSize - (2048 * 2048) != drives[di].Partitions[pi].PartitionCapacity) continue;
                        // If it hasn't been found yet, this is a potential match.
                        if (!found)
                        {
                            pIndex = pi;
                            dIndex = di;
                            found = true;
                        }
                        // If it has been found, there are two matches, it is not unique, stop the check.
                        else
                        {
                            unique = false;
                            break;
                        }
                    }
                }
                // If it is not unique, no drive or partition index is valid. Stop checking.
                if (unique)
                    continue;
                dIndex = -1;
                pIndex = -1;
                break;
            }
            if (found && unique)
            {
                // These should never be -1, however they seem to happen occasionally.
                // Prevent the exception by continuing the loop.
                if (dIndex == -1 || pIndex == -1)
                {
                    Issues.Add($"di/pi = -1 for partition {partitionSize} - this is a Specify error.");
                    continue;
                }
                var matchingPartition = drives[dIndex].Partitions[pIndex];
                var driveLetter = partition["Label"];
                if (driveLetter != null)
                {
                    matchingPartition.PartitionLabel = (string)driveLetter;
                }
                matchingPartition.PartitionFree = (ulong)partition["FreeSpace"];
                var fileSystem = partition["FileSystem"];
                if (fileSystem != null)
                {
                    matchingPartition.Filesystem = (string)fileSystem;
                }
            }
            else
            {
                var driveLetter = "";
                var fileSystem = "";
                try
                {
                    driveLetter = (string)partition["DriveLetter"];
                    fileSystem = (string)partition["FileSystem"];
                }
                catch
                { }
                if (driveLetter == "")
                {
                    DebugLog.LogEvent("Partition Link could not be established. Detailed Information follows:", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
                    DebugLog.LogEvent($"Failing Partion: Size: {partitionSize} - Label: {driveLetter} - File System: {fileSystem}", DebugLog.Region.Hardware);
                    DebugLog.LogEvent("Drive Info:", DebugLog.Region.Hardware);

                    foreach (var drive in drives)
                    {
                        string errorPartitionInfo = "";
                        foreach (var errorPartition in drive.Partitions)
                        {
                            var eSize = errorPartition.PartitionCapacity;
                            var eFS = errorPartition.Filesystem;
                            errorPartitionInfo += $"Size: {eSize} - ";
                            errorPartitionInfo += $"FS: {eFS} - ";
                            errorPartitionInfo += $"Difference: {Math.Abs((long)(partitionSize - eSize))} - ";
                            if (eFS != null && fileSystem != null)
                            {
                                errorPartitionInfo += $"Possible: {eFS.Equals(fileSystem)}\n";
                            }
                            else
                            {
                                errorPartitionInfo += $"Possible: false\n";
                            }
                            //errorPartitionInfo += $"Size: {eSize} - FS: {eFS} - Difference: {Math.Abs((float)partitionSize - eSize)} - Possible: {eFS.Equals(fileSystem)}\n";
                        }

                        DebugLog.LogEvent($"{drive.DeviceName}\n{errorPartitionInfo}", DebugLog.Region.Hardware);
                    }
                    Issues.Add($"Partition link could not be established for {partitionSize} byte partition - Drive Label: {driveLetter} -  File System: {fileSystem}");
                }
            }
        }
        foreach (var d in drives)
        {
            bool complete = true;
            UInt64 free = 0;
            foreach (var partition in d.Partitions)
            {
                if (partition.PartitionFree == 0)
                {
                    complete = false;
                }
                else
                {
                    free += partition.PartitionFree;
                }
            }
            if (!complete)
            {
                var LetteredDrives = DriveInfo.GetDrives();
                foreach (var letteredDrive in LetteredDrives)
                {
                    foreach (var partition in d.Partitions)
                    {
                        try
                        {
                            if (letteredDrive.Name.Contains(partition.PartitionLabel[0]))
                            {
                                d.DiskFree = (UInt64)letteredDrive.AvailableFreeSpace;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            else
            {
                d.DiskFree = free;
            }
        }
        DebugLog.LogEvent($"GetDiskDriveInfo() completed. Total Runtime: {(DateTime.Now - start).TotalMilliseconds}", DebugLog.Region.Hardware);
        return drives;
    }

    private static SmartAttribute GetAttribute(byte[] data)
    {
        // Smart data is fed backwards, with byte 10 being the first byte for the attribute and byte 5 being the last.
        var values = new byte[6]
        {
            data[10], data[9], data[8], data[7], data[6], data[5]
        };

        var attribute = new SmartAttribute()
        {
            Id = data[0],
            Name = GetAttributeName(data[0]),
        };
        var rawValue = BitConverter.ToString(values);

        rawValue = rawValue.Replace("-", string.Empty);
        attribute.RawValue = rawValue;
        return attribute;
    }
    private static string GetAttributeName(byte id)
    {
        return id switch
        {
            0x1 => "Read Error Rate",
            0x2 => "Throughput Performance",
            0x3 => "Spin-Up Time",
            0x4 => "Start/Stop Count",
            0x5 => "Reallocated Sectors Count(!)",
            0x6 => "Read Channel Margin",
            0x7 => "Seek Error Rate",
            0x8 => "Seek Time Performance",
            0x9 => "Power-On Hours",
            0xA => "Spin Retry Count(!)",
            0xB => "Calibration Retry Count",
            0xC => "Power Cycle Count",
            0xD => "Soft Read Error Rate",
            0x16 => "Current Helium Level",
            0x17 => "Helium Condition Lower",
            0x18 => "Helium Condition Upper",
            0xAA => "Available Reserved Space",
            0xAB => "SSD Program Fail Count",
            0xAC => "SSD Erase Fail Count",
            0xAD => "SSD Wear Leveling Count",
            0xAE => "Unexpected Power Loss Count",
            0xAF => "Power Loss Protection Failure",
            0xB0 => "Erase Fail Count",
            0xB1 => "Wear Range Delta",
            0xB2 => "Used Reserved Block Count",
            0xB3 => "Used Reserved Block Count Total",
            0xB4 => "Unused Reserved Block Count Total",
            0xB5 => "Vendor Specific", // Program Fail Count Total or Non-4K Aligned Access Count
            0xB6 => "Erase Fail Count",
            0xB7 => "Vendor Specific (WD or Seagate)", //SATA Downshift Error Count or Runtime Bad Block. WD or Seagate respectively.
            0xB8 => "End-to-end Error Count(!)",
            0xB9 => "Head Stability",
            0xBA => "Induced Op-Vibration Detection",
            0xBB => "Reported Uncorrectable Errors(!)",
            0xBC => "Command Timeout(!)",
            0xBD => "High Fly Writes(!)",
            0xBE => "Airflow Temperature",
            0xBF => "G-Sense Error Rate",
            0xC0 => "Unsafe Shutdown Count",
            0xC1 => "Load Cycle Count",
            0xC2 => "Temperature",
            0xC3 => "Hardware ECC Recovered",
            0xC4 => "Reallocation Event Count(!)",
            0xC5 => "Current Pending Sector Count(!)",
            0xC6 => "Uncorrectable Sector Count(!)",
            0xC7 => "UltraDMA CRC Error Count",
            0xC8 => "Multi-Zone Error Rate(!)(Unless Fujitsu)",
            0xC9 => "Soft Read Error Rate(!)",
            0xCA => "Data Address Mark Errors",
            0xCB => "Run Out Cancel",
            0xCC => "Soft ECC Correction",
            0xCD => "Thermal Asperity Rate",
            0xCE => "Flying Height",
            0xCF => "Spin High Current",
            0xD0 => "Spin Buzz",
            0xD1 => "Offline Seek Performance",
            0xD2 => "Vibration During Write",
            0xD3 => "Vibration During Write",
            0xD4 => "Shock During Write",
            0xDC => "Disk Shift",
            0xDD => "G-Sense Error Rate",
            0xDE => "Loaded Hours",
            0xDF => "Load/Unload Retry Count",
            0xE0 => "Load Friction",
            0xE1 => "Load/Unload Cycle Count",
            0xE2 => "Load 'In'-time",
            0xE3 => "Torque Amplification Count",
            0xE4 => "Power-Off Retract Cycle",
            0xE6 => "GMR Head Amplitude / Drive Life Protection Status", // HDDs / SSDs respectively.
            0xE7 => "SSD Life Left / HDD Temperature",
            0xE8 => "Vendor Specific", // Endurance Remaining or Available Reserved Space.
            0xE9 => "Media Wearout Indicator",
            0xEA => "Average and Maximum Erase Count",
            0xEB => "Good Block and Free Block Count",
            0xF0 => "Head Flying Hours (Unless Fujitsu)",
            0xF1 => "Total LBAs Written",
            0xF2 => "Total LBAs Read",
            0xF3 => "Total LBAs Written Expanded",
            0xF4 => "Total LBAs Read Expanded",
            0xF9 => "NAND Writes (# of GiB)",
            0xFA => "Read Error Retry Rate",
            0xFB => "Minimum Spares Remaining",
            0xFC => "Newly Added Bad Flash Block",
            0xFE => "Free Fall Protection",
            _ => "Vendor Specific"
        };
        ;
    }


    // TEMPERATURES
    private static async System.Threading.Tasks.Task<List<TempMeasurement>> GetTemps()
    {
        DateTime start = DateTime.Now;
        await DebugLog.LogEventAsync("GetTemps() Started", DebugLog.Region.Hardware);
        //Any temp sensor reading below 24 will be filtered out
        //These sensors are either not reading in celsius, are in error, or we cannot interpret them properly here
        var Temps = new List<TempMeasurement>();
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true
        };

        try
        {
            computer.Open();
            computer.Accept(new SensorUpdateVisitor());

            foreach (var hardware in computer.Hardware)
            {
                Temps.AddRange(
                    from subhardware in hardware.SubHardware
                    from sensor in subhardware.Sensors
                    where sensor.SensorType.Equals(SensorType.Temperature) && sensor.Value > 24 || sensor.Name.ToLower().Contains("tjmax")
                    select new TempMeasurement
                    { Hardware = hardware.Name, SensorName = sensor.Name, SensorValue = sensor.Value.Value }
                    );

                Temps.AddRange(
                    from sensor in hardware.Sensors
                    where sensor.SensorType.Equals(SensorType.Temperature) && sensor.Value > 24 || sensor.Name.ToLower().Contains("tjmax")
                    select new TempMeasurement
                    { Hardware = hardware.Name, SensorName = sensor.Name, SensorValue = sensor.Value.Value }
                    );
            }
        }
        catch (OverflowException)
        {
            Issues.Add("Absolute value overflow occured when fetching temperature data");
        }
        catch (Exception ex)
        {
            await DebugLog.LogEventAsync($"Exception during temperature measurement: " + ex, DebugLog.Region.Hardware);
        }
        finally
        {
            computer.Close();
        }
        await DebugLog.LogEventAsync($"GetTemps() Completed. Total Runtime: {(DateTime.Now - start).TotalMilliseconds}", DebugLog.Region.Hardware);
        return Temps;
    }


    // BATTERIES
    private static List<BatteryData> GetBatteryData()
    {
        DateTime start = DateTime.Now;
        DebugLog.LogEvent("GetBatteryData() Started.", DebugLog.Region.Hardware);
        List<BatteryData> BatteryInfo = new List<BatteryData>();
        String path =
            System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()
                .Location); //Directory the .exe has been launched from

        Process cmd = new Process //Generate the XML report we'll be grabbing the data from
        {
            StartInfo =
            {
                FileName = "powercfg",
                WorkingDirectory = path,
                CreateNoWindow = true,
                Arguments = "/batteryreport /xml",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        cmd.Start();

        Stopwatch timer = Stopwatch.StartNew();
        TimeSpan timeout = new TimeSpan().Add(TimeSpan.FromSeconds(60));

        while (timer.Elapsed < timeout)
        {
            if (File.Exists(Path.Combine(path, "battery-report.xml")) &&
                Process.GetProcessesByName("powercfg").Length == 0)
            {

                XmlDocument doc = new XmlDocument();
                doc.Load(Path.Combine(path, "battery-report.xml"));
                List<JToken> BatteryData =
                    JObject.Parse(JsonConvert.SerializeXmlNode(doc))["BatteryReport"]["Batteries"].Children().Children()
                        .ToList();

                foreach (JToken battery in BatteryData)
                    if (battery.HasValues)
                    {
                        BatteryInfo.Add(
                            new BatteryData
                            {
                                Name = (string)battery["Id"],
                                Manufacturer = (string)battery["Manufacturer"],
                                Chemistry = (string)battery["Chemistry"],
                                Design_Capacity = (string)battery["DesignCapacity"],
                                Full_Charge_Capacity = (string)battery["FullChargeCapacity"],
                                Remaining_Life_Percentage =
                                    string.Concat(
                                        ((float)battery["FullChargeCapacity"] / (float)battery["DesignCapacity"] * 100)
                                        .ToString("0.00"), "%")
                            });
                    }

                File.Delete(Path.Combine(path, "battery-report.xml"));
                break;
            }
            var reader = cmd.StandardOutput.ReadToEnd();
            var errorReader = cmd.StandardError.ReadLine();
            /*if(reader != null && reader != "")
            {
                DebugLog.LogEvent($"Battery Report returned", DebugLog.Region.Hardware);
                break;
            }*/
            if (errorReader != null && errorReader != "")
            {
                DebugLog.LogEvent($"PowerCfg reported an error: {errorReader}", DebugLog.Region.Hardware, DebugLog.EventType.ERROR);
                break;
            }
        }

        if (timer.Elapsed > timeout)
            Issues.Add("Battery report was not generated before the timeout!");

        timer.Stop();
        cmd.Close();
        DebugLog.LogEvent($"GetBatteryInfo() completed. Total Runtime: {(DateTime.Now - start).TotalMilliseconds}", DebugLog.Region.Hardware);
        return BatteryInfo;
    }
}