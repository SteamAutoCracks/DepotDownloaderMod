// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DepotDownloader
{
    static class DepotKeyStore
    {
        private static readonly Dictionary<uint, byte[]> depotKeysCache = new Dictionary<uint, byte[]>();

        /// <summary>
        /// 从命令行参数格式添加密钥（原有方法，保持不变）
        /// </summary>
        public static void AddAll(string[] values)
        {
            foreach (var value in values)
            {
                var split = value.Split(';');

                if (split.Length != 2)
                {
                    throw new FormatException($"Invalid depot key line: {value}");
                }

                depotKeysCache.Add(uint.Parse(split[0]), StringToByteArray(split[1]));
            }
        }

        /// <summary>
        /// 从 JSON 字符串解析 depot keys，格式为 {"depotId": "hexKey"}
        /// </summary>
        public static void AddAllFromJson(string jsonText)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                return;

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonText);
            if (dict == null || dict.Count == 0)
                return;

            foreach (var kv in dict)
            {
                if (!uint.TryParse(kv.Key, out var depotId))
                {
                    Console.WriteLine($"[警告] 无效的 depotId: {kv.Key}，跳过。");
                    continue;
                }

                var keyBytes = StringToByteArray(kv.Value);
                if (keyBytes == null || keyBytes.Length == 0)
                {
                    Console.WriteLine($"[警告] 无效的密钥值: {kv.Value}，跳过。");
                    continue;
                }

                depotKeysCache[depotId] = keyBytes;
            }
        }

        /// <summary>
        /// 从 JSON 文件自动加载 depot keys
        /// </summary>
        public static void LoadFromFile(string path)
        {
            if (!File.Exists(path))
                return;

            AddAllFromJson(File.ReadAllText(path));
        }

        private static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static bool ContainsKey(uint depotId)
        {
            return depotKeysCache.ContainsKey(depotId);
        }

        public static byte[] Get(uint depotId)
        {
            return depotKeysCache[depotId];
        }
    }
}
