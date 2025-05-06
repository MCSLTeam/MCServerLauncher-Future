namespace MCServerLauncher.Common.ProtoType.Status;

public record struct DaemonReport(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive, long StartTimeStamp);