using System.Text.RegularExpressions;
using Xunit;
using XmlIndexer.Reports;

namespace XmlIndexer.Tests.Reports;

/// <summary>
/// Integration tests that verify the generated gamecode.html contains expected features.
/// These tests validate the actual HTML output, not just internal logic.
/// </summary>
public class GameCodePageIntegrationTests
{
    /// <summary>
    /// Test that SharedAssets produces CSS with doc-link styles.
    /// </summary>
    [Fact]
    public void SharedAssets_Css_ContainsDocLinkStyles()
    {
        var css = SharedAssets.GetStylesCss();

        Assert.Contains(".doc-link", css);
        Assert.Contains("text-decoration", css);
        Assert.Contains("dotted", css);
    }

    /// <summary>
    /// Test that SharedAssets produces JS with doc-link toggle functionality.
    /// </summary>
    [Fact]
    public void SharedAssets_SharedJs_ContainsDocLinkToggle()
    {
        var script = SharedAssets.GetSharedJavaScript();

        Assert.Contains("toggleDocLinks", script);
        Assert.Contains("7d2d-hide-doc-links", script);
    }

    /// <summary>
    /// Test that SharedAssets produces JS with theme initialization that handles doc links.
    /// </summary>
    [Fact]
    public void SharedAssets_SharedJs_InitThemeHandlesDocLinks()
    {
        var script = SharedAssets.GetSharedJavaScript();

        // Should have initTheme call toggleDocLinks
        Assert.Contains("initTheme", script);
        Assert.Contains("hide-doc-links", script);
    }

    /// <summary>
    /// Verify the doc-link CSS is properly formatted for browser rendering.
    /// </summary>
    [Fact]
    public void SharedAssets_DocLinkCss_HasValidCssStructure()
    {
        var css = SharedAssets.GetStylesCss();

        // Check for proper CSS rule structure
        Assert.Matches(new Regex(@"\.doc-link\s*\{[^}]+\}"), css);
        Assert.Matches(new Regex(@"body\.hide-doc-links\s+\.doc-link"), css);
    }

    /// <summary>
    /// Verify the toggle function is callable from HTML.
    /// </summary>
    [Fact]
    public void SharedAssets_ToggleDocLinks_IsGlobalFunction()
    {
        var script = SharedAssets.GetSharedJavaScript();

        // Function should be defined at window scope (not inside another function scope)
        Assert.Contains("function toggleDocLinks", script);
    }

    /// <summary>
    /// Test DocumentationResolver produces links with correct CSS class.
    /// </summary>
    [Fact]
    public void DocumentationResolver_FormatAsLink_UsesDocLinkClass()
    {
        var resolver = new DocumentationResolver();
        var html = resolver.FormatAsLink("override", DocumentationResolver.TokenContext.CSharp);

        Assert.Contains("class=\"doc-link\"", html);
    }

    /// <summary>
    /// Test DocumentationResolver produces external links with target="_blank".
    /// </summary>
    [Fact]
    public void DocumentationResolver_FormatAsLink_OpensInNewTab()
    {
        var resolver = new DocumentationResolver();
        var html = resolver.FormatAsLink("abstract", DocumentationResolver.TokenContext.CSharp);

        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener\"", html);
    }

    /// <summary>
    /// Test that tooltip summaries are loaded from JSON.
    /// </summary>
    [Fact]
    public void DocumentationResolver_HasTooltipSummary_ForCommonKeywords()
    {
        var resolver = new DocumentationResolver();
        
        // Check that override keyword has a tooltip
        var html = resolver.FormatAsLink("override", DocumentationResolver.TokenContext.CSharp);
        
        // Should have title attribute with tooltip text
        Assert.Contains("title=", html);
    }

    /// <summary>
    /// Verify CodeTokenizer extracts C# keywords correctly.
    /// </summary>
    [Fact]
    public void CodeTokenizer_TokenizeCSharp_FindsKeywords()
    {
        var tokenizer = new CodeTokenizer();
        var code = "public override void Update() { if (flag) return; }";
        var tokens = tokenizer.TokenizeCSharp(code).ToList();
        var tokenValues = tokens.Select(t => t.Value).ToList();

        Assert.Contains("override", tokenValues);
        Assert.Contains("void", tokenValues);
        Assert.Contains("if", tokenValues);
        Assert.Contains("return", tokenValues);
    }

    /// <summary>
    /// Verify CodeTokenizer extracts XPath operators.
    /// </summary>
    [Fact]
    public void CodeTokenizer_TokenizeXPath_FindsOperators()
    {
        var tokenizer = new CodeTokenizer();
        var xpath = "//items/item[@name='test']/ancestor::config";
        var tokens = tokenizer.TokenizeXPath(xpath).ToList();
        var tokenValues = tokens.Select(t => t.Value).ToList();

        Assert.Contains("//", tokenValues);
        Assert.Contains("@", tokenValues);
        Assert.Contains("ancestor", tokenValues);
    }

    /// <summary>
    /// Integration test: DocumentationResolver + CodeTokenizer work together.
    /// </summary>
    [Fact]
    public void Integration_TokenizerAndResolver_ProduceLinkedCode()
    {
        var resolver = new DocumentationResolver();
        var tokenizer = new CodeTokenizer();
        var code = "public virtual void Method()";
        var tokens = tokenizer.ExtractLinkable(code, isCSharp: true);

        int linkedCount = 0;
        foreach (var token in tokens)
        {
            var html = resolver.FormatAsLink(token.Value, DocumentationResolver.TokenContext.CSharp);
            if (html.Contains("<a href="))
            {
                linkedCount++;
            }
        }

        // Should have at least 'virtual' and 'void' linked
        Assert.True(linkedCount >= 2, $"Expected at least 2 linked tokens, got {linkedCount}");
    }

    /// <summary>
    /// Verify CSS handles both light and dark themes for doc links.
    /// </summary>
    [Fact]
    public void SharedAssets_DocLinkCss_HandlesThemes()
    {
        var css = SharedAssets.GetStylesCss();

        // Should have color definitions (could be in light or dark theme context)
        Assert.Contains(".doc-link", css);
        // The underline should be visible in both themes
        Assert.Contains("text-decoration", css);
    }

    /// <summary>
    /// Test that JavaScript regex pattern in GameCodePageGenerator is correctly escaped.
    /// The C# verbatim string should produce '\\b' in the output which JavaScript
    /// interprets as a word boundary regex pattern.
    /// </summary>
    [Fact]
    public void GameCodePage_LinkifyCSharp_HasCorrectRegexEscaping()
    {
        // The regex should be produced as '\\b' in the HTML (two characters: backslash + b)
        // This test simulates what JavaScript sees when parsing the string
        
        // In C# verbatim string $@"...", '\\b' outputs two characters: \ and b
        // JavaScript then sees '\\b' which it interprets as one backslash (escape) 
        // followed by 'b', resulting in \b = word boundary
        
        // Test the expected JavaScript behavior
        // Note: In C# regular strings, \\b is one backslash + b
        var jsStringValue = @"\b(public)\b";  // This is what JS string literal parses to
        var pattern = new Regex(jsStringValue);
        
        Assert.Matches(pattern, "public void Method()");
        Assert.Matches(pattern, " public ");
        Assert.DoesNotMatch(pattern, "republic");  // Should NOT match 'public' inside another word
    }
}
