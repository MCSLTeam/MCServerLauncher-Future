using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace MCServerLauncher.Daemon.FileManagement
{
    internal class FileManager
    {
        public static readonly string ROOT = "mcsl_future";
        private static ConcurrentDictionary<Guid, FileUploadInfo> _uploadSessions = new();

        /// <summary>
        /// 请求上传文件:首先检查是否有同名文件正在上传,若没有,则预分配空间并添加后缀.tmp,返回file_id
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="size">文件总大小</param>
        /// <param name="chunkSize">文件分片传输大小</param>
        /// <param name="sha1">预期的sha1</param>
        /// <returns>分配的file_id</returns>
        public static Guid FileUploadRequest(string path, long size, long chunkSize, string sha1)
        {
            // 由于FileStream.WriteAsync只支持int,故提前检查,若大于2G,则返回空
            if ((int)size != size || (int)chunkSize != chunkSize || size < 0 || chunkSize < 0 || chunkSize > size)
            {
                return Guid.Empty;
            }

            var fileName = Path.Combine(ROOT, path);

            // check if file is uploading
            foreach (var info in _uploadSessions.Values)
            {
                if (info.FileName == path) throw new Exception("file is uploading");
            }

            // pre-allocate file in disk
            try
            {
                // ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);

                FileStream fs = new(fileName + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                fs.SetLength(size);
                fs.Seek(0, SeekOrigin.Begin);
                var guid = Guid.NewGuid();

                _uploadSessions.TryAdd(guid, new FileUploadInfo(fileName, size, chunkSize, sha1, fs));
                return guid;
            }
            catch (Exception _)
            {
                return Guid.Empty;
            }
        }

        /// <summary>
        /// 写入文件分片
        /// </summary>
        /// <param name="id">file_id</param>
        /// <param name="offset">分片文件偏移量</param>
        /// <param name="strData">分片文件的字符串形式的数据</param>
        /// <returns>范围值为done</returns>
        /// <exception cref="Exception"></exception>
        public static async Task<(bool, long)> FileUploadChunk(Guid id, long offset, string strData)
        {
            var info = _uploadSessions[id]!;
            if (offset >= info.Size) throw new Exception("offset out of range");

            var data = Encoding.BigEndianUnicode.GetBytes(strData);
            int remain;
            var count = (remain = (int)(info.Size - offset)) < info.ChunkSize ? remain : (int)info.ChunkSize;

            info.File.Seek(offset, SeekOrigin.Begin);
            info.File.Write(
                data,
                0,
                count
            ); // 可能为奇数长度

            // 更新文件状态
            info.RemainLength -= count;
            info.Remain.Reduce(offset, offset + count);

            if (info.RemainLength > 0)
            {
                // partial done
                return (false, info.Size - info.RemainLength);
            }

            // file upload complete
            var sha1 = await FileSha1(info.File);
            info.File.Close();
            
            // rename tmp file to its origin name
            File.Move(info.FileName + ".tmp", info.FileName);

            if (sha1 == info.Sha1)
            {
                _uploadSessions.TryRemove(id, out _);
                Log.Debug($"[FileUploadChunk] file upload complete, sha1 matched, accepted.");
                return (true, info.Size - info.RemainLength);
            }

            throw new Exception("SHA1 mismatch");
        }

        private static Task<string> FileSha1(FileStream fs, uint bufferSize = 16384)
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
                    {
                        sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    }

                    sha1.TransformFinalBlock(buffer, 0, 0);

                    var hashBytes = sha1.Hash!;

                    fs.Seek(ptr, SeekOrigin.Begin);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            });
        }
    }
}