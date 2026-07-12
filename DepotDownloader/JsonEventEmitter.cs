// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;
using System.IO;

namespace DepotDownloader
{
    public static class JsonEventEmitter
    {
        private static readonly ProgressThrottle throttle =
            new(() => DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100));
        private static readonly object gate = new();

        public static TextWriter Out { get; set; } = Console.Out;

        public static void Emit(DownloadEvent evt)
        {
            if (!ContentDownloader.Config.JsonOutput)
            {
                return;
            }

            lock (gate)
            {
                if (evt is ProgressEvent && !throttle.ShouldEmit())
                {
                    return;
                }

                Out.WriteLine(JsonEventWriter.Serialize(evt));
            }
        }
    }
}
