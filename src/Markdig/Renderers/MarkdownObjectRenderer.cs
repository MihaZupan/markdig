// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using Markdig.Helpers;
using Markdig.Syntax;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Markdig.Renderers
{
    /// <summary>
    /// Internal implementation detail allowing for some performance optimizations.
    /// It can not be derived from outside of the Markdig assembly.
    /// Use <see cref="MarkdownObjectRenderer{TRenderer, TObject}"/> instead.
    /// </summary>
    public abstract class TypedMarkdownObjectRenderer : IMarkdownObjectRenderer
    {
        internal Type? RendererType { get; set; }

        public abstract bool Accept(RendererBase renderer, Type objectType);

        public abstract void Write(RendererBase renderer, MarkdownObject objectToRender);

        internal abstract bool ShouldUseTryWriters { get; }
        internal abstract void WriteWithoutTypeChecks(RendererBase renderer, MarkdownObject obj);
        internal abstract void WriteWithoutTypeChecksOrTryWriters(RendererBase renderer, MarkdownObject obj);
    }

    /// <summary>
    /// A base class for rendering <see cref="Block" /> and <see cref="Syntax.Inlines.Inline" /> Markdown objects.
    /// </summary>
    /// <typeparam name="TRenderer">The type of the renderer.</typeparam>
    /// <typeparam name="TObject">The type of the object.</typeparam>
    /// <seealso cref="IMarkdownObjectRenderer" />
    public abstract class MarkdownObjectRenderer<TRenderer, TObject> : TypedMarkdownObjectRenderer where TRenderer : RendererBase where TObject : MarkdownObject
    {
        private OrderedList<TryWriteDelegate>? _tryWriters;

        protected MarkdownObjectRenderer()
        {
            RendererType = typeof(TRenderer);
        }

        public delegate bool TryWriteDelegate(TRenderer renderer, TObject obj);

        public sealed override bool Accept(RendererBase renderer, Type objectType)
        {
            return typeof(TObject).IsAssignableFrom(objectType);
        }

        internal sealed override bool ShouldUseTryWriters => _tryWriters is not null && _tryWriters.Count > 0;

        public sealed override void Write(RendererBase renderer, MarkdownObject obj)
        {
            var htmlRenderer = (TRenderer)renderer;
            var typedObj = (TObject)obj;

            if (_tryWriters is not null && TryWrite(htmlRenderer, typedObj))
            {
                return;
            }

            Write(htmlRenderer, typedObj);
        }

        internal sealed override void WriteWithoutTypeChecks(RendererBase renderer, MarkdownObject obj)
        {
            Debug.Assert(_tryWriters is not null && _tryWriters.Count > 0);
            Debug.Assert(renderer is TRenderer);
            Debug.Assert(obj is TObject);

            var htmlRenderer = Unsafe.As<TRenderer>(renderer);
            var typedObj = Unsafe.As<TObject>(obj);

            if (TryWrite(htmlRenderer, typedObj))
            {
                return;
            }

            Write(htmlRenderer, typedObj);
        }

        internal sealed override void WriteWithoutTypeChecksOrTryWriters(RendererBase renderer, MarkdownObject obj)
        {
            Debug.Assert(_tryWriters is null || _tryWriters.Count == 0);
            Debug.Assert(renderer is TRenderer);
            Debug.Assert(obj is TObject);

            Write(Unsafe.As<TRenderer>(renderer), Unsafe.As<TObject>(obj));
        }

        private bool TryWrite(TRenderer renderer, TObject obj)
        {
            for (int i = 0; i < _tryWriters!.Count; i++)
            {
                var tryWriter = _tryWriters[i];
                if (tryWriter(renderer, obj))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the optional writers attached to this instance.
        /// </summary>
        public OrderedList<TryWriteDelegate> TryWriters => _tryWriters ??= new();

        /// <summary>
        /// Writes the specified Markdown object to the renderer.
        /// </summary>
        /// <param name="renderer">The renderer.</param>
        /// <param name="obj">The markdown object.</param>
        protected abstract void Write(TRenderer renderer, TObject obj);
    }
}