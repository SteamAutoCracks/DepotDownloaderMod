// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

/// <summary>
/// 管理 App Access Token 的加载、保存、查询、设置和移除。
/// 所有 Token 的读写必须通过此类的公开方法进行。
/// </summary>
public static class AppAccessTokenManager
{
    private static Dictionary<uint, ulong> _tokens = new();
    private static string _filePath;
    private static readonly object _lock = new();

    /// <summary>
    /// 从指定的 JSON 文件中加载 Token 数据。
    /// 文件格式示例：
    /// {
    ///   "0": "10660652434190618804",
    ///   "766": "6919353661818993790"
    /// }
    /// </summary>
    /// <param name="path">JSON 文件路径</param>
    /// <exception cref="FileNotFoundException">文件不存在时抛出</exception>
    /// <exception cref="InvalidDataException">JSON 解析失败或格式错误时抛出</exception>
    public static void Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Token 文件未找到: {path}");
        }

        var json = File.ReadAllText(path);
        var rawDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        if (rawDict == null)
        {
            throw new InvalidDataException("JSON 解析失败或为空");
        }

        var newTokens = new Dictionary<uint, ulong>();
        foreach (var kvp in rawDict)
        {
            if (!uint.TryParse(kvp.Key, out var appId))
            {
                throw new FormatException($"无效的 appid 键: '{kvp.Key}'");
            }

            if (!ulong.TryParse(kvp.Value, out var tokenValue))
            {
                throw new FormatException($"无效的 token 值: '{kvp.Value}' (appid={appId})");
            }

            newTokens[appId] = tokenValue;
        }

        lock (_lock)
        {
            _tokens = newTokens;
            _filePath = path;
        }
    }

    /// <summary>
    /// 将当前内存中的 Token 数据保存回上次 Load 的文件中。
    /// </summary>
    /// <exception cref="InvalidOperationException">尚未调用 Load 时抛出</exception>
    public static void Save()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            throw new InvalidOperationException("尚未加载任何 Token 文件，无法保存。请先调用 Load 方法。");
        }

        Dictionary<string, string> serializable;
        lock (_lock)
        {
            serializable = new Dictionary<string, string>(_tokens.Count);
            foreach (var kvp in _tokens)
            {
                serializable[kvp.Key.ToString()] = kvp.Value.ToString();
            }
        }

        var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// 尝试获取指定 appid 对应的 Token。
    /// </summary>
    /// <param name="appId">应用 ID</param>
    /// <param name="token">输出参数，成功时返回 Token</param>
    /// <returns>是否成功获取</returns>
    public static bool TryGet(uint appId, out ulong token)
    {
        lock (_lock)
        {
            return _tokens.TryGetValue(appId, out token);
        }
    }

    /// <summary>
    /// 设置或更新指定 appid 的 Token（仅修改内存，不会自动保存到文件）。
    /// </summary>
    /// <param name="appId">应用 ID</param>
    /// <param name="token">新的 Token 值</param>
    public static void Set(uint appId, ulong token)
    {
        lock (_lock)
        {
            _tokens[appId] = token;
        }
    }

    /// <summary>
    /// 移除指定 appid 的 Token（仅修改内存，不会自动保存到文件）。
    /// </summary>
    /// <param name="appId">应用 ID</param>
    /// <returns>是否成功移除（若不存在则返回 false）</returns>
    public static bool Remove(uint appId)
    {
        lock (_lock)
        {
            return _tokens.Remove(appId);
        }
    }
}
