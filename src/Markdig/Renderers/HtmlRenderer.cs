// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Markdig.Helpers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
#if NETCOREAPP3_1_OR_GREATER
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
#endif

namespace Markdig.Renderers
{
    /// <summary>
    /// Default HTML renderer for a Markdown <see cref="MarkdownDocument"/> object.
    /// </summary>
    /// <seealso cref="TextRendererBase{HtmlRenderer}" />
    public class HtmlRenderer : TextRendererBase<HtmlRenderer>
    {
        private static readonly IdnMapping s_idnMapping = new();
        private static readonly char[] s_writeEscapeIndexOfAnyChars = new[] { '<', '>', '&', '"' };

        /// <summary>
        /// Initializes a new instance of the <see cref="HtmlRenderer"/> class.
        /// </summary>
        /// <param name="writer">The writer.</param>
        public HtmlRenderer(TextWriter writer) : base(writer)
        {
            // Default block renderers
            ObjectRenderers.Add(new CodeBlockRenderer());
            ObjectRenderers.Add(new ListRenderer());
            ObjectRenderers.Add(new HeadingRenderer());
            ObjectRenderers.Add(new HtmlBlockRenderer());
            ObjectRenderers.Add(new ParagraphRenderer());
            ObjectRenderers.Add(new QuoteBlockRenderer());
            ObjectRenderers.Add(new ThematicBreakRenderer());

            // Default inline renderers
            ObjectRenderers.Add(new AutolinkInlineRenderer());
            ObjectRenderers.Add(new CodeInlineRenderer());
            ObjectRenderers.Add(new DelimiterInlineRenderer());
            ObjectRenderers.Add(new EmphasisInlineRenderer());
            ObjectRenderers.Add(new LineBreakInlineRenderer());
            ObjectRenderers.Add(new HtmlInlineRenderer());
            ObjectRenderers.Add(new HtmlEntityInlineRenderer());
            ObjectRenderers.Add(new LinkInlineRenderer());
            ObjectRenderers.Add(new LiteralInlineRenderer());

            EnableHtmlForBlock = true;
            EnableHtmlForInline = true;
            EnableHtmlEscape = true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to output HTML tags when rendering. See remarks.
        /// </summary>
        /// <remarks>
        /// This is used by some renderers to disable HTML tags when rendering some inline elements (for image links).
        /// </remarks>
        public bool EnableHtmlForInline { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to output HTML tags when rendering. See remarks.
        /// </summary>
        /// <remarks>
        /// This is used by some renderers to disable HTML tags when rendering some block elements (for image links).
        /// </remarks>
        public bool EnableHtmlForBlock { get; set; }

        public bool EnableHtmlEscape { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use implicit paragraph (optional &lt;p&gt;)
        /// </summary>
        public bool ImplicitParagraph { get; set; }

        public bool UseNonAsciiNoEscape { get; set; }

        /// <summary>
        /// Gets a value to use as the base url for all relative links
        /// </summary>
        public Uri? BaseUrl { get; set; }

        /// <summary>
        /// Allows links to be rewritten
        /// </summary>
        public Func<string, string>? LinkRewriter { get; set; }

        /// <summary>
        /// Writes the content escaped for HTML.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <returns>This instance</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HtmlRenderer WriteEscape(string? content)
        {
            WriteEscape(content.AsSpan());
            return this;
        }

        /// <summary>
        /// Writes the content escaped for HTML.
        /// </summary>
        /// <param name="slice">The slice.</param>
        /// <param name="softEscape">Only escape &lt; and &amp;</param>
        /// <returns>This instance</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HtmlRenderer WriteEscape(ref StringSlice slice, bool softEscape = false)
        {
            WriteEscape(slice.AsSpan(), softEscape);
            return this;
        }

        /// <summary>
        /// Writes the content escaped for HTML.
        /// </summary>
        /// <param name="slice">The slice.</param>
        /// <param name="softEscape">Only escape &lt; and &amp;</param>
        /// <returns>This instance</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HtmlRenderer WriteEscape(StringSlice slice, bool softEscape = false)
        {
            WriteEscape(slice.AsSpan(), softEscape);
            return this;
        }

        /// <summary>
        /// Writes the content escaped for HTML.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The length.</param>
        /// <param name="softEscape">Only escape &lt; and &amp;</param>
        /// <returns>This instance</returns>
        public HtmlRenderer WriteEscape(string content, int offset, int length, bool softEscape = false)
        {
            WriteEscape(content.AsSpan(offset, length), softEscape);
            return this;
        }

        /// <summary>
        /// Writes the content escaped for HTML.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <param name="softEscape">Only escape &lt; and &amp;</param>
        public void WriteEscape(ReadOnlySpan<char> content, bool softEscape = false)
        {
            if (!content.IsEmpty)
            {
                int nextIndex = softEscape
                    ? content.IndexOfAny('<', '&')
                    : content.IndexOfAny(s_writeEscapeIndexOfAnyChars);

                if (nextIndex == -1)
                {
                    Write(content);
                }
                else
                {
                    WriteEscapeSlow(content, nextIndex, softEscape);
                }
            }
        }

        private void WriteEscapeSlow(ReadOnlySpan<char> content, int nextIndex, bool softEscape = false)
        {
            WriteIndent();

            do
            {
                WriteRaw(content.Slice(0, nextIndex));

                char c = content[nextIndex];
                content = content.Slice(nextIndex + 1);

                switch (c)
                {
                    case '<':
                        if (EnableHtmlEscape)
                        {
                            WriteRaw("&lt;");
                        }
                        break;

                    case '>':
                        if (EnableHtmlEscape)
                        {
                            WriteRaw("&gt;");
                        }
                        break;

                    case '&':
                        if (EnableHtmlEscape)
                        {
                            WriteRaw("&amp;");
                        }
                        break;

                    case '"':
                        if (EnableHtmlEscape)
                        {
                            WriteRaw("&quot;");
                        }
                        break;
                }

                nextIndex = softEscape
                    ? content.IndexOfAny('<', '&')
                    : content.IndexOfAny(s_writeEscapeIndexOfAnyChars);
            }
            while (nextIndex >= 0);

            WriteRaw(content);
        }

        /// <summary>
        /// Writes the URL escaped for HTML.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <returns>This instance</returns>
        public HtmlRenderer WriteEscapeUrl(string? content)
        {
            if (content is null)
                return this;

            if (BaseUrl is not null
                // According to https://github.com/dotnet/runtime/issues/22718
                // this is the proper cross-platform way to check whether a uri is absolute or not:
                && Uri.TryCreate(content, UriKind.RelativeOrAbsolute, out var contentUri) && !contentUri.IsAbsoluteUri)
            {
                content = new Uri(BaseUrl, contentUri).AbsoluteUri;
            }

            if (LinkRewriter is not null)
            {
                content = LinkRewriter(content);
            }

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == ':')
                {
                    if ((uint)(i + 2) < (uint)content.Length &&
                        (uint)(i + 1) < (uint)content.Length &&
                        content[i + 1] == '/' &&
                        content[i + 2] == '/')
                    {
                        int schemeLength = i + 3; // Skip "://"

                        (int domainNameLength, bool idnaEncodeDomain) = ScanDomainName(content.AsSpan(schemeLength));

                        if (idnaEncodeDomain)
                        {
                            return WriteNonAsciiDomainName(content, schemeLength, domainNameLength);
                        }
                    }

                    break;
                }
            }

            WriteIndent();
            WriteEscapeUrlCore(content, 0, content.Length);
            return this;

            HtmlRenderer WriteNonAsciiDomainName(string content, int schemeLength, int domainNameLength)
            {
                Debug.Assert(content is not null && schemeLength > 3 && domainNameLength >= 0 && schemeLength + domainNameLength <= content.Length);

                string? domainName = null;
                try
                {
                    domainName = s_idnMapping.GetAscii(content, schemeLength, domainNameLength);
                    domainNameLength += schemeLength;
                }
                catch
                {
                    // Not a valid IDN, fallback to non-punycode encoding
                }

                WriteIndent();

                if (domainName is not null)
                {
                    WriteEscapeUrlCore(content, 0, schemeLength);
                    WriteEscapeUrlCore(domainName, 0, domainName.Length);
                    WriteEscapeUrlCore(content, domainNameLength, content.Length - domainNameLength);
                }
                else
                {
                    WriteEscapeUrlCore(content, 0, content.Length);
                }

                return this;
            }
        }

        private static (int DomainNameLength, bool ContainsNonAscii) ScanDomainName(ReadOnlySpan<char> domainName)
        {
            int domainNameLength = domainName.IndexOfAny('/', '?', ':');
            if (domainNameLength < 0)
            {
                domainNameLength = domainName.Length;
            }
            else
            {
                domainName = domainName.Slice(0, domainNameLength);
            }

            bool containsNonAscii = false;
            foreach (char c in domainName)
            {
                if (c > 127)
                {
                    containsNonAscii = true;
                    int fragment = domainName.IndexOf('#');
                    if (fragment >= 0)
                    {
                        domainNameLength = fragment;
                    }
                    break;
                }
            }

            return (domainNameLength, containsNonAscii);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteEscapeUrlCore(string content, int offset, int length)
        {
            Debug.Assert(content is not null && offset >= 0 && length >= 0 && length <= content.Length);

#if NETCOREAPP3_1_OR_GREATER
            if (Sse3.IsSupported && BitConverter.IsLittleEndian && length >= 2 * Vector128<short>.Count)
            {
                WriteEscapeUrlCoreSse3(
                    ref Unsafe.Add(ref Unsafe.AsRef(in content.GetPinnableReference()), offset),
                    ref Unsafe.Add(ref Unsafe.AsRef(in content.GetPinnableReference()), length));
            }
            else
            {
                WriteEscapeUrlCorePortable(MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.Add(ref Unsafe.AsRef(in content.GetPinnableReference()), offset),
                    length));
            }
#else
            WriteEscapeUrlCorePortable(content.AsSpan(offset, length));
#endif
        }

#if NETCOREAPP3_1_OR_GREATER
        private void WriteEscapeUrlCoreSse3(ref char textStartRef, ref char textEndRef)
        {
            Debug.Assert((nint)Unsafe.ByteOffset(ref textStartRef, ref textEndRef) >= 2 * Vector128<byte>.Count);

            ref char previousTextRef = ref textStartRef;

            do
            {
                // This bitmap is calculated based on HtmlHelper.EscapeUrlsForAscii.
                // It matches on any character that has to be percent-encoded.
                const ulong Bitmap_0_3 = 506376794573046599UL;
                const ulong Bitmap_4_7 = 9487856997555700483UL;

                // See CharacterMap.IndexOfOpeningCharacter for an explaination of this algorithm
                Vector128<byte> input = Sse2.PackUnsignedSaturate(
                    Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<char, byte>(ref textStartRef)),
                    Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<char, byte>(ref Unsafe.Add(ref textStartRef, Vector128<short>.Count))));

                Vector128<byte> higherNibbles = Sse2.And(Sse2.ShiftRightLogical(input.AsUInt16(), 4).AsByte(), Vector128.Create((byte)0xF));
                Vector128<byte> bitsets = Ssse3.Shuffle(Vector128.Create(Bitmap_0_3, Bitmap_4_7).AsByte(), input);
                Vector128<byte> bitmask = Ssse3.Shuffle(Vector128.Create(0x8040201008040201).AsByte(), higherNibbles);
                Vector128<byte> nonAsciiResult = Sse2.CompareLessThan(input.AsSByte(), Vector128<sbyte>.Zero).AsByte();
                Vector128<byte> asciiResult = Sse2.And(bitsets, bitmask);
                Vector128<byte> result = Sse2.Or(nonAsciiResult, asciiResult);

                if (result.Equals(Vector128<byte>.Zero))
                {
                    AssertNothingToEscape(ref textStartRef, 2 * Vector128<short>.Count);
                    textStartRef = ref Unsafe.Add(ref textStartRef, 2 * Vector128<short>.Count);
                }
                else
                {
                    int minIndex = BitOperations.TrailingZeroCount((uint)~Sse2.MoveMask(Sse2.CompareEqual(result, Vector128<byte>.Zero)));

                    AssertNothingToEscape(ref textStartRef, minIndex);
                    textStartRef = ref Unsafe.Add(ref textStartRef, minIndex);
                    Debug.Assert(HtmlHelper.EscapeUrlCharacter(textStartRef) is not null || textStartRef > 127);

                    WriteRaw(MemoryMarshal.CreateReadOnlySpan(ref previousTextRef, (int)Unsafe.ByteOffset(ref previousTextRef, ref textStartRef) >> 1));

                    Debug.Assert(!Unsafe.AreSame(ref textStartRef, ref textEndRef));
                    do
                    {
                        char c = textStartRef;

                        string?[] asciiEscapeTable = HtmlHelper.EscapeUrlsForAscii;
                        if (c < (uint)asciiEscapeTable.Length)
                        {
                            var escape = asciiEscapeTable[c];
                            if (escape is null)
                            {
                                break;
                            }
                            else
                            {
                                WriteRaw(escape);
                                textStartRef = ref Unsafe.Add(ref textStartRef, 1);
                            }
                        }
                        else if (UseNonAsciiNoEscape)
                        {
                            WriteRaw(c);
                            textStartRef = ref Unsafe.Add(ref textStartRef, 1);
                        }
                        else if (CharHelper.IsHighSurrogate(c) && (nint)Unsafe.ByteOffset(ref textStartRef, ref textEndRef) > 2)
                        {
                            EscapeSurrogatePair(c, Unsafe.Add(ref textStartRef, 1));
                            textStartRef = ref Unsafe.Add(ref textStartRef, 2);
                        }
                        else
                        {
                            EscapeNonAscii(c);
                            textStartRef = ref Unsafe.Add(ref textStartRef, 1);
                        }
                    }
                    while (!Unsafe.AreSame(ref textStartRef, ref textEndRef));

                    previousTextRef = ref textStartRef;
                }
            }
            while ((nint)Unsafe.ByteOffset(ref textStartRef, ref textEndRef) >= 2 * Vector128<byte>.Count);

