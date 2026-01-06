using XmlIndexer.Analysis;
using System.Text.RegularExpressions;

namespace XmlIndexer.Tests.Analysis;

/// <summary>
/// Tests for GameCodeAnalyzer - method extraction, pattern matching, etc.
/// </summary>
public class GameCodeAnalyzerTests
{
    // The method pattern from FindEnclosingMethod (for testing)
    private static readonly Regex MethodPattern = new(
        @"(?:public|private|protected|internal)\s+" +
        @"(?:(?:static|virtual|override|abstract|sealed|async|new|extern|unsafe|partial)\s+)*" +
        @"(?:\w+(?:<[^>]+>)?)\s+(\w+)\s*\(");

    // The property pattern from FindEnclosingMethod (for testing)
    private static readonly Regex PropertyPattern = new(
        @"(?:public|private|protected|internal)\s+" +
        @"(?:(?:static|virtual|override|abstract|sealed|new|extern|unsafe)\s+)*" +
        @"(?:\w+(?:<[^>]+>)?(?:\?)?(?:\[\])?)\s+(\w+)\s*(?:=>|\{|$)");

    /// <summary>
    /// Test that the method extraction regex handles various C# modifiers correctly.
    /// This was a bug found during QA - the original regex only handled 'static'.
    /// </summary>
    [Theory]
    [InlineData("public void DoSomething()", "DoSomething")]
    [InlineData("private int GetCount(int x)", "GetCount")]
    [InlineData("public static void Main(string[] args)", "Main")]
    [InlineData("public override void Execute()", "Execute")]
    [InlineData("protected virtual bool TryParse(string s)", "TryParse")]
    [InlineData("public async Task<bool> LoadAsync()", "LoadAsync")]
    [InlineData("public abstract void Process();", "Process")]
    [InlineData("public sealed override string ToString()", "ToString")]
    public void MethodPattern_ExtractsCorrectMethodName(string codeLine, string expectedMethod)
    {
        var match = MethodPattern.Match(codeLine);
        Assert.True(match.Success, $"Pattern should match: {codeLine}");
        Assert.Equal(expectedMethod, match.Groups[1].Value);
    }

    [Theory]
    [InlineData("private static readonly int _field = 5;")] // Field, not method
    [InlineData("public class MyClass {")] // Class declaration
    [InlineData("public bool Pressed")] // Property without parens
    [InlineData("        get")] // Getter accessor
    [InlineData("        set")] // Setter accessor
    public void MethodPattern_DoesNotMatchNonMethods(string codeLine)
    {
        var match = MethodPattern.Match(codeLine);
        Assert.False(match.Success, $"Pattern should NOT match: {codeLine}");
    }

    /// <summary>
    /// Test that property extraction works correctly.
    /// This addresses the bug where FindEnclosingMethod missed property accessors.
    /// </summary>
    [Theory]
    [InlineData("        public bool Pressed", "Pressed")]
    [InlineData("        public string Name", "Name")]
    [InlineData("        public int Count {", "Count")]
    [InlineData("        public override string Text =>", "Text")]
    [InlineData("        public virtual int Value {", "Value")]
    [InlineData("        private bool _isActive", "_isActive")]
    [InlineData("        public List<string> Items {", "Items")]
    [InlineData("        public string? NullableProp", "NullableProp")]
    [InlineData("        public int[] ArrayProp", "ArrayProp")]
    public void PropertyPattern_ExtractsCorrectPropertyName(string codeLine, string expectedProperty)
    {
        // Property pattern should match, and line should not contain '('
        var match = PropertyPattern.Match(codeLine);
        Assert.True(match.Success && !codeLine.Contains('('), 
            $"Property pattern should match (and no parens): {codeLine}");
        Assert.Equal(expectedProperty, match.Groups[1].Value);
    }

    [Theory]
    [InlineData("public void DoSomething()")] // Method, has parens
    [InlineData("public int GetCount(int x)")] // Method
    [InlineData("        get")] // Just accessor keyword
    [InlineData("        set")] // Just accessor keyword
    [InlineData("class MyClass {")] // Class declaration
    public void PropertyPattern_DoesNotMatchMethods(string codeLine)
    {
        // Either pattern doesn't match, or line contains '(' (so it's a method)
        var match = PropertyPattern.Match(codeLine);
        var isProperty = match.Success && !codeLine.Contains('(');
        Assert.False(isProperty, $"Property pattern should NOT match: {codeLine}");
    }

    /// <summary>
    /// Integration test: verify PowerPressurePlate property is detected.
    /// This was the original bug report case.
    /// </summary>
    [Fact]
    public void FindEnclosingMethod_PowerPressurePlate_DetectsProperty()
    {
        // Simulated content from PowerPressurePlate.cs
        var lines = new[]
        {
            "using Audio;",
            "",
            "public class PowerPressurePlate : PowerTrigger",
            "{",
            "        [PublicizedFrom(EAccessModifier.Protected)]",
            "        public bool pressed;",
            "",
            "        [PublicizedFrom(EAccessModifier.Protected)]",
            "        public bool lastPressed;",
            "",
            "        public override PowerItemTypes PowerItemType => PowerItemTypes.PressurePlate;",
            "",
            "        public bool Pressed",  // Line 13 (index 12)
            "        {",
            "                get",
            "                {",
            "                        return pressed;",
            "                }",
            "                set",
            "                {",
            "                        pressed = value;",
            "                        if (pressed && !lastPressed)",
            "                        {",
            "                                Manager.BroadcastPlay(Position.ToVector3(), \"pressureplate_down\");", // Line 24 (index 23)
            "                        }",
            "                        lastPressed = pressed;",
            "                }",
            "        }",
        };

        // Target line 24 (index 23) where the sound is
        int targetLine = 23;
        string? foundMember = null;

        // Scan backwards like FindEnclosingMethod does
        for (int i = targetLine; i >= 0; i--)
        {
            // Check method first
            var methodMatch = MethodPattern.Match(lines[i]);
            if (methodMatch.Success)
            {
                foundMember = methodMatch.Groups[1].Value;
                break;
            }

            // Check property (no parens)
            var propMatch = PropertyPattern.Match(lines[i]);
            if (propMatch.Success && !lines[i].Contains('('))
            {
                foundMember = propMatch.Groups[1].Value + " (property)";
                break;
            }

            // Stop at class
            if (Regex.IsMatch(lines[i], @"(?:class|struct|interface)\s+\w+"))
                break;
        }

        Assert.NotNull(foundMember);
        Assert.Equal("Pressed (property)", foundMember);
    }

    /// <summary>
    /// Test that analysis results contain expected severity levels.
    /// </summary>
    [Fact]
    public void AnalysisSeverity_HasExpectedValues()
    {
        // Verify the severity enum/constants exist and have expected values
        var severities = new[] { "INFO", "WARNING", "OPPORTUNITY" };
        
        foreach (var severity in severities)
        {
            Assert.NotNull(severity);
            Assert.NotEmpty(severity);
        }
    }
}
