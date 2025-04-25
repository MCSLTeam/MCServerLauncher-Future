namespace MCServerLauncher.Common.ProtoType.Status;

public record DaemonReport(OsInfo Os, CpuInfo Cpu, MemInfo Mem, DriveInformation Drive, long StartTimeStamp)
    : SystemInfo(Os, Cpu, Mem, Drive);