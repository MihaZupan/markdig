// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Markdig.Renderers;

/// <summary>
/// Base class for a <see cref="IMarkdownRenderer"/>.
/// </summary>
/// <seealso cref="IMarkdownRenderer" />
public abstract class RendererBase : IMarkdownRenderer
{
    private sealed class TypeInfo
    {
        public IMarkdownObjectRenderer? Renderer;
        public int SeenCount;
    }

    private readonly struct RendererEntry
    {
        public readonly IntPtr Key;
        public readonly IMarkdownObjectRenderer? Renderer;

        public RendererEntry(IntPtr key, IMarkdownObjectRenderer? renderer)
        {
            Key = key;
            Renderer = renderer;
        }
    }

    private RendererEntry[] _renderersPerType = Array.Empty<RendererEntry>();
    private readonly ConcurrentDictionary<IntPtr, TypeInfo> _typeStats = new();
    private int _objectsSinceUnknownType = 0;

    internal int _childrenDepth = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendererBase"/> class.
    /// </summary>
    protected RendererBase() { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IMarkdownObjectRenderer? GetRendererInstance(MarkdownObject obj)
    {
        IntPtr key = GetKeyForType(obj);

        TypeInfo? typeInfo;

        while (!_typeStats.TryGetValue(key, out typeInfo))
        {
            _objectsSinceUnknownType = 0;
            _renderersPerType = Array.Empty<RendererEntry>();

            typeInfo = new TypeInfo();

            Type objectType = obj.GetType();
            for (int i = 0; i < ObjectRenderers.Count; i++)
            {
                var renderer = ObjectRenderers[i];
                if (renderer.Accept(this, objectType))
                {
                    typeInfo.Renderer = renderer;
                    break;
                }
            }

            if (_typeStats.TryAdd(key, typeInfo))
            {
                _renderersPerType = Array.Empty<RendererEntry>();
                Interlocked.Exchange(ref _objectsSinceUnknownType, 0);
                break;
            }
        }

        Interlocked.Increment(ref typeInfo.SeenCount);

        if (Interlocked.Increment(ref _objectsSinceUnknownType) == 10_000)
        {
            _renderersPerType = CreateRenderersArray(_typeStats);
        }

        return typeInfo.Renderer;

        static RendererEntry[] CreateRenderersArray(ConcurrentDictionary<IntPtr, TypeInfo> typeStats)
        {
            return typeStats
                .OrderByDescending(e => e.Value.SeenCount)
                .Select(e => new RendererEntry(e.Key, e.Value.Renderer))
                .ToArray();
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

        // Calls before writing an object
        ObjectWriteBefore?.Invoke(this, obj);

        IMarkdownObjectRenderer? renderer = null;
        IntPtr key = GetKeyForType(obj);

        foreach (RendererEntry entry in _renderersPerType)
        {
            if (key == entry.Key)
            {
                renderer = entry.Renderer;
                goto Render;
            }
        }

        renderer = GetRendererInstance(obj);

    Render:
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

        // Calls after writing an object
        ObjectWriteAfter?.Invoke(this, obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IntPtr GetKeyForType(MarkdownObject obj)
    {
        return Type.GetTypeHandle(obj).Value;
    }
}