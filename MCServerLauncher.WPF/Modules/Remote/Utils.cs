using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote
{
    internal class Utils
    {
        public static Task<string> FileSha1(FileStream fs, uint bufferSize = 16384)
        {
            return Task.Run(() =>
            {
                using (var sha1 = SHA1.Create())
                {
                    var ptr = fs.Position;
                    fs.Seek(0, SeekOrigin.Begin);

                    var buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                        sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);

                    sha1.TransformFinalBlock(buffer, 0, 0);

                    var hashBytes = sha1.Hash!;

                    fs.Seek(ptr, SeekOrigin.Begin);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            });
        }
    }
}