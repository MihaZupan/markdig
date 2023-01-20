using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ObjectLayoutInspector;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;

//TypeLayout.PrintLayout<LinkInline>();

//foreach (var container in new Benchmark().Parse().Descendants<ContainerBlock>())
//{
//    Console.WriteLine(container.Count);
//}


//Custom();
//QuickAllocationTest(); QuickAllocationTest(); QuickAllocationTest();
//RunForMemoryAnalysis();
RunForTracing();
//BenchmarkRunner.Run<Benchmark>();

static void RunForTracing()
{
    var rollingAverage = new RollingAverage(50);
    var benchmark = new Benchmark();

    Stopwatch s = Stopwatch.StartNew();

    for (int loopCounter = 0; ; loopCounter++)
    {
        const int Iterations = 1 << 12;

        for (int i = 0; i < Iterations; i++)
        {
            benchmark.Test();
        }

        double avg = rollingAverage.Update(Iterations / s.Elapsed.TotalSeconds);

        if (loopCounter == 8)
        {
            loopCounter = 0;
            Console.WriteLine($"{(int)avg}/s ({TimeSpan.FromSeconds(1 / avg).TotalMilliseconds:N2} ms)");
        }

        s.Restart();
    }
}

static void RunForMemoryAnalysis()
{
    var benchmark = new Benchmark();
    benchmark.Test();

    for (int retry = 1; retry <= 4; retry++)
    {
        for (int i = 0; i < 1_000; i++)
        {
            benchmark.Test();
        }

        Thread.Sleep(3000);
    }
}

static void QuickAllocationTest()
{
    var benchmark = new Benchmark();
    //benchmark.SourceText = "MarkdigReadme";
    benchmark.Setup();

    for (int i = 0; i < 10; i++) benchmark.Test();

    long start = GC.GetAllocatedBytesForCurrentThread();

    const int Iterations = 1_000;
    for (int i = 0; i < Iterations; i++)
    {
        benchmark.Test();
    }

    long end = GC.GetAllocatedBytesForCurrentThread();

    Console.WriteLine($"{(end - start) / (double)Iterations:N1} B / iteration");
}

static void Custom()
{
    var benchmark = new Benchmark();
    //benchmark.SourceText = "MarkdigReadme";
    benchmark.Setup();

    foreach (var document in benchmark.Documents)
    {
        foreach (var group in document
            .Descendants<CodeInline>().Select(c => c.Content)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"{group.Count(),3}: {group.Key}");
        }

        Console.WriteLine();
    }
}

[MediumRunJob]
//[ShortRunJob]
[MemoryDiagnoser(false)]
public class Benchmark
{
    private readonly FastStringWriter _writer;
    private readonly HtmlRenderer _renderer;

    private MarkdownPipeline _pipeline = null!;
    private string[] _sourceTexts = null!;
    public MarkdownDocument[] Documents = null!;

    [Params("MarkdigReadme", /* "TracingArticle", */ "YarpDocs")]
    public string SourceTexts = "MarkdigReadme";
    //public string SourceText = "MarkdigReadme";

    [Params(true, false)]
    public bool AdvancedPipeline = false;

    [GlobalSetup]
    public void Setup()
    {
        var builder = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation();

        if (AdvancedPipeline)
        {
            builder.UseAdvancedExtensions();
        }

        _pipeline = builder.Build();
        _pipeline.Setup(_renderer);

        _sourceTexts = SourceTexts == "MarkdigReadme" ? GetFromUrl("https://raw.githubusercontent.com/xoofx/markdig/master/readme.md") :
            SourceTexts == "YarpDocs" ? GetFromFiles(@"C:\MihaZupan\reverse-proxy\docs") :
            SourceTexts == "Spec" ? GetFromUrl("https://raw.githubusercontent.com/xoofx/markdig/master/src/Markdig.Tests/Specs/CommonMark.md") : 
            GetFromUrl("https://raw.githubusercontent.com/microsoft/reverse-proxy/main/docs/docfx/articles/distributed-tracing.md");

        //_sourceTexts = new[] { "this is a **Markdown** comment that _may_ find it's way into some `C#` source code" };

        Documents = _sourceTexts.Select(t => Markdown.Parse(t, _pipeline)).ToArray();

        static string[] GetFromUrl(string url)
        {
            return new[] { new HttpClient().GetStringAsync(url).Result };
        }

        static string[] GetFromFiles(string path)
        {
            return Directory.EnumerateFiles(path, "*.md", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();
        }
    }

    public Benchmark()
    {
        _writer = new FastStringWriter();
        _renderer = new HtmlRenderer(_writer);

        Setup();
    }

    //[Benchmark]
    public void Parse()
    {
        foreach (string text in _sourceTexts)
        {
            Markdown.Parse(text, _pipeline);
        }
    }

    //[Benchmark]
    public void ToHtml()
    {
        foreach (string text in _sourceTexts)
        {
            Markdown.ToHtml(text, _pipeline);
        }
    }

    //[Benchmark]
    public int ToHtmlRenderOnly()
    {
        int count = 0;
        foreach (MarkdownDocument document in Documents)
        {
            _renderer.Render(document);
            count += _writer.ToString().Length;
            _writer.Reset();
        }
        return count;
    }

    [Benchmark]
    public void RenderWithoutToString()
    {
        foreach (MarkdownDocument document in Documents)
        {
            _renderer.Render(document);
            _writer.Reset();
        }
    }

    public void Test() => RenderWithoutToString();
}

public sealed class CustomBlock : Block
{
    public CustomBlock(BlockParser? parser) : base(parser) { }
}

public sealed class CustomInline : Inline
{

}

internal sealed class FastStringWriter : TextWriter
{
#if NET452
        private static Task CompletedTask => Task.FromResult(0);
#else
    private static Task CompletedTask => Task.CompletedTask;
#endif

    public override Encoding Encoding => Encoding.Unicode;

    private char[] _chars;
    private int _pos;
    private string _newLine;

    public FastStringWriter()
    {
        _chars = new char[1024];
        _newLine = "\n";
    }

    [AllowNull]
    public override string NewLine
    {
        get => _newLine;
        set => _newLine = value ?? Environment.NewLine;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(char value)
    {
        char[] chars = _chars;
        int pos = _pos;
        if ((uint)pos < (uint)chars.Length)
        {
            chars[pos] = value;
            _pos = pos + 1;
        }
        else
        {
            GrowAndAppend(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(char value)
    {
        Write(value);
        WriteLine();
    }

    public override Task WriteAsync(char value)
    {
        Write(value);
        return CompletedTask;
    }

    public override Task WriteLineAsync(char value)
    {
        WriteLine(value);
        return CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(string? value)
    {
        if (value is not null)
        {
            if (_pos > _chars.Length - value.Length)
            {
                Grow(value.Length);
            }

            value.AsSpan().CopyTo(_chars.AsSpan(_pos));
            _pos += value.Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(string? value)
    {
        Write(value);
        WriteLine();
    }

    public override Task WriteAsync(string? value)
    {
        Write(value);
        return CompletedTask;
    }

    public override Task WriteLineAsync(string? value)
    {
        WriteLine(value);
        return CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(char[]? buffer)
    {
        if (buffer is not null)
        {
            if (_pos > _chars.Length - buffer.Length)
            {
                Grow(buffer.Length);
            }

            buffer.CopyTo(_chars.AsSpan(_pos));
            _pos += buffer.Length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(char[]? buffer)
    {
        Write(buffer);
        WriteLine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(char[] buffer, int index, int count)
    {
        if (buffer is not null)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(buffer.Length);
            }

            buffer.AsSpan(index, count).CopyTo(_chars.AsSpan(_pos));
            _pos += count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(char[] buffer, int index, int count)
    {
        Write(buffer, index, count);
        WriteLine();
    }

    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        Write(buffer, index, count);
        return CompletedTask;
    }

    public override Task WriteLineAsync(char[] buffer, int index, int count)
    {
        WriteLine(buffer, index, count);
        return CompletedTask;
    }

#if !(NET452 || NETSTANDARD2_0)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<char> value)
    {
        if (_pos > _chars.Length - value.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_chars.AsSpan(_pos));
        _pos += value.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(ReadOnlySpan<char> buffer)
    {
        Write(buffer);
        WriteLine();
    }

    public override Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return CompletedTask;
    }

    public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        WriteLine(buffer.Span);
        return CompletedTask;
    }
#endif

#if !(NET452 || NETSTANDARD2_0 || NETSTANDARD2_1)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(StringBuilder? value)
    {
        if (value is not null)
        {
            int length = value.Length;
            if (_pos > _chars.Length - length)
            {
                Grow(length);
            }

            value.CopyTo(0, _chars.AsSpan(_pos), length);
            _pos += length;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine(StringBuilder? value)
    {
        Write(value);
        WriteLine();
    }

    public override Task WriteAsync(StringBuilder? value, CancellationToken cancellationToken = default)
    {
        Write(value);
        return CompletedTask;
    }

    public override Task WriteLineAsync(StringBuilder? value, CancellationToken cancellationToken = default)
    {
        WriteLine(value);
        return CompletedTask;
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteLine()
    {
        foreach (char c in _newLine)
        {
            Write(c);
        }
    }

    public override Task WriteLineAsync()
    {
        WriteLine();
        return CompletedTask;
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowAndAppend(char value)
    {
        Grow(1);
        Write(value);
    }

    private void Grow(int additionalCapacityBeyondPos)
    {
        Debug.Assert(additionalCapacityBeyondPos > 0);
        Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos, "No resize is needed.");

        char[] newArray = new char[(int)Math.Max((uint)(_pos + additionalCapacityBeyondPos), (uint)_chars.Length * 2)];
        _chars.AsSpan(0, _pos).CopyTo(newArray);
        _chars = newArray;
    }


    public override void Flush() { }

    public override void Close() { }

    public override Task FlushAsync() => CompletedTask;

#if !(NET452 || NETSTANDARD2_0)
    public override ValueTask DisposeAsync() => default;
#endif


    public void Reset()
    {
        _pos = 0;
    }

    public override string ToString()
    {
        return _chars.AsSpan(0, _pos).ToString();
    }
}

public sealed class RollingAverage
{
    private readonly double[] _values;
    private double _sum;
    private int _index;
    private int _count;

    public RollingAverage(int intervals)
    {
        _values = new double[intervals];
    }

    public double Update(double value)
    {
        double[] values = _values;
        int index = _index + 1;
        if ((uint)index >= values.Length) index = 0;
        _index = index;
        double newSum = _sum = _sum - values[index] + value;
        values[index] = value;
        if (_count < values.Length) _count++;
        return newSum / _count;
    }
}
