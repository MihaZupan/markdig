// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license. 
// See the license.txt file in the project root for more information.

using System;

namespace Markdig.Helpers
{
    /// <summary>
    /// <see cref="StringSlice"/> but without the <see cref="NewLine"/> field.
    /// </summary>
    internal struct StringChunk
    {
        private string? _text;
        private int _offset;
        private int _length;

        public StringChunk(string? text)
        {
            _text = text;
            _offset = 0;
            _length = text?.Length ?? 0;
        }

        public StringChunk(string text, int offset, int length)
        {
            _text = text;
            _offset = offset;
            _length = length;
        }

        public readonly bool HasValue => _text is not null;

        public readonly int Length => _length;

        public readonly ReadOnlySpan<char> AsSpan() => _text.AsSpan(_offset, _length);

        public override string? ToString()
        {
            if (_offset != 0)
            {
                string substring = _text!.Substring(_offset, _length);
                _text = substring;
                _offset = 0;
            }
            return _text;
        }
    }
}
