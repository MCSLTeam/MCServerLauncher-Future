namespace MCServerLauncher.Common.ProtoType.Status;

public record struct OsInfo(string Name, string Arch);

public record struct CpuInfo(string Vendor, string Name, int Count, double Usage);

public record struct MemInfo(ulong Total, ulong Free); // in KB

public record struct DriveInformation(string DriveFormat, ulong Total, ulong Free); // in Byte

public record struct SystemInfo(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive);