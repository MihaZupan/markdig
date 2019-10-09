using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Markdig;
using Markdig.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bench
{
    class Program
    {
        static void Main()
        {
            var test = new MarkdigTests();
            Console.WriteLine(test.TestCount);

#if BENCHMARK
            BenchmarkRunner.Run<MarkdigTests>();
#else
            for (int i = 1; i <= 10; i++)
            {
                test.Parse();

                // if (i % 10 == 0) Console.WriteLine(i);
            }
#endif
        }
    }

    [MemoryDiagnoser]
    public class MarkdigTests
    {
        public int TestCount => MarkdownTexts.Length;
        private readonly string[] MarkdownTexts;
        private readonly MarkdownPipeline Pipeline;

        public MarkdigTests()
        {
            Pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var dirs = new List<string>
            {
                @"C:\Users\Miha\Downloads\docs-master\docs-master\docs\core"
            };


            var markdownPaths = dirs
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Where(file => !file.EndsWith("hang.md"))
                .ToList();

            List<string> nonThrowingMarkdown = new List<string>();
            foreach (var path in markdownPaths)
            {
                try
                {
                    string markdown = File.ReadAllText(path);
                    _ = Markdown.ToHtml(markdown, Pipeline);
                    nonThrowingMarkdown.Add(markdown);
                }
                catch { }
            }
            MarkdownTexts = nonThrowingMarkdown.ToArray();
        }

        [Benchmark]
        public int Parse()
        {
            int count = 0;
            foreach (var markdown in MarkdownTexts)
                count += Markdown.ToHtml(markdown, Pipeline).Length;
            return count;
        }
    }
}
