// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepotDownloader
{
    /// <summary>
    /// Represents a single depot entry obtained from SteamCMD API.
    /// Designed to be extensible for future integration with Steam PICS.
    /// </summary>
    public sealed class DepotManifestInfo
    {
        public uint DepotId { get; set; }
        public ulong ManifestId { get; set; }
        public string Branch { get; set; } = "public";
        public bool IsDlc { get; set; }
        public string DepotName { get; set; }
    }

    /// <summary>
    /// Provides methods to retrieve depot manifest information from external APIs.
    /// Currently uses steamcmd.net API; can be replaced with SteamPicsProvider later.
    /// </summary>
    public static class SteamCmdApi
    {
        private static readonly HttpClient httpClient = HttpClientFactory.CreateHttpClient();

        /// <summary>
        /// Fetches all public depots and their manifest IDs for the given AppID.
        /// </summary>
        /// <param name="appId">Steam AppID.</param>
        /// <returns>List of depot manifest info entries.</returns>
        public static async Task<List<DepotManifestInfo>> GetDepotsAsync(uint appId)
        {
            var results = new List<DepotManifestInfo>();

            try
            {
                var url = $"https://api.steamcmd.net/v1/info/{appId}";
                using var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                // Navigate: data -> appid -> depots
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data))
                    return results;

                var appIdStr = appId.ToString();
                if (!data.TryGetProperty(appIdStr, out var appEntry))
                    return results;

                if (!appEntry.TryGetProperty("depots", out var depots))
                    return results;

                foreach (var depotProp in depots.EnumerateObject())
                {
                    // Each property name is a depot ID (numeric string)
                    if (!uint.TryParse(depotProp.Name, out var depotId))
                        continue;

                    var depotObj = depotProp.Value;

                    // Correct structure: depotObj -> "manifests" -> "public" -> "gid"
                    if (!depotObj.TryGetProperty("manifests", out var manifests))
                        continue;

                    if (!manifests.TryGetProperty("public", out var publicBranch))
                        continue;

                    if (!publicBranch.TryGetProperty("gid", out var gidElement))
                        continue;

                    if (!ulong.TryParse(gidElement.GetString(), out var manifestId))
                        continue;

                    var info = new DepotManifestInfo
                    {
                        DepotId = depotId,
                        ManifestId = manifestId,
                        Branch = "public",
                        IsDlc = depotObj.TryGetProperty("dlcappid", out _), // presence of dlcappid indicates DLC
                        DepotName = depotObj.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null
                    };

                    results.Add(info);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SteamCmdApi: Failed to get depots for app {appId}: {ex.Message}");
            }

            return results;
        }
    }
}
