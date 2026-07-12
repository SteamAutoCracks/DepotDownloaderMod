// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;

namespace DepotDownloader
{
    public sealed class ProgressThrottle
    {
        private readonly Func<DateTimeOffset> clock;
        private readonly TimeSpan interval;
        private DateTimeOffset lastEmit;
        private bool everEmitted;

        public ProgressThrottle(Func<DateTimeOffset> clock, TimeSpan interval)
        {
            this.clock = clock;
            this.interval = interval;
        }

        public bool ShouldEmit()
        {
            var now = clock();
            if (!everEmitted || now - lastEmit >= interval)
            {
                lastEmit = now;
                everEmitted = true;
                return true;
            }

            return false;
        }
    }
}
