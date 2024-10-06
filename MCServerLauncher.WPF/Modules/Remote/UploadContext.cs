using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.Remote
{
    public enum UploadContextState
    {
        Opening,
        Cancelling,
        Closed
    }

    public class UploadContext
    {
        public UploadContext(string fileId, CancellationTokenSource uploadCts, NetworkLoadSpeed uploadSpeed,
            FileStream uploadFileStream, IDaemon daemon)
        {
            FileId = fileId;
            UploadCts = uploadCts;
            UploadSpeed = uploadSpeed;
            UploadFileStream = uploadFileStream;
            Daemon = daemon;

            State = UploadContextState.Opening;
        }

        /// <summary>
        ///     服务端分配的文件ID, 用于标识一个上传任务
        /// </summary>
        public string FileId { get; }

        /// <summary>
        ///     各分段上传任务共同持有的CancellationTokenSource
        /// </summary>
        public CancellationTokenSource UploadCts { get; }

        /// <summary>
        ///     上传速度,用于查看速度和ETA,同时提供了速度更新时的event
        /// </summary>
        public NetworkLoadSpeed UploadSpeed { get; }

        /// <summary>
        ///     待上传文件的文件流
        /// </summary>
        public FileStream UploadFileStream { get; }

        /// <summary>
        ///     开启该上传上下文的Daemon
        /// </summary>
        public IDaemon Daemon { get; }

        public Task? UploadTask { get; set; }

        /// <summary>
        ///     服务端已接收的字节数
        /// </summary>
        public long Received { get; set; } = 0;

        /// <summary>
        ///     是否完成上传
        /// </summary>
        public bool Done { get; set; } = false;

        /// <summary>
        ///     上传任务是否已经关闭
        /// </summary>
        public UploadContextState State { get; private set; }


        public void OnDone()
        {
            State = UploadContextState.Closed;
            UploadFileStream.Close();
        }

        /// <summary>
        ///     取消上传(not thread-safe)
        /// </summary>
        public async Task Cancel()
        {
            if (State != UploadContextState.Opening)
                throw new InvalidOperationException("Cannot cancel an already closed upload context");

            State = UploadContextState.Cancelling;

            UploadCts.Cancel(); // 发送取消信号
            if (UploadTask != null) await UploadTask; // 等待各分区上传任务完成
            UploadFileStream.Close();
            await Daemon.UploadFileCancelAsync(this); // 向服务端发出取消信号

            State = UploadContextState.Closed;
        }
    }
}