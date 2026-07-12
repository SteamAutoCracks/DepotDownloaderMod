// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DepotDownloader
{
    /// <summary>
    /// 顶层配置对象，对应 requestcode.json 的根结构。
    /// </summary>
    public class RequestCodeConfig
    {
        [JsonPropertyName("providers")]
        public List<ProviderEntry> Providers { get; set; } = new();
    }

    /// <summary>
    /// 单个服务提供者的配置条目。
    /// </summary>
    public class ProviderEntry
    {
        /// <summary>
        /// 匹配的 manifestId 列表，支持通配符 "*" 表示默认。
        /// </summary>
        [JsonPropertyName("manifestIds")]
        public List<string> ManifestIds { get; set; } = new();

        /// <summary>
        /// 请求 URL，可使用 {manifestId} 占位符。
        /// </summary>
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// 超时时间（毫秒），默认 10000。
        /// </summary>
        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 自定义 User-Agent，为空则不设置。
        /// </summary>
        [JsonPropertyName("userAgent")]
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>
        /// 自定义 HTTP 头。
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// 响应格式："plaintext" 或 "jsonpath"。
        /// </summary>
        [JsonPropertyName("responseFormat")]
        public string ResponseFormat { get; set; } = "plaintext";

        /// <summary>
        /// 当 responseFormat 为 jsonpath 时，指定 JSON 路径（如 $.content）。
        /// </summary>
        [JsonPropertyName("jsonPath")]
        public string JsonPath { get; set; } = string.Empty;
    }
}
