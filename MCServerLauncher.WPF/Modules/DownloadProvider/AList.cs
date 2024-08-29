using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.DownloadProvider
{
    internal class AList
    {
        private readonly string _fileListApi = "/api/fs/list";
        private readonly string _fileUrlApi = "/api/fs/get";

        /// <summary>
        ///    Get AList file list.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<List<AListFileStructure>> GetFileList(string host, string path)
        {
            var response = await Network.SendGetRequest($"{host}{_fileListApi}?path={path}");
            if (!response.IsSuccessStatusCode) return null;
            var remoteFileList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                .SelectToken("data")!.SelectToken("content");
            return remoteFileList!.Select(file => new AListFileStructure
            {
                FileName = file.SelectToken("name")!.ToString(),
                FileSize = file.SelectToken("size")!.ToString(),
                IsDirectory = file.SelectToken("is_dir")!.ToObject<bool>()
            }).ToList();
        }

        /// <summary>
        ///    Get download url.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<string> GetFileUrl(string host, string path)
        {
            var response = await Network.SendGetRequest($"{host}{_fileUrlApi}?path={path}");
            if (!response.IsSuccessStatusCode) return null;
            var remoteFileDetail = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                .SelectToken("data");
            return remoteFileDetail!.SelectToken("raw_url")!.ToString();
        }

        public class AListFileStructure
        {
            public string FileName { get; set; }
            public string FileSize { get; set; }
            public bool IsDirectory { get; set; }
        }
    }
}