            int previousPosition = (int)Unsafe.ByteOffset(ref previousTextRef, ref textStartRef) >> 1;
            int length = (int)Unsafe.ByteOffset(ref previousTextRef, ref textEndRef) >> 1;
            WriteEscapeUrlCorePortable(MemoryMarshal.CreateReadOnlySpan(ref previousTextRef, length), previousPosition);

            [Conditional("DEBUG")]
            static void AssertNothingToEscape(ref char textStartRef, int length)
            {
                foreach (char c in MemoryMarshal.CreateReadOnlySpan(ref textStartRef, length))
                {
                    Debug.Assert(c <= 127);
                    Debug.Assert(HtmlHelper.EscapeUrlCharacter(c) is null);
                }
            }
        }
#endif

        private void WriteEscapeUrlCorePortable(ReadOnlySpan<char> content, int start = 0)
        {
            int previousPosition = 0;
            for (int i = start; (uint)i < (uint)content.Length; i++)
            {
                char c = content[i];

                string?[] asciiEscapeTable = HtmlHelper.EscapeUrlsForAscii;
                if (c < (uint)asciiEscapeTable.Length)
                {
                    var escape = asciiEscapeTable[c];
                    if (escape != null)
                    {
                        Flush(content, previousPosition, i - previousPosition);
                        previousPosition = i + 1;
                        WriteRaw(escape);
                    }
                }
                else if (!UseNonAsciiNoEscape)
                {
                    Flush(content, previousPosition, i - previousPosition);
                    previousPosition = i + 1;

                    if (CharHelper.IsHighSurrogate(c) && (uint)previousPosition < (uint)content.Length)
                    {
                        EscapeSurrogatePair(c, content[previousPosition]);

                        // Skip next char as it is decoded above
                        i++;
                        previousPosition = i + 1;
                    }
                    else
                    {
                        EscapeNonAscii(c);
                    }
                }
            }

            Flush(content, previousPosition, content.Length - previousPosition);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void Flush(ReadOnlySpan<char> content, int previousPosition, int length)
            {
                if (length != 0)
                {
                    WriteRaw(content.Slice(previousPosition, length));
                }
            }
        }

#if NETCOREAPP3_1_OR_GREATER
        private void EscapeSurrogatePair(char high, char low)
        {
            if (!Rune.TryCreate(high, low, out Rune rune))
                rune = Rune.ReplacementChar;

            EscapeRune(rune);
        }

