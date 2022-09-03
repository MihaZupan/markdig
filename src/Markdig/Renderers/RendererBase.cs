// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdig.Renderers
{
    /// <summary>
    /// Base class for a <see cref="IMarkdownRenderer"/>.
    /// </summary>
    /// <seealso cref="IMarkdownRenderer" />
    public abstract class RendererBase : IMarkdownRenderer
    {
        private sealed class TypeInfo
        {
            public Type Type;
            public IMarkdownObjectRenderer? Renderer;
            public int SeenCount;

            public TypeInfo(Type type, IMarkdownObjectRenderer? renderer)
            {
                Type = type;
                Renderer = renderer;
            }
        }

        private (IntPtr Key, Action<RendererBase, MarkdownObject> Renderer)[] _renderersPerType = ArrayHelper.Empty<(IntPtr, Action<RendererBase, MarkdownObject>)>();
        private readonly Dictionary<IntPtr, TypeInfo> _typeStats = new();
        private int _objectsSinceUnknownType = 0;

        internal int _childrenDepth = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="RendererBase"/> class.
        /// </summary>
        protected RendererBase() { }

        private void WriteUnknownType(MarkdownObject obj)
        {
            TypeInfo? typeInfo;

            lock (_typeStats)
            {
                IntPtr key = GetKeyForType(obj);
                if (_typeStats.TryGetValue(key, out typeInfo))
                {
                    if (++_objectsSinceUnknownType == 10_000)
                    {
                        UpdateRenderersArray();
                    }
                }
                else
                {
                    _objectsSinceUnknownType = 0;
                    _renderersPerType = ArrayHelper.Empty<(IntPtr, Action<RendererBase, MarkdownObject>)>();
                    Type objectType = obj.GetType();
                    typeInfo = new TypeInfo(objectType, FindRenderer(objectType));
                    _typeStats.Add(key, typeInfo);
                }
            }

            Interlocked.Increment(ref typeInfo.SeenCount);

            ObjectWriteBefore?.Invoke(this, obj);

            IMarkdownObjectRenderer? renderer = typeInfo.Renderer;

            if (renderer is not null)
            {
                renderer.Write(this, obj);
            }
            else if (obj.IsContainerInline)
            {
                WriteChildren(Unsafe.As<ContainerInline>(obj));
            }
            else if (obj.IsContainerBlock)
            {
                WriteChildren(Unsafe.As<ContainerBlock>(obj));
            }

            ObjectWriteAfter?.Invoke(this, obj);

            IMarkdownObjectRenderer? FindRenderer(Type objectType)
            {
                for (int i = 0; i < ObjectRenderers.Count; i++)
                {
                    var renderer = ObjectRenderers[i];
                    if (renderer.Accept(this, objectType))
                    {
                        return renderer;
                    }
                }
                return null;
            }

            void UpdateRenderersArray()
            {
                Debug.Assert(Monitor.IsEntered(_typeStats));
                _renderersPerType = _typeStats
                    .OrderByDescending(e => e.Value.SeenCount)
                    .Select(e => (e.Key, GetRenderActionForType(e.Value)))
                    .ToArray();
            }
        }

        private Action<RendererBase, MarkdownObject> GetRenderActionForType(TypeInfo typeInfo)
        {
            Type objectType = typeInfo.Type;
            IMarkdownObjectRenderer? renderer = typeInfo.Renderer;
            bool beforeAfterWriteCallbacks = ObjectWriteBefore is not null || ObjectWriteAfter is not null;

            if (renderer is not null)
            {
                if (renderer is TypedMarkdownObjectRenderer typedRenderer &&
                    typedRenderer.RendererType is not null &&
                    typedRenderer.RendererType.IsAssignableFrom(GetType()))
                {
                    // Derived from MarkdownObjectRenderer<TRenderer, TObject>
                    // We have internal knowledge that can let us skip type checks
                    if (typedRenderer.ShouldUseTryWriters)
                    {
                        return beforeAfterWriteCallbacks
                            ? (thisRef, obj) =>
                            {
                                thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                                typedRenderer.WriteWithoutTypeChecks(thisRef, obj);
                                thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                            }
                            : (thisRef, obj) => typedRenderer.WriteWithoutTypeChecks(thisRef, obj);
                    }
                    else
                    {
                        return beforeAfterWriteCallbacks
                            ? (thisRef, obj) =>
                            {
                                thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                                typedRenderer.WriteWithoutTypeChecksOrTryWriters(thisRef, obj);
                                thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                            }
                            : (thisRef, obj) => typedRenderer.WriteWithoutTypeChecksOrTryWriters(thisRef, obj);
                    }
                }
                else
                {
                    // Custom renderer
                    return beforeAfterWriteCallbacks
                        ? (thisRef, obj) =>
                        {
                            thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                            renderer.Write(thisRef, obj);
                            thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                        }
                        : (thisRef, obj) => renderer.Write(thisRef, obj);
                }
            }
            else if (typeof(ContainerInline).IsAssignableFrom(objectType))
            {
                return beforeAfterWriteCallbacks
                    ? static (thisRef, obj) =>
                    {
                        thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                        thisRef.WriteChildren(Unsafe.As<ContainerInline>(obj));
                        thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                    }
                    : static (thisRef, obj) => thisRef.WriteChildren(Unsafe.As<ContainerInline>(obj));
            }
            else if (typeof(ContainerBlock).IsAssignableFrom(objectType))
            {
                return beforeAfterWriteCallbacks
                    ? static (thisRef, obj) =>
                    {
                        thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                        thisRef.WriteChildren(Unsafe.As<ContainerBlock>(obj));
                        thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                    }
                    : static (thisRef, obj) => thisRef.WriteChildren(Unsafe.As<ContainerBlock>(obj));
            }
            else
            {
                return beforeAfterWriteCallbacks
                    ? static (thisRef, obj) =>
                    {
                        thisRef.ObjectWriteBefore?.Invoke(thisRef, obj);
                        thisRef.ObjectWriteAfter?.Invoke(thisRef, obj);
                    }
                    : static (thisRef, obj) => { };
            }
        }

        public ObjectRendererCollection ObjectRenderers { get; } = new();

        public abstract object Render(MarkdownObject markdownObject);

        public bool IsFirstInContainer { get; private set; }

        public bool IsLastInContainer { get; private set; }

        /// <summary>
        /// Occurs when before writing an object.
        /// </summary>
        public event Action<IMarkdownRenderer, MarkdownObject>? ObjectWriteBefore;

        /// <summary>
        /// Occurs when after writing an object.
        /// </summary>
        public event Action<IMarkdownRenderer, MarkdownObject>? ObjectWriteAfter;

        /// <summary>
        /// Writes the children of the specified <see cref="ContainerBlock"/>.
        /// </summary>
        /// <param name="containerBlock">The container block.</param>
        public void WriteChildren(ContainerBlock containerBlock)
        {
            if (containerBlock is null)
            {
                return;
            }

            ThrowHelper.CheckDepthLimit(_childrenDepth++);

            bool saveIsFirstInContainer = IsFirstInContainer;
            bool saveIsLastInContainer = IsLastInContainer;

            for (int i = 0; i < containerBlock.Count; i++)
            {
                IsFirstInContainer = i == 0;
                IsLastInContainer = i + 1 == containerBlock.Count;
                Write(containerBlock[i]);
            }

            IsFirstInContainer = saveIsFirstInContainer;
            IsLastInContainer = saveIsLastInContainer;

            _childrenDepth--;
        }

        /// <summary>
        /// Writes the children of the specified <see cref="ContainerInline"/>.
        /// </summary>
        /// <param name="containerInline">The container inline.</param>
        public void WriteChildren(ContainerInline containerInline)
        {
            if (containerInline is null)
            {
                return;
            }

            ThrowHelper.CheckDepthLimit(_childrenDepth++);

            bool saveIsFirstInContainer = IsFirstInContainer;
            bool saveIsLastInContainer = IsLastInContainer;

            bool isFirst = true;
            var inline = containerInline.FirstChild;
            while (inline != null)
            {
                IsFirstInContainer = isFirst;
                IsLastInContainer = inline.NextSibling is null;

                Write(inline);
                inline = inline.NextSibling;

                isFirst = false;
            }

            IsFirstInContainer = saveIsFirstInContainer;
            IsLastInContainer = saveIsLastInContainer;

            _childrenDepth--;
        }

        /// <summary>
        /// Writes the specified Markdown object.
        /// </summary>
        /// <param name="obj">The Markdown object to write to this renderer.</param>
        public void Write(MarkdownObject obj)
        {
            if (obj is null)
            {
                return;
            }

            IntPtr key = GetKeyForType(obj);

            foreach (var (Key, Renderer) in _renderersPerType)
            {
                if (key == Key)
                {
                    Renderer(this, obj);
                    return;
                }
            }

            WriteUnknownType(obj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr GetKeyForType(MarkdownObject obj)
        {
            return Type.GetTypeHandle(obj).Value;
        }
    }
}