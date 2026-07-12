// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DepotDownloader
{
    public static class ManifestRequestCodeProvider
    {
        private static readonly HttpClient _httpClient = HttpClientFactory.CreateHttpClient();
        private static RequestCodeConfig _config;
        private static readonly object _lock = new();
        private static readonly HashSet<string> _failedProviderUrls = new();

        /// <summary>
        /// 初始化配置（线程安全，通常只在启动时调用一次）。
        /// </summary>
        public static void Initialize(RequestCodeConfig config)
        {
            lock (_lock)
            {
                _config = config ?? throw new ArgumentNullException(nameof(config));
            }
        }

        /// <summary>
        /// 根据 manifestId 获取请求码。
        /// </summary>
        public static async Task<ulong> GetRequestCodeAsync(ulong manifestId)
        {
            if (_config == null)
            {
                throw new InvalidOperationException("ManifestRequestCodeProvider 尚未初始化，请先调用 Initialize。");
            }

            // 查找所有匹配的 provider 条目
            var entries = FindAllMatchingProviders(manifestId);
            if (entries == null || entries.Count == 0)
            {
                throw new KeyNotFoundException($"未找到 manifestId {manifestId} 对应的配置，且无默认配置。");
            }

            // 将之前失败的 provider 排到后面，优先使用健康的 provider
            var orderedEntries = entries
                .OrderBy(e => _failedProviderUrls.Contains(e.Url) ? 1 : 0)
                .ToList();

            // 依次尝试每个提供者，实现故障切换
            var errors = new List<string>();
            foreach (var entry in orderedEntries)
            {
                var result = await ExecuteRequestAsync(entry, manifestId);
                if (result > 0)
                {
                    // 成功获取到 request code，将该 provider 从失败列表中移除（如果存在）
                    _failedProviderUrls.Remove(entry.Url);
                    return result;
                }

                // 记录失败，标记该 provider 为不可用
                _failedProviderUrls.Add(entry.Url);
                errors.Add($"  - {entry.Url.Replace("{manifestId}", manifestId.ToString())}: 请求失败或返回无效值");
                Console.WriteLine($"[ManifestRequestCodeProvider] API {entry.Url.Replace("{manifestId}", manifestId.ToString())} 请求失败，尝试下一个提供者...");
            }

            // 所有提供者都失败了，清空失败列表以便下次重试
            _failedProviderUrls.Clear();
            var errorMsg = $"所有 API 提供者请求失败 (manifestId: {manifestId}):\n{string.Join("\n", errors)}";
            Console.WriteLine(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        private static List<ProviderEntry> FindAllMatchingProviders(ulong manifestId)
        {
            var idStr = manifestId.ToString();
            var matches = new List<ProviderEntry>();

            // 1. 精确匹配
            var exactMatches = _config.Providers.Where(p =>
                p.ManifestIds.Any(mid => mid == idStr)).ToList();

            if (exactMatches.Any())
            {
                return exactMatches;
            }

            // 2. 通配符 "*" 匹配
            var wildcardMatches = _config.Providers.Where(p =>
                p.ManifestIds.Any(mid => mid == "*")).ToList();

            return wildcardMatches;
        }

        private static async Task<ulong> ExecuteRequestAsync(ProviderEntry entry, ulong manifestId)
        {
            var url = entry.Url.Replace("{manifestId}", manifestId.ToString());
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(entry.TimeoutMs));

            // 构建请求消息
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // 设置 User-Agent
            if (!string.IsNullOrEmpty(entry.UserAgent))
            {
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.TryParseAdd(entry.UserAgent);
            }

            // 设置自定义 Header
            foreach (var kvp in entry.Headers)
            {
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            }

            try
            {
                var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();

                // 根据 responseFormat 解析
                if (entry.ResponseFormat.Equals("plaintext", StringComparison.OrdinalIgnoreCase))
                {
                    if (ulong.TryParse(body.Trim(), out var code))
                        return code;
                }
                else if (entry.ResponseFormat.Equals("jsonpath", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(body);
                    var value = doc.RootElement;
                    var pathParts = entry.JsonPath.TrimStart('$', '.').Split('.');
                    foreach (var part in pathParts)
                    {
                        if (!value.TryGetProperty(part, out value))
                            break;
                    }
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        if (ulong.TryParse(value.GetString(), out var code))
                            return code;
                    }
                    else if (value.ValueKind == JsonValueKind.Number)
                    {
                        return value.GetUInt64();  // 直接取数字
                    }
                }
                else
                {
                    throw new NotSupportedException($"不支持的 responseFormat: {entry.ResponseFormat}");
                }

                // 解析失败返回 0
                Console.WriteLine($"Warning: 无法从响应中解析出有效的 uint64，URL: {url}, Body: {body}");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Request timed out for URL: {url}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request failed for URL: {url}, Error: {ex.Message}");
                return 0;
            }
        }
    }
}
