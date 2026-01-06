using Xunit;
using XmlIndexer.Reports;

namespace XmlIndexer.Tests.Reports;

public class CodeTokenizerTests
{
    private readonly CodeTokenizer _tokenizer = new();

    [Fact]
    public void TokenizeCSharp_Keywords_ClassifiedCorrectly()
    {
        var code = "public override void Test()";
        var tokens = _tokenizer.TokenizeCSharp(code).ToList();

        Assert.Contains(tokens, t => t.Value == "public" && t.Type == CodeTokenizer.TokenType.Keyword);
        Assert.Contains(tokens, t => t.Value == "override" && t.Type == CodeTokenizer.TokenType.Keyword);
        Assert.Contains(tokens, t => t.Value == "void" && t.Type == CodeTokenizer.TokenType.Keyword);
    }

    [Fact]
    public void TokenizeCSharp_PascalCaseTypes_ClassifiedAsType()
    {
        var code = "EntityPlayer player = new EntityPlayer()";
        var tokens = _tokenizer.TokenizeCSharp(code).ToList();

        Assert.Contains(tokens, t => t.Value == "EntityPlayer" && t.Type == CodeTokenizer.TokenType.Type);
    }

    [Fact]
    public void TokenizeCSharp_CamelCaseVariables_ClassifiedAsIdentifier()
    {
        var code = "var myVariable = 0";
        var tokens = _tokenizer.TokenizeCSharp(code).ToList();

        Assert.Contains(tokens, t => t.Value == "myVariable" && t.Type == CodeTokenizer.TokenType.Identifier);
    }

    [Fact]
    public void TokenizeXPath_Operators_Detected()
    {
        var xpath = "//items/item[@name='test']";
        var tokens = _tokenizer.TokenizeXPath(xpath).ToList();

        Assert.Contains(tokens, t => t.Value == "//" && t.Type == CodeTokenizer.TokenType.XPathOperator);
        Assert.Contains(tokens, t => t.Value == "/" && t.Type == CodeTokenizer.TokenType.XPathOperator);
        Assert.Contains(tokens, t => t.Value == "@" && t.Type == CodeTokenizer.TokenType.XPathOperator);
    }

    [Fact]
    public void TokenizeXPath_AxisNames_Detected()
    {
        var xpath = "ancestor::div/descendant::span";
        var tokens = _tokenizer.TokenizeXPath(xpath).ToList();

        Assert.Contains(tokens, t => t.Value == "ancestor" && t.Type == CodeTokenizer.TokenType.XPathOperator);
        Assert.Contains(tokens, t => t.Value == "descendant" && t.Type == CodeTokenizer.TokenType.XPathOperator);
    }

    [Fact]
    public void ExtractLinkable_ReturnsFirstOccurrenceOnly()
    {
        var code = "public void Test() { public void Other() }";
        var tokens = _tokenizer.ExtractLinkable(code, isCSharp: true).ToList();

        var publicCount = tokens.Count(t => t.Value == "public");
        Assert.Equal(1, publicCount);
    }

    [Fact]
    public void ExtractLinkable_SkipsIdentifiers()
    {
        var code = "myVariable = otherVariable";
        var tokens = _tokenizer.ExtractLinkable(code, isCSharp: true).ToList();

        Assert.Empty(tokens);
    }

    [Fact]
    public void ExtractLinkable_IncludesKeywordsAndTypes()
    {
        var code = "public class MyClass : BaseClass";
        var tokens = _tokenizer.ExtractLinkable(code, isCSharp: true).ToList();

        Assert.Contains(tokens, t => t.Value == "public");
        Assert.Contains(tokens, t => t.Value == "class");
        Assert.Contains(tokens, t => t.Value == "MyClass");
        Assert.Contains(tokens, t => t.Value == "BaseClass");
    }

    [Fact]
    public void TokenizeCSharp_EmptyString_ReturnsEmpty()
    {
        var tokens = _tokenizer.TokenizeCSharp("").ToList();
        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeCSharp_NullString_ReturnsEmpty()
    {
        var tokens = _tokenizer.TokenizeCSharp(null!).ToList();
        Assert.Empty(tokens);
    }

    [Fact]
    public void TokenizeXPath_EmptyString_ReturnsEmpty()
    {
        var tokens = _tokenizer.TokenizeXPath("").ToList();
        Assert.Empty(tokens);
    }
}
