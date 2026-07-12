// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System;

namespace DepotDownloader
{
    public static class JsonOutputFlag
    {
        public static bool Parse(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("-json", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("--json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
