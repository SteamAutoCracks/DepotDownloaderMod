// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DepotDownloader
{
    public abstract record DownloadEvent([property: JsonPropertyOrder(-1)] string Type);

    public sealed record StartDepot(uint Id, ulong ManifestId, string Branch, ulong TotalBytes);

    public sealed record StartEvent(
        uint AppId,
        IReadOnlyList<StartDepot> Depots,
        ulong TotalBytes) : DownloadEvent("start");

    public sealed record DepotStartEvent(uint DepotId) : DownloadEvent("depot_start");

    public sealed record ProgressEvent(
        uint DepotId,
        ulong DownloadedBytes,
        ulong TotalBytes,
        double Percent) : DownloadEvent("progress");

    public sealed record DepotDoneEvent(
        uint DepotId,
        ulong CompressedBytes,
        ulong UncompressedBytes) : DownloadEvent("depot_done");

    public sealed record ErrorEvent(
        uint DepotId,
        string Stage,
        string Message) : DownloadEvent("error");

    public sealed record DoneEvent(
        ulong TotalBytes,
        int DepotCount,
        bool Ok) : DownloadEvent("done");
}
