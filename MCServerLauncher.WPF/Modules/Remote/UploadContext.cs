using System.IO;
using System.Threading;

namespace MCServerLauncher.WPF.Modules.Remote
{
    public class UploadContext
    {
        public UploadContext(string fileId, CancellationTokenSource uploadCts, NetworkLoadSpeed uploadSpeed,
            FileStream uploadFileStream)
        {
            FileId = fileId;
            UploadCts = uploadCts;
            UploadSpeed = uploadSpeed;
            UploadFileStream = uploadFileStream;
        }

        public long Received { get; set; }
        public bool Done { get; set; }
        public string FileId { get; private set; }
        public CancellationTokenSource UploadCts { get; }
        public NetworkLoadSpeed UploadSpeed { get; private set; }
        public FileStream UploadFileStream { get; }

        public void Cancel()
        {
            UploadCts.Cancel();
            UploadFileStream.Close();
        }
    }
}