// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;

namespace Markdig.Extensions.MediaLinks
{
    /// <summary>
    /// Extension for extending image Markdown links in case a video or an audio file is linked and output proper link.
    /// </summary>
    /// <seealso cref="IMarkdownExtension" />
    public class MediaLinkExtension : IMarkdownExtension
    {
        private readonly CompactPrefixTree<string> _extensionToMimeType;

        public MediaLinkExtension() : this(null)
        {
        }

        public MediaLinkExtension(MediaOptions? options)
        {
            Options = options ?? new MediaOptions();

            Dictionary<string, string> input = Options.ExtensionToMimeType;
            _extensionToMimeType = new CompactPrefixTree<string>(input.Count, input.Count * 2, input.Count * 2);
            foreach (var pair in input)
            {
                _extensionToMimeType.Add(pair.Key.Substring(1).ToLowerInvariant(), pair.Value.ToLowerInvariant());
            }
        }

        public MediaOptions Options { get; }

        public void Setup(MarkdownPipelineBuilder pipeline)
        {
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is HtmlRenderer htmlRenderer)
            {
                var inlineRenderer = htmlRenderer.ObjectRenderers.FindExact<LinkInlineRenderer>();
                if (inlineRenderer != null)
                {
                    inlineRenderer.TryWriters.Remove(TryLinkInlineRenderer);
                    inlineRenderer.TryWriters.Add(TryLinkInlineRenderer);
                }
            }
        }

        private bool TryLinkInlineRenderer(HtmlRenderer renderer, LinkInline linkInline)
        {
            if (!linkInline.IsImage || linkInline.Url is null)
            {
                return false;
            }

            bool isSchemaRelative = false;
            // Only process absolute Uri
            if (!Uri.TryCreate(linkInline.Url, UriKind.RelativeOrAbsolute, out Uri? uri) || !uri.IsAbsoluteUri)
            {
                // see https://tools.ietf.org/html/rfc3986#section-4.2
                // since relative uri doesn't support many properties, "http" is used as a placeholder here.
                if (linkInline.Url.StartsWith("//", StringComparison.Ordinal) && Uri.TryCreate("http:" + linkInline.Url, UriKind.Absolute, out uri))
                {
                    isSchemaRelative = true;
                }
                else
                {
                    return false;
                }
            }

            if (TryRenderIframeFromKnownProviders(uri, isSchemaRelative, renderer, linkInline))
            {
                return true;
            }

            if (TryGuessAudioVideoFile(uri.OriginalString, renderer, linkInline))
            {
                return true;
            }

            return false;
        }

        private static HtmlAttributes GetHtmlAttributes(LinkInline linkInline)
        {
            var htmlAttributes = new HtmlAttributes();
            var fromAttributes = linkInline.TryGetAttributes();
            if (fromAttributes != null)
            {
                fromAttributes.CopyTo(htmlAttributes, false, false);
            }

            return htmlAttributes;
        }

        private bool TryGuessAudioVideoFile(string uri, HtmlRenderer renderer, LinkInline linkInline)
        {
            // Try to detect if we have an audio/video from the file extension
            int lastDot = uri.LastIndexOf('.');
            if (lastDot < 0)
            {
                return false;
            }

            ReadOnlySpan<char> extension = uri.AsSpan(lastDot + 1);
            if (extension.Length > 16)
            {
                return false;
            }

            Span<char> extensionLowerCase = stackalloc char[16];
            extensionLowerCase = extensionLowerCase.Slice(0, extension.ToLowerInvariant(extensionLowerCase));

            if (_extensionToMimeType.TryMatchExact(extensionLowerCase, out var match))
            {
                string mimeType = match.Value;
                var htmlAttributes = GetHtmlAttributes(linkInline);
                var isAudio = mimeType.StartsWith("audio", StringComparison.Ordinal);
                var tagType = isAudio ? "audio" : "video";

                renderer.Write('<');
                renderer.WriteRaw(tagType);

                htmlAttributes.AddPropertyIfNotExist("width", Options.Width);
                if (!isAudio)
                {
                    htmlAttributes.AddPropertyIfNotExist("height", Options.Height);
                }
                htmlAttributes.AddPropertyIfNotExist("controls", null);

                if (!string.IsNullOrEmpty(Options.Class))
                    htmlAttributes.AddClass(Options.Class);

                renderer.WriteAttributes(htmlAttributes);

                renderer.Write("><source type=\"");
                renderer.WriteRaw(mimeType);
                renderer.WriteRaw("\" src=\"");
                renderer.WriteRaw(linkInline.Url);
                renderer.WriteRaw("\"></source></");
                renderer.WriteRaw(tagType);
                renderer.WriteRaw('>');

                return true;
            }
            return false;
        }

        private bool TryRenderIframeFromKnownProviders(Uri uri, bool isSchemaRelative, HtmlRenderer renderer, LinkInline linkInline)
        {
            IHostProvider? foundProvider = null;
            string? iframeUrl = null;
            foreach (var provider in Options.Hosts)
            {
                if (!provider.TryHandle(uri, isSchemaRelative, out iframeUrl))
                    continue;
                foundProvider = provider;
                break;
            }

            if (foundProvider is null)
            {
                return false;
            }

            var htmlAttributes = GetHtmlAttributes(linkInline);
            renderer.Write("<iframe src=\"");
            renderer.WriteEscapeUrl(iframeUrl);
            renderer.Write('"');

            if (!string.IsNullOrEmpty(Options.Width))
                htmlAttributes.AddPropertyIfNotExist("width", Options.Width);

            if (!string.IsNullOrEmpty(Options.Height))
                htmlAttributes.AddPropertyIfNotExist("height", Options.Height);

            if (!string.IsNullOrEmpty(Options.Class))
                htmlAttributes.AddClass(Options.Class);

            if (foundProvider.Class is { Length: > 0 } className)
                htmlAttributes.AddClass(className);

            htmlAttributes.AddPropertyIfNotExist("frameborder", "0");
            if (foundProvider.AllowFullScreen)
            {
                htmlAttributes.AddPropertyIfNotExist("allowfullscreen", null);
            }
            renderer.WriteAttributes(htmlAttributes);
            renderer.Write("></iframe>");

            return true;
        }
    }
}
