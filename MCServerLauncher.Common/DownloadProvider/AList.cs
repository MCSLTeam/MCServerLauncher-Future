using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.DownloadProvider
{
    public static class AList
    {
        private const string _fileListApi = "/api/fs/list";
        private const string _fileUrlApi = "/api/fs/get";

        /// <summary>
        ///    Get AList file list.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static async Task<List<AListFileStructure>?> GetFileList(string host, string path)
        {
            var response = await HttpHelper.SendGetRequest($"{host}{_fileListApi}?path={path}");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var content = doc.RootElement.GetProperty("data").GetProperty("content");
            var results = new List<AListFileStructure>();
            foreach (var file in content.EnumerateArray())
            {
                results.Add(new AListFileStructure
                {
                    FileName = file.GetProperty("name").GetString(),
                    FileSize = file.GetProperty("size").GetRawText(),
                    IsDirectory = file.GetProperty("is_dir").GetBoolean()
                });
            }
            return results;
        }

        /// <summary>
        ///    Get download url.
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static async Task<string?> GetFileUrl(string host, string path)
        {
            var response = await HttpHelper.SendGetRequest($"{host}{_fileUrlApi}?path={path}");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("data").GetProperty("raw_url").GetString();
        }

        public class AListFileStructure
        {
            public string? FileName { get; set; }
            public string? FileSize { get; set; }
            public bool IsDirectory { get; set; }
        }
    }
}