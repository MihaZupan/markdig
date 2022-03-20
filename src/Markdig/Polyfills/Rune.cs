// Based on code at located at https://github.com/dotnet/runtime/blob/79ae74f5ca5c8a6fe3a48935e85bd7374959c570/src/libraries/System.Private.CoreLib/src/System/Diagnostics/CodeAnalysis/NullableAttributes.cs

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Markdig.Helpers;
using System.Runtime.CompilerServices;

namespace System.Text
{
#if !NETCOREAPP3_1_OR_GREATER
    public readonly struct Rune
    {
        private const char HighSurrogateStart = '\ud800';
        private const char LowSurrogateStart = '\udc00';
        private const int HighSurrogateRange = 0x3FF;
        private const uint ReplacementCharValue = 0xFFFD;

        private readonly uint _value;

        public bool IsAscii => _value <= 0x7Fu;

        public int Value => (int)_value;

        public static Rune ReplacementChar => new Rune(ReplacementCharValue);

        private Rune(uint value) => _value = value;

        public static bool TryCreate(char ch, out Rune result)
        {
            uint extendedValue = ch;
            if (!CharHelper.IsInInclusiveRange(extendedValue, 0xD800U, 0xDFFFU))
            {
                result = new Rune(extendedValue);
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        public static bool TryCreate(char highSurrogate, char lowSurrogate, out Rune result)
        {
            // First, extend both to 32 bits, then calculate the offset of
            // each candidate surrogate char from the start of its range.

            uint highSurrogateOffset = (uint)highSurrogate - HighSurrogateStart;
            uint lowSurrogateOffset = (uint)lowSurrogate - LowSurrogateStart;

            // This is a single comparison which allows us to check both for validity at once since
            // both the high surrogate range and the low surrogate range are the same length.
            // If the comparison fails, we call to a helper method to throw the correct exception message.

            if ((highSurrogateOffset | lowSurrogateOffset) <= HighSurrogateRange)
            {
                // The 0x40u << 10 below is to account for uuuuu = wwww + 1 in the surrogate encoding.
                result = new Rune((highSurrogateOffset << 10) + ((uint)lowSurrogate - LowSurrogateStart) + (0x40u << 10));
                return true;
            }
            else
            {
                // Didn't have a high surrogate followed by a low surrogate.
                result = default;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEncodeToUtf8(Span<byte> destination, out int bytesWritten)
        {
            return TryEncodeToUtf8(this, destination, out bytesWritten);
        }

        private static bool TryEncodeToUtf8(Rune value, Span<byte> destination, out int bytesWritten)
        {
            // The bit patterns below come from the Unicode Standard, Table 3-6.

            if (!destination.IsEmpty)
            {
                if (value.IsAscii)
                {
                    destination[0] = (byte)value._value;
                    bytesWritten = 1;
                    return true;
                }

                if (1 < (uint)destination.Length)
                {
                    if (value.Value <= 0x7FFu)
                    {
                        // Scalar 00000yyy yyxxxxxx -> bytes [ 110yyyyy 10xxxxxx ]
                        destination[0] = (byte)((value._value + (0b110u << 11)) >> 6);
                        destination[1] = (byte)((value._value & 0x3Fu) + 0x80u);
                        bytesWritten = 2;
                        return true;
                    }

                    if (2 < (uint)destination.Length)
                    {
                        if (value.Value <= 0xFFFFu)
                        {
                            // Scalar zzzzyyyy yyxxxxxx -> bytes [ 1110zzzz 10yyyyyy 10xxxxxx ]
                            destination[0] = (byte)((value._value + (0b1110 << 16)) >> 12);
                            destination[1] = (byte)(((value._value & (0x3Fu << 6)) >> 6) + 0x80u);
                            destination[2] = (byte)((value._value & 0x3Fu) + 0x80u);
                            bytesWritten = 3;
                            return true;
                        }

                        if (3 < (uint)destination.Length)
                        {
                            // Scalar 000uuuuu zzzzyyyy yyxxxxxx -> bytes [ 11110uuu 10uuzzzz 10yyyyyy 10xxxxxx ]
                            destination[0] = (byte)((value._value + (0b11110 << 21)) >> 18);
                            destination[1] = (byte)(((value._value & (0x3Fu << 12)) >> 12) + 0x80u);
                            destination[2] = (byte)(((value._value & (0x3Fu << 6)) >> 6) + 0x80u);
                            destination[3] = (byte)((value._value & 0x3Fu) + 0x80u);
                            bytesWritten = 4;
                            return true;
                        }
                    }
                }
            }

            // Destination buffer not large enough

            bytesWritten = default;
            return false;
        }
    }
#endif
}
