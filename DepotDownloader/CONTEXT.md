# DepotDownloaderMod

A Steam content downloading utility (.NET 9 CLI). Authenticates to Steam, resolves app/depot metadata, fetches manifests, decrypts depot content, and writes files to disk.

## Language

**Depot**:
A numbered container of game content, identified by a `uint` DepotId. May have its own manifest or inherit one from a parent app via `depotfromapp`. Requires a depot key for decryption and has configurable install directories. Depots can be public, private/beta, or workshop-specific.
_Avoid_: package, game files, container

**Manifest**:
A versioned description of a depot's file and chunk layout, identified by a `ulong` ManifestId. Fetched from Steam and used to drive the download. Can be encrypted or plaintext, and exists in both Steam format and local ProtoManifest cache format.
_Avoid_: file list, metadata

**DepotKey**:
An AES `byte[]` key for a depot, required to decrypt chunk payloads and encrypted manifest filenames. Managed via DepotKeyStore with command-line, JSON, and runtime acquisition paths.
_Avoid_: decryption key, depot secret

**AppAccessToken**:
A `ulong` token required to access restricted app metadata on Steam. Distinct from a DepotKey — it gates app info, not depot content. Uses hierarchical lookup: command-line → JSON cache → dynamic Steam fetch → fallback.
_Avoid_: app token, access token

**ContentDownloader**:
The static orchestration class that manages the entire download lifecycle, including manifest fetching, chunk downloading, file validation, and delta updates. Orchestrates multi-depot downloads with intelligent resume capability.
_Avoid_: downloader, download manager

**Steam3Session**:
The authenticated Steam connection wrapper handling login, token management, and API calls. Maintains connection state with automatic recovery and manages multiple token types (app, package, CDN auth).
_Avoid_: steam client, steam connection

**CDN**:
Steam's content delivery network, accessed through a CDNClientPool that handles server selection, load balancing, and failover. Manages CDN auth tokens and server penalties for retry logic.
_Avoid_: download server, mirror

**Download Session**:
One process invocation of DepotDownloaderMod that serves a single download request across multiple depots. Handles cancellation, progress reporting, and maintains configuration state across the download lifecycle. When serving external callers, outputs detailed progress information to stdout in structured format.
_Avoid_: daemon, service instance

**External Caller**:
A single process that invokes DepotDownloaderMod programmatically to initiate downloads. Receives real-time progress reports through stdout and provides configuration parameters via command-line arguments or environment variables. Distinguished from manual CLI usage by its expectation of machine-readable output and programmatic integration. The system assumes only one external caller at a time.
_Avoid_: client, consumer, caller

**Progress Stream**:
The structured output mechanism that sends download progress information to stdout during execution. Provides hierarchical progress data including overall session status, individual depot progress, file-level operations, chunk downloads, and validation results. Each event contains a unique SessionId for tracking. Designed to be machine-parseable for external callers while remaining human-readable for debugging. Continues output even if the external caller disconnects and automatically resumes stdout connection when reconnected.
_Avoid_: output, log, status report

**Fire-and-Forget Session**:
A download session initiated by an external caller that continues execution independently even if the external caller disconnects or crashes. The session maintains progress reporting until completion, allowing the caller to reconnect later to retrieve results.
_Avoid_: detached session, background session

**AppInfoQuery**:
A read-only query operation that fetches Steam AppInfo metadata for a single AppId and outputs it as structured JSON. Returns the app name, the Windows executable launch path (from config/launch), and a flat list of all DLCs (their AppIds and names) reachable by recursively traversing extended/listofdlc. Never modifies any local state, downloads files, or touches the CDN.
_Avoid_: app info scan, metadata download, app lookup

**DLCInfo**:
A single DLC entry in an AppInfoQuery result, containing the DLC's AppId and its display name. Exists only as a child node of the query result. No nested children — DLCs of DLCs are flattened to the top-level list.
_Avoid_: sub-app, addon, expansion

**LaunchConfig**:
The Windows executable launch path extracted from an app's config/launch section. Located by finding the first launch entry whose oslist contains "windows" and reading its executable field. May be empty if no Windows launch configuration exists.
_Avoid_: exe path, startup executable, launch exe

**ManifestDirectory**:
A directory path specified via `-manifest-dir` containing `{depotId}_{manifestGid}.manifest` files. When provided, the system scans this directory, matches depot IDs against the app's depots (and DLC depots when `-app` is specified), loads the matched files as manifest data, and drives downloads from them — bypassing CDN manifest download. Missing files for a depot are silently skipped.
_Avoid_: manifest folder, local manifests

**ManifestRef**:
A `{depotId}:{manifestGid}` pair specified directly on the command line via `-depot <id>:<gid>` syntax. Used to bypass Steam's manifest GID resolution: the system uses the exact GID to request a manifest request code and downloads the manifest from CDN. Supports multiple `-depot` arguments for games with multiple depots. Replaces the removed `-manifest` parameter.
_Avoid_: manifest override, manual manifest