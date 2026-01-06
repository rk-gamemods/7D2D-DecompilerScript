namespace XmlIndexer.Tests;

/// <summary>
/// Manual unit tests for JsClickBuilder.
/// Run with: dotnet run -- test-jsclick
/// </summary>
public static class JsClickBuilderTests
{
    public static int Run()
    {
        Console.WriteLine("=== JsClickBuilder Tests ===\n");
        int passed = 0, failed = 0;

        // Test 1: Basic anchor generation
        Test("Anchor generates valid structure", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("doThing", new[] { "arg1" }, "Click Me");
            if (!html.StartsWith("<a ")) return "Should start with <a";
            if (!html.EndsWith("</a>")) return "Should end with </a>";
            if (!html.Contains("href=\"#\"")) return "Missing href=\"#\"";
            return null;
        }, ref passed, ref failed);

        // Test 2: Anchor has js-click class
        Test("Anchor has js-click class", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "text");
            if (!html.Contains("class=\"js-click\"")) return "Missing js-click class";
            return null;
        }, ref passed, ref failed);

        // Test 3: Anchor has data-action
        Test("Anchor has data-action", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("myFunction", new[] { "x" }, "text");
            if (!html.Contains("data-action=\"myFunction\"")) return "Missing data-action";
            return null;
        }, ref passed, ref failed);

        // Test 4: Anchor has data-arg0
        Test("Anchor has data-arg0", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "firstArg" }, "text");
            if (!html.Contains("data-arg0=\"firstArg\"")) return "Missing data-arg0";
            return null;
        }, ref passed, ref failed);

        // Test 5: Anchor includes inner HTML
        Test("Anchor includes inner HTML", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "My Link Text");
            if (!html.Contains(">My Link Text</a>")) return "Missing inner content";
            return null;
        }, ref passed, ref failed);

        // Test 6: HTML encoding of single quotes
        Test("Encodes single quotes", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "O'Brien" }, "Name");
            if (!html.Contains("data-arg0=\"O&#39;Brien\""))
                return $"Quote not encoded correctly. Got: {ExtractDataArg0(html)}";
            return null;
        }, ref passed, ref failed);

        // Test 7: HTML encoding of double quotes
        Test("Encodes double quotes", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "Say \"Hello\"" }, "Text");
            if (!html.Contains("data-arg0=\"Say &quot;Hello&quot;\""))
                return $"Double quotes not encoded. Got: {ExtractDataArg0(html)}";
            return null;
        }, ref passed, ref failed);

        // Test 8: HTML encoding of ampersands
        Test("Encodes ampersands", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "A&B" }, "Text");
            if (!html.Contains("data-arg0=\"A&amp;B\""))
                return $"Ampersand not encoded. Got: {ExtractDataArg0(html)}";
            return null;
        }, ref passed, ref failed);

        // Test 9: HTML encoding of less-than
        Test("Encodes less-than", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "a<b" }, "Text");
            if (!html.Contains("data-arg0=\"a&lt;b\""))
                return $"Less-than not encoded. Got: {ExtractDataArg0(html)}";
            return null;
        }, ref passed, ref failed);

        // Test 10: HTML encoding of greater-than
        Test("Encodes greater-than", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "a>b" }, "Text");
            if (!html.Contains("data-arg0=\"a&gt;b\""))
                return $"Greater-than not encoded. Got: {ExtractDataArg0(html)}";
            return null;
        }, ref passed, ref failed);

        // Test 11: Multiple arguments
        Test("Multiple arguments work", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "a", "b", "c" }, "Text");
            if (!html.Contains("data-arg0=\"a\"")) return "Missing arg0";
            if (!html.Contains("data-arg1=\"b\"")) return "Missing arg1";
            if (!html.Contains("data-arg2=\"c\"")) return "Missing arg2";
            return null;
        }, ref passed, ref failed);

        // Test 12: Empty args array
        Test("Empty args array works", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", Array.Empty<string>(), "Text");
            if (html.Contains("data-arg")) return "Should have no data-arg attributes";
            return null;
        }, ref passed, ref failed);

        // Test 13: Optional style attribute
        Test("Style attribute included when provided", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "Text", style: "color:red;font-weight:bold");
            if (!html.Contains("style=\"color:red;font-weight:bold\"")) return "Missing style";
            return null;
        }, ref passed, ref failed);

        // Test 14: Style attribute omitted when null
        Test("Style attribute omitted when null", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "Text", style: null);
            if (html.Contains("style=")) return "Should not have style attribute";
            return null;
        }, ref passed, ref failed);

        // Test 15: Title attribute
        Test("Title attribute works", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "Text", title: "Click here");
            if (!html.Contains("title=\"Click here\"")) return "Missing title";
            return null;
        }, ref passed, ref failed);

        // Test 16: Additional CSS class
        Test("Additional CSS class appended", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new[] { "x" }, "Text", cssClass: "my-class");
            if (!html.Contains("class=\"js-click my-class\"")) return "CSS class not appended correctly";
            return null;
        }, ref passed, ref failed);

        // Test 17: Span element
        Test("Span generates correctly", () =>
        {
            var html = Utils.JsClickBuilder.Span("fn", new[] { "x" }, "Text");
            if (!html.StartsWith("<span")) return "Should start with <span";
            if (!html.EndsWith("</span>")) return "Should end with </span>";
            if (!html.Contains("class=\"js-click\"")) return "Missing js-click class";
            return null;
        }, ref passed, ref failed);

        // Test 18: Event handler has required parts
        Test("Event handler has click listener", () =>
        {
            var js = Utils.JsClickBuilder.GenerateEventHandler();
            if (!js.Contains("document.addEventListener('click'"))
                return "Missing click event listener";
            return null;
        }, ref passed, ref failed);

        // Test 19: Event handler finds js-click elements
        Test("Event handler finds js-click", () =>
        {
            var js = Utils.JsClickBuilder.GenerateEventHandler();
            if (!js.Contains(".closest('.js-click')"))
                return "Missing js-click selector";
            return null;
        }, ref passed, ref failed);

        // Test 20: Event handler extracts action
        Test("Event handler extracts action", () =>
        {
            var js = Utils.JsClickBuilder.GenerateEventHandler();
            if (!js.Contains("dataset.action"))
                return "Missing action extraction";
            return null;
        }, ref passed, ref failed);

        // Test 21: Event handler collects args
        Test("Event handler collects args", () =>
        {
            var js = Utils.JsClickBuilder.GenerateEventHandler();
            if (!js.Contains("dataset['arg' + i]"))
                return "Missing arg collection loop";
            return null;
        }, ref passed, ref failed);

        // Test 22: Event handler calls function
        Test("Event handler calls window function", () =>
        {
            var js = Utils.JsClickBuilder.GenerateEventHandler();
            if (!js.Contains("window[action]"))
                return "Missing window function call";
            return null;
        }, ref passed, ref failed);

        // Test 23: SimpleAnchor convenience method
        Test("SimpleAnchor works", () =>
        {
            var html = Utils.JsClickBuilder.SimpleAnchor("fn", "arg", "Text");
            if (!html.Contains("data-action=\"fn\"")) return "Missing action";
            if (!html.Contains("data-arg0=\"arg\"")) return "Missing arg0";
            return null;
        }, ref passed, ref failed);

        // Test 24: TwoArgAnchor convenience method
        Test("TwoArgAnchor works", () =>
        {
            var html = Utils.JsClickBuilder.TwoArgAnchor("fn", "a", "b", "Text");
            if (!html.Contains("data-arg0=\"a\"")) return "Missing arg0";
            if (!html.Contains("data-arg1=\"b\"")) return "Missing arg1";
            return null;
        }, ref passed, ref failed);

        // Test 25: Null arg handled gracefully
        Test("Null arg becomes empty string", () =>
        {
            var html = Utils.JsClickBuilder.Anchor("fn", new string[] { null! }, "Text");
            if (!html.Contains("data-arg0=\"\"")) return "Null should become empty";
            return null;
        }, ref passed, ref failed);

        // Test 26: Complex real-world example
        Test("Complex class name with special chars", () =>
        {
            var className = "MyClass<T>";
            var html = Utils.JsClickBuilder.Anchor("filterByClass", new[] { className }, Utils.JsClickBuilder.HtmlEncode(className));
            if (!html.Contains("data-arg0=\"MyClass&lt;T&gt;\""))
                return $"Class name not encoded correctly in data attribute";
            if (!html.Contains(">MyClass&lt;T&gt;</a>"))
                return "Class name not encoded correctly in content";
            return null;
        }, ref passed, ref failed);

        Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
        return failed > 0 ? 1 : 0;
    }

    private static string ExtractDataArg0(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, @"data-arg0=""([^""]*)""");
        return match.Success ? match.Groups[1].Value : "(not found)";
    }

    private static void Test(string name, Func<string?> test, ref int passed, ref int failed)
    {
        try
        {
            var error = test();
            if (error == null)
            {
                Console.WriteLine($"  ✓ {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: {error}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {name}: EXCEPTION - {ex.Message}");
            failed++;
        }
    }
}
