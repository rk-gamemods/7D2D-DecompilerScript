using Xunit;
using XmlIndexer.Reports;

namespace XmlIndexer.Tests.Reports;

public class DocumentationResolverTests
{
    private readonly DocumentationResolver _resolver = new();

    [Theory]
    [InlineData("override", "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override")]
    [InlineData("abstract", "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/abstract")]
    [InlineData("virtual", "https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual")]
    public void Resolve_CSharpKeyword_ReturnsCorrectUrl(string keyword, string expectedUrl)
    {
        var link = _resolver.Resolve(keyword, DocumentationResolver.TokenContext.CSharp);

        Assert.NotNull(link);
        Assert.Equal(expectedUrl, link!.Url);
        Assert.Equal(DocumentationResolver.Confidence.High, link.Confidence);
        Assert.True(link.IsExternal);
    }

    [Theory]
    [InlineData("//", "https://www.w3.org/TR/xpath/#axes")]
    [InlineData("/", "https://www.w3.org/TR/xpath/#axes")]
    [InlineData("@", "https://www.w3.org/TR/xpath/#axes")]
    public void Resolve_XPathOperator_ReturnsW3CUrl(string op, string expectedUrl)
    {
        var link = _resolver.Resolve(op, DocumentationResolver.TokenContext.XPath);

        Assert.NotNull(link);
        Assert.Equal(expectedUrl, link!.Url);
        Assert.Equal(DocumentationResolver.Confidence.High, link.Confidence);
    }

    [Theory]
    [InlineData("i")]
    [InlineData("x")]
    [InlineData("value")]
    [InlineData("true")]
    [InlineData("null")]
    public void Resolve_BlacklistedToken_ReturnsNull(string token)
    {
        var link = _resolver.Resolve(token, DocumentationResolver.TokenContext.CSharp);

        Assert.Null(link);
    }

    [Fact]
    public void Resolve_EmptyString_ReturnsNull()
    {
        var link = _resolver.Resolve("", DocumentationResolver.TokenContext.CSharp);
        Assert.Null(link);
    }

    [Fact]
    public void Resolve_WhitespaceOnly_ReturnsNull()
    {
        var link = _resolver.Resolve("   ", DocumentationResolver.TokenContext.CSharp);
        Assert.Null(link);
    }

    [Fact]
    public void Resolve_CachesSameToken()
    {
        var link1 = _resolver.Resolve("override", DocumentationResolver.TokenContext.CSharp);
        var link2 = _resolver.Resolve("override", DocumentationResolver.TokenContext.CSharp);

        Assert.Same(link1, link2);
    }

    [Fact]
    public void Resolve_DifferentContext_ReturnsDifferentResult()
    {
        var csharpLink = _resolver.Resolve("//", DocumentationResolver.TokenContext.CSharp);
        var xpathLink = _resolver.Resolve("//", DocumentationResolver.TokenContext.XPath);

        // // is not a C# keyword, but is an XPath operator
        Assert.Null(csharpLink);
        Assert.NotNull(xpathLink);
    }

    [Fact]
    public void FormatAsLink_Keyword_ReturnsHtmlAnchor()
    {
        var html = _resolver.FormatAsLink("override", DocumentationResolver.TokenContext.CSharp);

        Assert.Contains("<a href=", html);
        Assert.Contains("class=\"doc-link\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains(">override</a>", html);
    }

    [Fact]
    public void FormatAsLink_UnknownToken_ReturnsEncodedText()
    {
        var html = _resolver.FormatAsLink("myVariable", DocumentationResolver.TokenContext.CSharp);

        Assert.Equal("myVariable", html);
        Assert.DoesNotContain("<a", html);
    }

    [Fact]
    public void FormatAsLink_HtmlSpecialChars_AreEncoded()
    {
        var html = _resolver.FormatAsLink("<script>", DocumentationResolver.TokenContext.CSharp);

        Assert.Contains("&lt;", html);
        Assert.Contains("&gt;", html);
    }
}
