namespace MCServerLauncher.Common.ProtoType.Status;

public record OsInfo(string Name, string Arch);

public record CpuInfo(string Vendor, string Name, int Count, double Usage);

public record MemInfo(ulong Total, ulong Free); // in KB

public record DriveInformation(string DriveFormat, ulong Total, ulong Free); // in Byte

public record SystemInfo(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive);