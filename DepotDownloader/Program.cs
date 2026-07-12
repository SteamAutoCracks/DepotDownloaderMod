// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;

namespace DepotDownloader
{
    static class TokenCFG
    {
        public static bool useAppToken;
        public static bool usePackageToken;
        public static ulong appToken;
        public static ulong packageToken;
    }
    class Program
    {
        private static bool[] consumedArgs;

        private static readonly JsonSerializerOptions InfoJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintVersion();
                PrintUsage();

                if (OperatingSystem.IsWindowsVersionAtLeast(5, 0))
                {
                    PlatformUtilities.VerifyConsoleLaunch();
                }

                return 0;
            }

            Ansi.Init();

            DebugLog.Enabled = false;

            // 获取 exe 所在目录，确保配置文件路径不受外部程序工作目录影响
            var exeDir = AppContext.BaseDirectory;

            AccountSettingsStore.LoadFromFile(Path.Combine(exeDir, "account.config"));

            // 加载 App Access Token 配置文件
            AppAccessTokenManager.Load(Path.Combine(exeDir, "appaccesstokens.json"));

            // 自动加载 depotkeys.json（如果存在），读取逻辑由 DepotKeyStore 内部处理
            DepotKeyStore.LoadFromFile(Path.Combine(exeDir, "depotkeys.json"));

            // 加载 Request Code 配置文件，并初始化 ManifestRequestCodeProvider
            try
            {
                var requestCodePath = Path.Combine(exeDir, "requestcode.json");
                if (File.Exists(requestCodePath))
                {
                    var json = File.ReadAllText(requestCodePath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<RequestCodeConfig>(json);
                    if (config != null)
                    {
                        ManifestRequestCodeProvider.Initialize(config);
                        Console.Error.WriteLine("Loaded request codes from 'requestcode.json'.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Warning: Unable to load requestcode.json: {0}", ex.Message);
            }

            #region Common Options
            // Not using HasParameter because it is case insensitive
            if (args.Length == 1 && (args[0] == "-V" || args[0] == "--version"))
            {
                PrintVersion(true);
                return 0;
            }

            consumedArgs = new bool[args.Length];

            ContentDownloader.Config.JsonOutput = HasParameter(args, "-json") || HasParameter(args, "--json");
            if (ContentDownloader.Config.JsonOutput)
            {
                JsonEventEmitter.Out = Console.Out;
                Console.SetOut(Console.Error);
            }

            if (HasParameter(args, "-debug"))
            {
                PrintVersion(true);

                DebugLog.Enabled = true;
                DebugLog.AddListener((category, message) =>
                {
                    Console.WriteLine("[{0}] {1}", category, message);
                });

                var httpEventListener = new HttpDiagnosticEventListener();
            }

            var username = GetParameter<string>(args, "-username") ?? GetParameter<string>(args, "-user");
            var password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");

            TokenCFG.useAppToken = HasParameter(args, "-apptoken");
            TokenCFG.usePackageToken = HasParameter(args, "-packagetoken");
            TokenCFG.appToken = Convert.ToUInt64(GetParameter<string>(args, "-apptoken"));
            TokenCFG.packageToken = Convert.ToUInt64(GetParameter<string>(args, "-packagetoken"));

            ContentDownloader.Config.RememberPassword = HasParameter(args, "-remember-password");
            ContentDownloader.Config.UseQrCode = HasParameter(args, "-qr");
            ContentDownloader.Config.SkipAppConfirmation = HasParameter(args, "-no-mobile");

            if (username == null)
            {
                if (ContentDownloader.Config.RememberPassword && !ContentDownloader.Config.UseQrCode)
                {
                    Console.WriteLine("Error: -remember-password can not be used without -username or -qr.");
                    return 1;
                }
            }
            else if (ContentDownloader.Config.UseQrCode)
            {
                Console.WriteLine("Error: -qr can not be used with -username.");
                return 1;
            }

            ContentDownloader.Config.DownloadManifestOnly = HasParameter(args, "-manifest-only");

            var cellId = GetParameter(args, "-cellid", -1);
            if (cellId == -1)
            {
                cellId = 0;
            }

            ContentDownloader.Config.CellID = cellId;

            var fileList = GetParameter<string>(args, "-filelist");
            if (fileList != null)
            {
                const string RegexPrefix = "regex:";

                try
                {
                    ContentDownloader.Config.UsingFileList = true;
                    ContentDownloader.Config.FilesToDownload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    ContentDownloader.Config.FilesToDownloadRegex = [];

                    var files = await File.ReadAllLinesAsync(fileList);

                    foreach (var fileEntry in files)
                    {
                        if (string.IsNullOrWhiteSpace(fileEntry))
                        {
                            continue;
                        }

                        if (fileEntry.StartsWith(RegexPrefix))
                        {
                            var rgx = new Regex(fileEntry[RegexPrefix.Length..], RegexOptions.Compiled | RegexOptions.IgnoreCase);
                            ContentDownloader.Config.FilesToDownloadRegex.Add(rgx);
                        }
                        else
                        {
                            ContentDownloader.Config.FilesToDownload.Add(fileEntry.Replace('\\', '/'));
                        }
                    }

                    Console.WriteLine("Using filelist: '{0}'.", fileList);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: Unable to load filelist: {0}", ex);
                }
            }

            var depotKeysList = GetParameter<string>(args, "-depotkeys");

            if (depotKeysList != null)
            {
                try
                {
                    var depotKeysListData = File.ReadAllText(depotKeysList);
                    var lines = depotKeysListData.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    DepotKeyStore.AddAll(lines);

                    Console.WriteLine("Using depot keys from '{0}'.", depotKeysList);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Warning: Unable to load filelist: {0}", ex.ToString());
                }
            }

            ContentDownloader.Config.InstallDirectory = GetParameter<string>(args, "-dir");

            ContentDownloader.Config.VerifyAll = HasParameter(args, "-verify-all") || HasParameter(args, "-verify_all") || HasParameter(args, "-validate");

            if (HasParameter(args, "-use-lancache"))
            {
                await Client.DetectLancacheServerAsync();
                if (Client.UseLancacheServer)
                {
                    Console.WriteLine("Detected Lancache server! Downloads will be directed through the Lancache.");

                    if (!HasParameter(args, "-max-downloads"))
                    {
                        ContentDownloader.Config.MaxDownloads = 25;
                    }
                }
            }

            ContentDownloader.Config.MaxDownloads = GetParameter(args, "-max-downloads", 8);
            ContentDownloader.Config.LoginID = HasParameter(args, "-loginid") ? GetParameter<uint>(args, "-loginid") : null;
            ContentDownloader.Config.ManifestDirectory = GetParameter<string>(args, "-manifest-dir");

            #endregion

            #region App Info Query (read-only, no download)
            var infoIndex = -1;
            for (var i = 0; i < args.Length; i++)
            {
                if (!consumedArgs[i] && args[i].Equals("info", StringComparison.OrdinalIgnoreCase))
                {
                    infoIndex = i;
                    break;
                }
            }

            if (infoIndex >= 0)
            {
                consumedArgs[infoIndex] = true;

                var infoAppId = GetParameter<uint>(args, "--app", ContentDownloader.INVALID_APP_ID);
                if (infoAppId == ContentDownloader.INVALID_APP_ID)
                {
                    infoAppId = GetParameter<uint>(args, "-app", ContentDownloader.INVALID_APP_ID);
                }

                if (infoAppId == ContentDownloader.INVALID_APP_ID)
                {
                    await Console.Error.WriteLineAsync("Error: info subcommand requires --app <id>");
                    return 1;
                }

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        var queryResult = await ContentDownloader.QueryAppInfoAsync(infoAppId);
                        var json = JsonSerializer.Serialize(queryResult, InfoJsonOptions);
                        JsonEventEmitter.Out.WriteLine(json);
                        return queryResult.Success ? 0 : 1;
                    }
                    catch (ContentDownloaderException ex)
                    {
                        await Console.Error.WriteLineAsync(ex.Message);
                        return 1;
                    }
                    catch (OperationCanceledException ex)
                    {
                        await Console.Error.WriteLineAsync(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        await Console.Error.WriteLineAsync($"App info query failed due to an unhandled exception: {e.Message}");
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    await Console.Error.WriteLineAsync("Error: InitializeSteam failed");
                    return 2;
                }
            }
            #endregion

            var appId = GetParameter(args, "-app", ContentDownloader.INVALID_APP_ID);
            if (appId == ContentDownloader.INVALID_APP_ID)
            {
                Console.WriteLine("Error: -app not specified!");
                return 1;
            }

            var pubFile = GetParameter(args, "-pubfile", ContentDownloader.INVALID_MANIFEST_ID);
            var ugcId = GetParameter(args, "-ugc", ContentDownloader.INVALID_MANIFEST_ID);
            if (pubFile != ContentDownloader.INVALID_MANIFEST_ID)
            {
                #region Pubfile Downloading

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader.DownloadPubfileAsync(appId, pubFile).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        Console.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    Console.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }
            else if (ugcId != ContentDownloader.INVALID_MANIFEST_ID)
            {
                #region UGC Downloading

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader.DownloadUGCAsync(appId, ugcId).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        Console.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    Console.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }
            else
            {
                #region App downloading

                var branch = GetParameter<string>(args, "-branch") ?? GetParameter<string>(args, "-beta") ?? ContentDownloader.DEFAULT_BRANCH;
                ContentDownloader.Config.BetaPassword = GetParameter<string>(args, "-branchpassword") ?? GetParameter<string>(args, "-betapassword");

                if (!string.IsNullOrEmpty(ContentDownloader.Config.BetaPassword) && string.IsNullOrEmpty(branch))
                {
                    Console.WriteLine("Error: Cannot specify -branchpassword when -branch is not specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllPlatforms = HasParameter(args, "-all-platforms");

                var os = GetParameter<string>(args, "-os");

                if (ContentDownloader.Config.DownloadAllPlatforms && !string.IsNullOrEmpty(os))
                {
                    Console.WriteLine("Error: Cannot specify -os when -all-platforms is specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllArchs = HasParameter(args, "-all-archs");

                var arch = GetParameter<string>(args, "-osarch");

                if (ContentDownloader.Config.DownloadAllArchs && !string.IsNullOrEmpty(arch))
                {
                    Console.WriteLine("Error: Cannot specify -osarch when -all-archs is specified.");
                    return 1;
                }

                ContentDownloader.Config.DownloadAllLanguages = HasParameter(args, "-all-languages");
                var language = GetParameter<string>(args, "-language");

                if (ContentDownloader.Config.DownloadAllLanguages && !string.IsNullOrEmpty(language))
                {
                    Console.WriteLine("Error: Cannot specify -language when -all-languages is specified.");
                    return 1;
                }

                var lv = HasParameter(args, "-lowviolence");

                var depotManifestIds = new List<(uint, ulong)>();
                var isUGC = false;

                var depotRefs = GetParameterList<string>(args, "-depot");
                foreach (var depotRef in depotRefs)
                {
                    var colonIdx = depotRef.IndexOf(':');
                    if (colonIdx > 0 && ulong.TryParse(depotRef.AsSpan(colonIdx + 1), out var explicitManifestId))
                    {
                        if (uint.TryParse(depotRef.AsSpan(0, colonIdx), out var refDepotId))
                        {
                            depotManifestIds.Add((refDepotId, explicitManifestId));
                        }
                    }
                    else
                    {
                        if (uint.TryParse(depotRef, out var depotId))
                        {
                            depotManifestIds.Add((depotId, ContentDownloader.INVALID_MANIFEST_ID));
                        }
                    }
                }

                PrintUnconsumedArgs(args);

                if (InitializeSteam(username, password))
                {
                    try
                    {
                        await ContentDownloader.DownloadAppAsync(appId, depotManifestIds, branch, os, arch, language, lv, isUGC).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is ContentDownloaderException
                        || ex is OperationCanceledException)
                    {
                        Console.WriteLine(ex.Message);
                        return 1;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Download failed to due to an unhandled exception: {0}", e.Message);
                        throw;
                    }
                    finally
                    {
                        ContentDownloader.ShutdownSteam3();
                    }
                }
                else
                {
                    Console.WriteLine("Error: InitializeSteam failed");
                    return 1;
                }

                #endregion
            }

            return 0;
        }

        static bool InitializeSteam(string username, string password)
        {
            if (!ContentDownloader.Config.UseQrCode)
            {
                if (username != null && password == null && (!ContentDownloader.Config.RememberPassword || !AccountSettingsStore.Instance.LoginTokens.ContainsKey(username)))
                {
                    if (AccountSettingsStore.Instance.LoginTokens.ContainsKey(username))
                    {
                        Console.WriteLine($"Account \"{username}\" has stored credentials. Did you forget to specify -remember-password?");
                    }

                    do
                    {
                        Console.Write("Enter account password for \"{0}\": ", username);
                        if (Console.IsInputRedirected)
                        {
                            password = Console.ReadLine();
                        }
                        else
                        {
                            password = Util.ReadPassword();
                        }

                        Console.WriteLine();
                    } while (string.Empty == password);
                }
                else if (username == null)
                {
                    Console.WriteLine("No username given. Using anonymous account with dedicated server subscription.");
                }
            }

            if (!string.IsNullOrEmpty(password))
            {
                const int MAX_PASSWORD_SIZE = 64;

                if (password.Length > MAX_PASSWORD_SIZE)
                {
                    Console.Error.WriteLine($"Warning: Password is longer than {MAX_PASSWORD_SIZE} characters, which is not supported by Steam.");
                }

                if (!password.All(char.IsAscii))
                {
                    Console.Error.WriteLine("Warning: Password contains non-ASCII characters, which is not supported by Steam.");
                }
            }

            return ContentDownloader.InitializeSteam3(username, password);
        }

        static int IndexOfParam(string[] args, string param)
        {
            for (var x = 0; x < args.Length; ++x)
            {
                if (args[x].Equals(param, StringComparison.OrdinalIgnoreCase))
                {
                    consumedArgs[x] = true;
                    return x;
                }
            }

            return -1;
        }

        static bool HasParameter(string[] args, string param)
        {
            return IndexOfParam(args, param) > -1;
        }

        static T GetParameter<T>(string[] args, string param, T defaultValue = default)
        {
            var index = IndexOfParam(args, param);

            if (index == -1 || index == (args.Length - 1))
                return defaultValue;

            var strParam = args[index + 1];

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (converter != null)
            {
                consumedArgs[index + 1] = true;
                return (T)converter.ConvertFromString(strParam);
            }

            return default;
        }

        static List<T> GetParameterList<T>(string[] args, string param)
        {
            var list = new List<T>();
            var index = IndexOfParam(args, param);

            if (index == -1 || index == (args.Length - 1))
                return list;

            index++;

            while (index < args.Length)
            {
                var strParam = args[index];

                if (strParam[0] == '-') break;

                var converter = TypeDescriptor.GetConverter(typeof(T));
                if (converter != null)
                {
                    consumedArgs[index] = true;
                    list.Add((T)converter.ConvertFromString(strParam));
                }

                index++;
            }

            return list;
        }

        static void PrintUnconsumedArgs(string[] args)
        {
            var printError = false;

            for (var index = 0; index < consumedArgs.Length; index++)
            {
                if (!consumedArgs[index])
                {
                    printError = true;
                    Console.Error.WriteLine($"Argument #{index + 1} {args[index]} was not used.");
                }
            }

            if (printError)
            {
                Console.Error.WriteLine("Make sure you specified the arguments correctly. Check --help for correct arguments.");
                Console.Error.WriteLine();
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: querying app info (read-only)");
            Console.WriteLine("       depotdownloader info --app <id> [-username <user> [-password <pass>]]");
            Console.WriteLine("       Outputs JSON with app name, Windows launch exe path, and DLC list.");
            Console.WriteLine();
            Console.WriteLine("Usage: downloading one or all depots for an app:");
            Console.WriteLine("       depotdownloader -app <id> [-depot <id>[:<manifestGid>]]");
            Console.WriteLine("                       [-username <username> [-password <password>]] [other options]");
            Console.WriteLine();
            Console.WriteLine("Usage: downloading a workshop item using pubfile id");
            Console.WriteLine("       depotdownloader -app <id> -pubfile <id> [-username <username> [-password <password>]]");
            Console.WriteLine("Usage: downloading a workshop item using ugc id");
            Console.WriteLine("       depotdownloader -app <id> -ugc <id> [-username <username> [-password <password>]]");
            Console.WriteLine();
            Console.WriteLine("Parameters:");
            Console.WriteLine("  -app <#>                 - the AppID to download.");
            Console.WriteLine("  -depot <id>[:<manifestGid>] - the DepotID to download. Append :<manifestGid> to use a specific manifest.");
            Console.WriteLine("  -manifest-dir <dir>     - directory containing {depotId}_{manifestGid}.manifest files to load locally (skips CDN manifest download). When used with -app, auto-discovers all app and DLC depots.");
            Console.WriteLine($"  -branch <branchname>    - download from specified branch if available (default: {ContentDownloader.DEFAULT_BRANCH}).");
            Console.WriteLine("  -branchpassword <pass>   - branch password if applicable.");
            Console.WriteLine("  -all-platforms           - downloads all platform-specific depots when -app is used.");
            Console.WriteLine("  -all-archs               - download all architecture-specific depots when -app is used.");
            Console.WriteLine("  -os <os>                 - the operating system for which to download the game (windows, macos or linux, default: OS the program is currently running on)");
            Console.WriteLine("  -osarch <arch>           - the architecture for which to download the game (32 or 64, default: the host's architecture)");
            Console.WriteLine("  -all-languages           - download all language-specific depots when -app is used.");
            Console.WriteLine("  -language <lang>         - the language for which to download the game (default: english)");
            Console.WriteLine("  -lowviolence             - download low violence depots when -app is used.");
            Console.WriteLine();
            Console.WriteLine("  -ugc <#>                 - the UGC ID to download.");
            Console.WriteLine("  -pubfile <#>             - the PublishedFileId to download. (Will automatically resolve to UGC id)");
            Console.WriteLine();
            Console.WriteLine("  -username <user>         - the username of the account to login to for restricted content.");
            Console.WriteLine("  -password <pass>         - the password of the account to login to for restricted content.");
            Console.WriteLine("  -remember-password       - if set, remember the password for subsequent logins of this user.");
            Console.WriteLine("                             use -username <username> -remember-password as login credentials.");
            Console.WriteLine("  -qr                      - display a login QR code to be scanned with the Steam mobile app");
            Console.WriteLine("  -no-mobile               - prefer entering a 2FA code instead of prompting to accept in the Steam mobile app");
            Console.WriteLine();
            Console.WriteLine("  -dir <installdir>        - the directory in which to place downloaded files.");
            Console.WriteLine("  -filelist <file.txt>     - the name of a local file that contains a list of files to download (from the manifest).");
            Console.WriteLine("                             prefix file path with `regex:` if you want to match with regex. each file path should be on their own line.");
            Console.WriteLine();
            Console.WriteLine("  -validate                - include checksum verification of files already downloaded");
            Console.WriteLine("  -manifest-only           - downloads a human readable manifest for any depots that would be downloaded.");
            Console.WriteLine("  -cellid <#>              - the overridden CellID of the content server to download from.");
            Console.WriteLine("  -max-downloads <#>       - maximum number of chunks to download concurrently. (default: 8).");
            Console.WriteLine("  -loginid <#>             - a unique 32-bit integer Steam LogonID in decimal, required if running multiple instances of DepotDownloader concurrently.");
            Console.WriteLine("  -use-lancache            - forces downloads over the local network via a Lancache instance.");
            Console.WriteLine();
            Console.WriteLine("  -debug                   - enable verbose debug logging.");
            Console.WriteLine("  -V or --version          - print version and runtime.");
            Console.WriteLine("  -depotkeys <file>        - a list of depot keys to use ('depotID;hexKey' per line).");
            Console.WriteLine("  -apptoken <#>            - Use Specified App Access Token.");
            Console.WriteLine("  -packagetoken <#>        - Use Specified Package Access Token.");
        }

        static void PrintVersion(bool printExtra = false)
        {
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            Console.WriteLine($"DepotDownloader v{version}");

            if (!printExtra)
            {
                return;
            }

            Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}");
        }
    }
}
