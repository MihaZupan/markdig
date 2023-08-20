// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

#if !NETSTANDARD2_1_OR_GREATER

namespace System;

internal static class StringExtensions
{
    public static bool Contains(this string text, char value) =>
        text.IndexOf(value) >= 0;

    public static bool StartsWith(this ReadOnlySpan<char> span, string value, StringComparison comparisonType) =>
        span.StartsWith(value.AsSpan(), comparisonType);
}

#endif