        private void EscapeNonAscii(char c)
        {
            if (!Rune.TryCreate(c, out Rune rune))
                rune = Rune.ReplacementChar;

            EscapeRune(rune);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EscapeRune(Rune rune)
        {
            Span<byte> utf8 = stackalloc byte[4];
            bool success = rune.TryEncodeToUtf8(utf8, out int utf8Length);
            Debug.Assert(success);

            Span<char> utf16 = stackalloc char[12];

            ref byte byteRef = ref MemoryMarshal.GetReference(utf8);
            ref char charRef = ref MemoryMarshal.GetReference(utf16);

            for (int i = 0; i < utf8Length; i++)
            {
                charRef = '%';

                byte b = byteRef;

                // Based on https://github.com/dotnet/runtime/blob/8582c5cbcf03d6c0d1d3f0d11e622b8b0168f50f/src/libraries/Common/src/System/HexConverter.cs#L83
                uint difference = (((uint)b & 0xF0U) << 4) + ((uint)b & 0x0FU) - 0x8989U;
                uint packedResult = ((((uint)(-(int)difference) & 0x7070U) >> 4) + difference + 0xB9B9U);

                Unsafe.Add(ref charRef, 1) = (char)(packedResult >> 8);
                Unsafe.Add(ref charRef, 2) = (char)(packedResult & 0xFF);

                byteRef = ref Unsafe.Add(ref byteRef, 1);
                charRef = ref Unsafe.Add(ref charRef, 3);
            }

            WriteRaw(utf16.Slice(0, utf8Length * 3));
        }
#else
        private void EscapeSurrogatePair(char high, char low)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(new[] { high, low }))
            {
                WriteRaw($"%{b:X2}");
            }
        }

        private void EscapeNonAscii(char c)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(new[] { c }))
            {
                WriteRaw($"%{b:X2}");
            }
        }
