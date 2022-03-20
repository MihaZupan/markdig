// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Markdig.Helpers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;

namespace Markdig.Renderers
{
    /// <summary>
    /// Default HTML renderer for a Markdown <see cref="MarkdownDocument"/> object.
    /// </summary>
    /// <seealso cref="TextRendererBase{HtmlRenderer}" />
    public class HtmlRenderer : TextRendererBase<HtmlRenderer>
    {
        private static ReadOnlySpan<char> WriteEscapeIndexOfAnyChars => new[] { '<', '>', '&', '"' };
        private static ReadOnlySpan<char> EndOfDomainIndexOfAnyChars => new[] { '/', '?', '#', ':' };

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
                int nextIndex = content.IndexOfAny(WriteEscapeIndexOfAnyChars);
                if (nextIndex == -1)
                {
                    Write(content);
                }
                else
                {
                    WriteEscapeSlow(content, softEscape);
                }
            }
        }

        private void WriteEscapeSlow(ReadOnlySpan<char> content, bool softEscape = false)
        {
            int previousOffset = 0;
            for (int i = 0; i < content.Length; i++)
            {
                switch (content[i])
                {
                    case '<':
                        Write(content.Slice(previousOffset, i - previousOffset));
                        if (EnableHtmlEscape)
                        {
                            Write("&lt;");
                        }
                        previousOffset = i + 1;
                        break;
                    case '>':
                        if (!softEscape)
                        {
                            Write(content.Slice(previousOffset, i - previousOffset));
                            if (EnableHtmlEscape)
                            {
                                Write("&gt;");
                            }
                            previousOffset = i + 1;
                        }
                        break;
                    case '&':
                        Write(content.Slice(previousOffset, i - previousOffset));
                        if (EnableHtmlEscape)
                        {
                            Write("&amp;");
                        }
                        previousOffset = i + 1;
                        break;
                    case '"':
                        if (!softEscape)
                        {
                            Write(content.Slice(previousOffset, i - previousOffset));
                            if (EnableHtmlEscape)
                            {
                                Write("&quot;");
                            }
                            previousOffset = i + 1;
                        }
                        break;
                }
            }

            Write(content.Slice(previousOffset));
        }

        private static readonly IdnMapping IdnMapping = new IdnMapping();

        /// <summary>
        /// Writes the URL escaped for HTML.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <returns>This instance</returns>
        public HtmlRenderer WriteEscapeUrl(string? content)
        {
            if (content is null)
                return this;

            if (BaseUrl != null
                // According to https://github.com/dotnet/runtime/issues/22718
                // this is the proper cross-platform way to check whether a uri is absolute or not:
                && Uri.TryCreate(content, UriKind.RelativeOrAbsolute, out var contentUri) && !contentUri.IsAbsoluteUri)
            {
                content = new Uri(BaseUrl, contentUri).AbsoluteUri;
            }

            if (LinkRewriter != null)
            {
                content = LinkRewriter(content);
            }

            var builder = new ValueStringBuilder(stackalloc char[ValueStringBuilder.StackallocThreshold]);

            // a://c.d = 7 chars
            int schemeOffset = content.Length < 7 ? -1 : content.AsSpan(1).IndexOf("://".AsSpan(), StringComparison.Ordinal);
            if (schemeOffset != -1) // This is an absolute URL
            {
                schemeOffset += 3; // skip ://
                WriteEscapeUrl(content.AsSpan(0, schemeOffset), ref builder);

                int domainLength = content.AsSpan(schemeOffset).IndexOfAny(EndOfDomainIndexOfAnyChars);
                if (domainLength == -1)
                {
                    domainLength = content.Length - schemeOffset - 1;
                }

                bool idnaEncodeDomain = false;
                foreach (char c in content.AsSpan(schemeOffset, domainLength))
                {
                    if (c > 127)
                    {
                        idnaEncodeDomain = true;
                        break;
                    }
                }

                if (idnaEncodeDomain)
                {
                    try
                    {
                        string domainName = IdnMapping.GetAscii(content, schemeOffset, domainLength);
                        WriteEscapeUrl(domainName.AsSpan(), ref builder);
                        WriteEscapeUrl(content.AsSpan(schemeOffset + domainLength), ref builder);
                    }
                    catch
                    {
                        // Not a valid IDN, fallback to non-punycode encoding
                        idnaEncodeDomain = false;
                    }
                }

                if (!idnaEncodeDomain)
                {
                    WriteEscapeUrl(content.AsSpan(schemeOffset), ref builder);
                }
            }
            else // This is a relative URL
            {
                WriteEscapeUrl(content.AsSpan(), ref builder);
            }

            Write(builder.AsSpan());
            builder.Dispose();

            return this;
        }

        private void WriteEscapeUrl(ReadOnlySpan<char> content, ref ValueStringBuilder vsb)
        {
            Span<byte> utf8Bytes = stackalloc byte[4];

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (!HtmlHelper.IsValidNonEscapedUrlCharacter(c))
                {
                    if (c < 128)
                    {
                        if (c == '&')
                        {
                            vsb.Append("&amp;");
                        }
                        else
                        {
                            vsb.AppendEscaped(c);
                        }
                        continue;
                    }
                    else if (!UseNonAsciiNoEscape)
                    {
                        Rune rune;
                        if (CharHelper.IsHighSurrogate(c) && (uint)(i + 1) < (uint)content.Length && Rune.TryCreate(c, content[i + 1], out rune))
                        {
                            i++;
                        }
                        else if (!Rune.TryCreate(c, out rune))
                        {
                            rune = Rune.ReplacementChar;
                        }

                        // The rune is non-ASCII, so encode it as UTF8, and escape each UTF8 byte.
                        rune.TryEncodeToUtf8(utf8Bytes, out int bytesWritten);
                        foreach (byte b in utf8Bytes.Slice(0, bytesWritten))
                        {
                            vsb.AppendEscaped(b);
                        }

                        continue;
                    }
                }

                vsb.Append(c);
            }
        }

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

            if (attributes.Id != null)
            {
                Write(" id=\"");
                WriteEscape(attributes.Id);
                WriteRaw('"');
            }

            if (attributes.Classes is { Count: > 0 })
            {
                Write(" class=\"");
                for (int i = 0; i < attributes.Classes.Count; i++)
                {
                    var cssClass = attributes.Classes[i];
                    if (i > 0)
                    {
                        WriteRaw(' ');
                    }
                    WriteEscape(classFilter != null ? classFilter(cssClass) : cssClass);
                }
                WriteRaw('"');
            }

            if (attributes.Properties is { Count: > 0 })
            {
                foreach (var property in attributes.Properties)
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