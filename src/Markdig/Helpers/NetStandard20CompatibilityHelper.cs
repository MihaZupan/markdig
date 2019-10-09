#if NETSTANDARD2_0
using System;
using System.IO;

namespace Markdig.Helpers
{
    internal static class NetStandard20CompatibilityHelper
    {
        public static void Write(this TextWriter writer, ReadOnlySpan<char> span)
        {
            writer.Write(span.ToString());
        }
    }
}
#endif