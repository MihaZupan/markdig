using Markdig;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace Markdig;

public static class InlineMarkdown
{
    private static readonly MarkdownPipeline s_inlinePipeline = CreatePipeline();

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder();
        builder.BlockParsers.Clear();
        builder.Extensions.AddIfNotAlready<CatchAllExtension>();
        return builder.Build();
    }

    public static string ToHtml(string inlineMarkdown, MarkdownParserContext? context = null)
    {
        return Markdown.ToHtml(inlineMarkdown, s_inlinePipeline, context);
    }

    private sealed class CatchAllExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            pipeline.BlockParsers.AddIfNotAlready<CatchAllParser>();
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            renderer.ObjectRenderers.AddIfNotAlready<CatchAllRenderer>();
        }
    }

    private sealed class CatchAllBlock : LeafBlock
    {
        public CatchAllBlock(BlockParser? parser) : base(parser) { }
    }

    private sealed class CatchAllRenderer : HtmlObjectRenderer<CatchAllBlock>
    {
        protected override void Write(HtmlRenderer renderer, CatchAllBlock obj)
        {
            renderer.WriteLeafInline(obj);
        }
    }

    private sealed class CatchAllParser : BlockParser
    {
        public override BlockState TryOpen(BlockProcessor processor)
        {
            processor.NewBlocks.Push(new CatchAllBlock(this)
            {
                ProcessInlines = true
            });
            return BlockState.Continue;
        }

        public override BlockState TryContinue(BlockProcessor processor, Block block)
        {
            return BlockState.Continue;
        }
    }
}