// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDownloader
{
    public static class JsonEventWriter
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        public static string Serialize(DownloadEvent evt)
        {
            return JsonSerializer.Serialize(evt, evt.GetType(), Options);
        }
    }
}
