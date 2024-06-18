using System.Collections.Generic;
using MCServerLauncher.UI.Helpers;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace MCServerLauncher.UI.Modules.Download
{
    internal class AList
    {
        private readonly string FileListAPI = "/api/fs/list";
        private readonly string FileUrlAPI = "/api/fs/get";

        public class AListFileStructure
        {
            public string FileName { get; set; }
            public string FileSize { get; set; }
            public bool IsDirectory { get; set; }
        }
        public class AListFileDetail
        {
            public string FileName { get; set; }
            public string FileSize { get; set; }
            public bool IsDirectory { get; set; }
            public string RawUrl { get; set; }
        }

        public async Task<List<AListFileStructure>> GetFileList(string Host, string Path)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{Host}{FileListAPI}?path={Path}");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemoteFileList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").SelectToken("content");
                List<AListFileStructure> FileList = new();
                foreach (JToken File in RemoteFileList)
                {
                    FileList.Add(new AListFileStructure
                    {
                        FileName = File.SelectToken("name").ToString(),
                        FileSize = File.SelectToken("size").ToString(),
                        IsDirectory = File.SelectToken("is_dir").ToObject<bool>()
                    });
                }
                return FileList;
            }
            else
            {
                return null;
            }
        }
        public async Task<AListFileDetail> GetFileUrl(string Host, string Path)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{Host}{FileUrlAPI}?path={Path}");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemoteFileDetail = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data");
                return new AListFileDetail
                {
                    FileName = RemoteFileDetail.SelectToken("name").ToString(),
                    FileSize = RemoteFileDetail.SelectToken("size").ToString(),
                    IsDirectory = RemoteFileDetail.SelectToken("is_dir").ToObject<bool>(),
                    RawUrl = RemoteFileDetail.SelectToken("raw_url").ToString()
                };
            }
            else
            {
                return null;
            }
        }
    }
}
