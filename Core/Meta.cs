using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf.Compiler;
using Newtonsoft.Json.Linq;

namespace Core
{
    public class Meta
    {
        private static readonly HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static async Task<(List<string> versions, string packageId, string password, string preDownloadPassword)> GetVersions(string game, string region)
        {
            Console.WriteLine($"Fetching versions for {game} in {region}...");

            var gameMeta = await Sophon.GetGameBranches(game, region);

            var latest = (string)gameMeta["data"]["game_branches"][0]["main"]["tag"];
            latest = string.Join('.', latest.Split('.').Take(2));

            var metaJson = await Dispatch.GetDispatchData();

            var versions = ((JObject)metaJson[game]["sophonHashes"]).Properties().Select(p => p.Name).ToList();

            if (!versions.Contains(latest))
                versions.Add(latest);

            bool preDownloadAvailable = false;
            if (gameMeta["data"]["game_branches"][0]["pre_download"]?.Type != JTokenType.Null)
            {
                var preLatest = (string)gameMeta["data"]["game_branches"][0]["pre_download"]["tag"];
                preLatest = string.Join('.', preLatest.Split('.').Take(2));
                if (!versions.Contains(preLatest))
                    versions.Add($"{preLatest} (pre-download)");
                preDownloadAvailable = true;
            }

            versions.Sort();
            versions.Reverse();

            var packageId = (string)gameMeta["data"]["game_branches"][0]["main"]["package_id"];
            var password = (string)gameMeta["data"]["game_branches"][0]["main"]["password"];
            var preDownloadPassword = preDownloadAvailable ? (string)gameMeta["data"]["game_branches"][0]["pre_download"]["password"] : "";


            return (versions, packageId, password, preDownloadPassword);
        }

        public static async Task<List<string[]>> GetPackages(string region, string version, string packageId, string password)
        {
            Console.WriteLine($"Fetching packages for {packageId} password {password} in {region} version {version}...");

            bool preDownload = false;
            if (version.EndsWith(" (pre-download)"))
            {
                preDownload = true;
                version = version.Replace(" (pre-download)", "");
            }

            var build = await Sophon.GetBuild(region, packageId, password, $"{version}.0", preDownload);

            return await GetPackagesInternal(build);
        }

        public static async Task<List<string[]>> GetCustomPackages(string url)
        {
            var build = await Sophon.GetCustomBuild(url);
            return await GetPackagesInternal(build);
        }

        private static async Task<List<string[]>> GetPackagesInternal(JObject build)
        {
            List<string[]> packages = new();

            foreach (var package in build["data"]["manifests"])
            {
                Console.WriteLine($"{package["category_id"]} - {package["matching_field"]} - {package["category_name"]}");
                packages.Add(new string[] { (string)package["category_id"], (string)package["matching_field"], (string)package["category_name"] });
            }

            return packages;
        }
    }
}
