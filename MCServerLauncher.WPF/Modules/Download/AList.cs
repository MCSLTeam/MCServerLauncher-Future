using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCServerLauncher.WPF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.Modules.Download
{
    internal class AList
    {
        private readonly string _fileListApi = "/api/fs/list";
        private readonly string _fileUrlApi = "/api/fs/get";

        public async Task<List<AListFileStructure>> GetFileList(string host, string path)
        {
            var response = await NetworkUtils.SendGetRequest($"{host}{_fileListApi}?path={path}");
            if (!response.IsSuccessStatusCode) return null;
            var remoteFileList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                .SelectToken("data")!.SelectToken("content");
            return remoteFileList!.Select(file => new AListFileStructure
            {
                FileName = file.SelectToken("name")!.ToString(), FileSize = file.SelectToken("size")!.ToString(),
                IsDirectory = file.SelectToken("is_dir")!.ToObject<bool>()
            }).ToList();
        }

        public async Task<string> GetFileUrl(string host, string path)
        {
            var response = await NetworkUtils.SendGetRequest($"{host}{_fileUrlApi}?path={path}");
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