#endif

        /// <summary>
        /// Writes the attached <see cref="HtmlAttributes"/> on the specified <see cref="MarkdownObject"/>.
        /// </summary>
        /// <param name="markdownObject">The object.</param>
        /// <returns></returns>
        public HtmlRenderer WriteAttributes(MarkdownObject markdownObject)
        {
            if (markdownObject is null) ThrowHelper.ArgumentNullException_markdownObject();
            return WriteAttributes(markdownObject.TryGetAttributes());
        }

        /// <summary>
        /// Writes the specified <see cref="HtmlAttributes"/>.
        /// </summary>
        /// <param name="attributes">The attributes to render.</param>
        /// <param name="classFilter">A class filter used to transform a class into another class at writing time</param>
        /// <returns>This instance</returns>
        public HtmlRenderer WriteAttributes(HtmlAttributes? attributes, Func<string, string>? classFilter = null)
        {
            if (attributes is null)
            {
                return this;
            }

            if (attributes.Id is { } id)
            {
                Write(" id=\"");
                WriteEscape(id);
                WriteRaw('"');
            }

            if (attributes.Classes is { Count: > 0 } classes)
            {
                Write(" class=\"");
                for (int i = 0; i < classes.Count; i++)
                {
                    var cssClass = classes[i];
                    if (i > 0)
                    {
                        WriteRaw(' ');
                    }
                    WriteEscape(classFilter != null ? classFilter(cssClass) : cssClass);
                }
                WriteRaw('"');
            }

            if (attributes.Properties is { Count: > 0 } properties)
            {
                foreach (var property in properties)
                {
                    Write(' ');
                    WriteRaw(property.Key);
                    WriteRaw("=\"");
                    WriteEscape(property.Value ?? "");
                    WriteRaw('"');
                }
            }

            return this;
        }

        /// <summary>
        /// Writes the lines of a <see cref="LeafBlock"/>
        /// </summary>
        /// <param name="leafBlock">The leaf block.</param>
        /// <param name="writeEndOfLines">if set to <c>true</c> write end of lines.</param>
        /// <param name="escape">if set to <c>true</c> escape the content for HTML</param>
        /// <param name="softEscape">Only escape &lt; and &amp;</param>
        /// <returns>This instance</returns>
        public HtmlRenderer WriteLeafRawLines(LeafBlock leafBlock, bool writeEndOfLines, bool escape, bool softEscape = false)
        {
            if (leafBlock is null) ThrowHelper.ArgumentNullException_leafBlock();

            var slices = leafBlock.Lines.Lines;
            if (slices is not null)
            {
                for (int i = 0; i < slices.Length; i++)
                {
                    ref StringSlice slice = ref slices[i].Slice;
                    if (slice.Text is null)
                    {
                        break;
                    }

                    if (!writeEndOfLines && i > 0)
                    {
                        WriteLine();
                    }

                    ReadOnlySpan<char> span = slice.AsSpan();
                    if (escape)
                    {
                        WriteEscape(span, softEscape);
                    }
                    else
                    {
                        Write(span);
                    }

                    if (writeEndOfLines)
                    {
                        WriteLine();
                    }
                }
            }

            return this;
        }
    }
}