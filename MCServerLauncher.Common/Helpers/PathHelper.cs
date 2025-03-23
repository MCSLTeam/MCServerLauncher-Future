using System.Runtime.InteropServices;

namespace MCServerLauncher.Common.Helpers;

public static class PathHelper
{
    public static string GetRelativePath(string basePath, string targetPath)
    {
        // 规范化路径
        basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
        targetPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);

        // 相同路径直接返回当前目录
        if (basePath.Equals(targetPath, GetComparisonType()))
            return ".";

        // 检查根目录是否一致
        if (!PathsShareRoot(basePath, targetPath))
            return targetPath;

        // 分割路径为组件
        var baseParts = SplitPath(basePath);
        var targetParts = SplitPath(targetPath);

        // 找出共同前缀
        var commonLength = FindCommonPrefix(baseParts, targetParts);

        // 计算向上回退的层级
        var backLevels = baseParts.Length - commonLength;

        // 构建相对路径组件
        var relativeParts = new List<string>();
        for (var i = 0; i < backLevels; i++)
            relativeParts.Add("..");

        for (var i = commonLength; i < targetParts.Length; i++)
            relativeParts.Add(targetParts[i]);

        // 合并最终路径
        return string.Join(Path.DirectorySeparatorChar.ToString(), relativeParts);
    }

    private static bool PathsShareRoot(string path1, string path2)
    {
        // 处理 Windows 驱动器号
        if (Path.IsPathRooted(path1) && Path.IsPathRooted(path2))
        {
            var root1 = Path.GetPathRoot(path1);
            var root2 = Path.GetPathRoot(path2);
            return root1.Equals(root2, GetComparisonType());
        }

        return true; // Unix 系统始终共享根
    }

    private static string[] SplitPath(string path)
    {
        return path.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int FindCommonPrefix(string[] parts1, string[] parts2)
    {
        var common = 0;
        var minLength = Math.Min(parts1.Length, parts2.Length);
        while (common < minLength && parts1[common].Equals(parts2[common], GetComparisonType())) common++;
        return common;
    }

    private static StringComparison GetComparisonType()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}