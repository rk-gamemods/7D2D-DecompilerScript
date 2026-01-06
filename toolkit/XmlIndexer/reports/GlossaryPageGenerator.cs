using System.Text;
using XmlIndexer.Models;

namespace XmlIndexer.Reports;

/// <summary>
/// Generates the glossary page (glossary.html) with searchable reference for all terms and patterns.
/// </summary>
public static class GlossaryPageGenerator
{
    public static string Generate(ReportData data, ExtendedReportData extData)
    {
        var body = new StringBuilder();

        // Page header
        body.AppendLine(@"<div class=""page-header"">");
        body.AppendLine(@"  <h1>Glossary</h1>");
        body.AppendLine(@"  <p>Reference guide for all terms, patterns, and concepts used in this report</p>");
        body.AppendLine(@"</div>");

        // Search filter
        body.AppendLine(@"<div class=""filter-bar"">");
        body.AppendLine(@"  <input type=""text"" class=""filter-search"" id=""glossary-search"" placeholder=""Search terms..."" oninput=""filterGlossary()"">");
        body.AppendLine(@"</div>");

        body.AppendLine(@"<div id=""glossary-content"">");

        // Reference Types category
        body.AppendLine(@"<div class=""glossary-category"" data-category=""reference-types"">");
        body.AppendLine(@"<h3>Reference Types</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">How entities connect to each other in the game's XML configuration.</p>");

        var referenceTypes = new[]
        {
            ("extends", "Inheritance - entity inherits all properties from a parent template. Changes to parent affect all children.",
             "<item name=\"gunPistol\" extends=\"gunHandgunT0Base\">"),
            ("property", "Direct property reference - entity uses another entity's name in a property value.",
             "<property name=\"HandItem\" value=\"meleeClubWood\"/>"),
            ("triggered_effect:AddBuff", "Buff application - this item/action applies a buff when used or triggered.",
             "<triggered_effect trigger=\"onSelfPrimaryActionEnd\" action=\"AddBuff\" buff=\"buffDrunk\"/>"),
            ("triggered_effect:RemoveBuff", "Buff removal - this item/action removes a buff when used.",
             "<triggered_effect trigger=\"onSelfSecondaryActionEnd\" action=\"RemoveBuff\" buff=\"buffDrunk\"/>"),
            ("triggered_effect:PlaySound", "Sound trigger - plays a sound effect when the trigger occurs.",
             "<triggered_effect trigger=\"onSelfPrimaryActionStart\" action=\"PlaySound\" sound=\"Weapons/Items/Use/syringe\"/>"),
            ("loot_entry", "Loot table entry - this entity can drop as loot from a container or group.",
             "<lootgroup name=\"groupTools\"><item name=\"toolPickaxeT1\" count=\"1\"/></lootgroup>"),
            ("recipe_ingredient", "Crafting ingredient - this item is required to craft something else.",
             "<ingredient name=\"resourceWood\" count=\"10\"/>"),
            ("recipe_output", "Crafting output - this is what a recipe produces when crafted.",
             "<recipe name=\"gunPistolT1\" count=\"1\"/>"),
            ("group_member", "Spawn group member - entity belongs to a spawn/entity group.",
             "<entitygroup name=\"ZombiesAll\"><entity name=\"zombieMarlene\" prob=\"1\"/></entitygroup>"),
            ("property:SpawnEntityName", "Entity spawn - spawns this entity under certain conditions.",
             "<property name=\"SpawnEntityName\" value=\"animalChicken\"/>"),
            ("property:LootListOnDeath", "Death loot - uses this loot table when the entity dies.",
             "<property name=\"LootListOnDeath\" value=\"12\"/>")
        };

        foreach (var (term, definition, example) in referenceTypes)
        {
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name"">{SharedAssets.HtmlEncode(term)}</div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(definition)}</div>");
            body.AppendLine($@"<div class=""term-example"">{SharedAssets.HtmlEncode(example)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        // XPath Operations category
        body.AppendLine(@"<div class=""glossary-category"" data-category=""xpath-ops"">");
        body.AppendLine(@"<h3>XPath Operations</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">How mods modify the game's XML files.</p>");

        var xpathOps = new[]
        {
            ("set", "Overwrites an existing property value. Last mod to run wins if multiple mods set the same property.",
             "<set xpath=\"/items/item[@name='gunPistol']/property[@name='DamageEntity']/@value\">50</set>"),
            ("setattribute", "Sets an attribute on an existing element. Similar to 'set' but targets attributes specifically.",
             "<setattribute xpath=\"/items/item[@name='gunPistol']\" name=\"extends\">gunHandgunBase</setattribute>"),
            ("append", "Adds new content as the last child of an element. Safe - multiple mods can append without conflict.",
             "<append xpath=\"/items/item[@name='gunPistol']\">\n  <property name=\"CustomProp\" value=\"1\"/>\n</append>"),
            ("insertBefore", "Adds new content before a specific element. Useful for controlling order.",
             "<insertBefore xpath=\"/recipes/recipe[@name='gunPistolT1']\"><!-- new recipe here --></insertBefore>"),
            ("insertAfter", "Adds new content after a specific element.",
             "<insertAfter xpath=\"/items/item[@name='gunPistol']\"><!-- new item here --></insertAfter>"),
            ("remove", "Removes an element from the XML. Dangerous if other mods depend on this content.",
             "<remove xpath=\"/items/item[@name='obsoleteWeapon']\"/>")
        };

        foreach (var (term, definition, example) in xpathOps)
        {
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name"">{SharedAssets.HtmlEncode(term)}</div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(definition)}</div>");
            body.AppendLine($@"<div class=""term-example"">{SharedAssets.HtmlEncode(example)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        // Severity Patterns category
        body.AppendLine(@"<div class=""glossary-category"" data-category=""severity"">");
        body.AppendLine(@"<h3>Conflict Severity Patterns</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">How we classify potential mod conflicts.</p>");

        var severityPatterns = new[]
        {
            ("HIGH: Remove + Modify", "One mod removes an entity while another modifies it. The modify operation will fail or have no effect.",
             "Mod A: remove xpath=\"/items/item[@name='X']\" | Mod B: set value on item X"),
            ("HIGH: Remove + C# Dependency", "A mod removes content that another mod's C# code depends on. Can cause runtime errors.",
             "Mod A removes 'buffBase' | Mod B's code calls BuffManager.AddBuff(\"buffBase\")"),
            ("MEDIUM: Multiple Set Operations", "Multiple mods set the same property. Only the last mod in load order takes effect.",
             "Mod A sets DamageEntity=50 | Mod B sets DamageEntity=100 → Result: 100"),
            ("MEDIUM: Conflicting Extends", "Multiple mods change what an entity extends/inherits from.",
             "Mod A: item extends gunBase | Mod B: item extends meleeBase"),
            ("LOW: Multiple Appends", "Multiple mods append content to the same entity. Usually compatible but may cause duplicates.",
             "Both Mod A and B append new properties to the same item"),
            ("NONE: Independent Operations", "Mods modify different aspects of the same entity without overlap.",
             "Mod A sets damage | Mod B sets reload time → Both changes apply")
        };

        foreach (var (term, definition, example) in severityPatterns)
        {
            var sevLevel = term.Split(':')[0].Trim();
            var sevClass = sevLevel switch
            {
                "HIGH" => "tag-high",
                "MEDIUM" => "tag-medium",
                "LOW" => "tag-low",
                _ => "tag-info"
            };
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name""><span class=""tag {sevClass}"">{sevLevel}</span> {SharedAssets.HtmlEncode(term.Split(':').Last().Trim())}</div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(definition)}</div>");
            body.AppendLine($@"<div class=""term-example"">{SharedAssets.HtmlEncode(example)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        // Entity Types category with live counts
        body.AppendLine(@"<div class=""glossary-category"" data-category=""entity-types"">");
        body.AppendLine(@"<h3>Entity Types</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">Types of game definitions and their counts in your database.</p>");

        var entityTypeDescriptions = new Dictionary<string, string>
        {
            { "item", "Holdable items: weapons, tools, food, resources, etc." },
            { "block", "Placeable blocks: building materials, furniture, traps, etc." },
            { "buff", "Status effects: temporary stat modifiers, debuffs, etc." },
            { "recipe", "Crafting recipes: how items are crafted from ingredients." },
            { "entity_class", "Entity classes: zombies, animals, NPCs, etc." },
            { "vehicle", "Drivable vehicles: bikes, cars, gyrocopters." },
            { "loot_group", "Loot groups: collections of items that can spawn together." },
            { "loot_container", "Loot containers: define what a searchable container can contain." },
            { "quest", "Quests: objectives the player can complete for rewards." },
            { "sound", "Sound definitions: audio assets and their properties." },
            { "skill", "Skills: character skills that can be learned/upgraded." },
            { "perk", "Perks: permanent character bonuses." },
            { "trader", "Traders: NPCs that buy/sell items." },
            { "biome", "Biomes: world generation terrain types." },
            { "prefab", "Prefabs: pre-built structures that spawn in the world." }
        };

        foreach (var (type, count) in data.DefinitionsByType.OrderByDescending(kv => kv.Value))
        {
            var description = entityTypeDescriptions.GetValueOrDefault(type, "Game configuration data.");
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name"">{SharedAssets.HtmlEncode(type)} <span class=""text-dim"">({count:N0})</span></div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(description)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        // Health Status category
        body.AppendLine(@"<div class=""glossary-category"" data-category=""health"">");
        body.AppendLine(@"<h3>Mod Health Status</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">What the health indicators mean for each mod.</p>");

        var healthStatuses = new[]
        {
            ("Healthy", "tag-healthy", "Mod appears to function correctly. No detected conflicts with other mods or missing dependencies."),
            ("Review", "tag-review", "Mod may have issues that need attention. Could include intentional removals or minor conflicts."),
            ("Broken", "tag-broken", "Mod has critical issues. Removes content needed by other mods or has unresolved C# dependencies.")
        };

        foreach (var (status, cssClass, description) in healthStatuses)
        {
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name""><span class=""tag {cssClass}"">{status}</span></div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(description)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        // C# Integration category
        body.AppendLine(@"<div class=""glossary-category"" data-category=""csharp"">");
        body.AppendLine(@"<h3>C# Mod Integration</h3>");
        body.AppendLine(@"<p class=""text-muted"" style=""margin-bottom: 1rem;"">How C# mods interact with the game.</p>");

        var csharpTerms = new[]
        {
            ("Harmony Patch", "A technique to modify game code at runtime without changing the original DLL. Can run code before (Prefix), after (Postfix), or transform (Transpiler) methods."),
            ("Prefix Patch", "Runs before the original method. Can skip the original method entirely by returning false."),
            ("Postfix Patch", "Runs after the original method. Can modify the return value."),
            ("Transpiler Patch", "Modifies the IL code of a method at runtime. Most powerful but also most complex."),
            ("Class Extension", "A mod class that inherits from a game class (like ItemAction, Block, Entity) to add new behavior."),
            ("IModApi", "Interface that mods implement to be loaded by the game. Entry point for mod initialization.")
        };

        foreach (var (term, description) in csharpTerms)
        {
            body.AppendLine($@"<div class=""glossary-term"">");
            body.AppendLine($@"<div class=""term-name"">{SharedAssets.HtmlEncode(term)}</div>");
            body.AppendLine($@"<div class=""term-def"">{SharedAssets.HtmlEncode(description)}</div>");
            body.AppendLine(@"</div>");
        }
        body.AppendLine(@"</div>");

        body.AppendLine(@"</div>"); // end glossary-content

        var script = @"
function filterGlossary() {
  const query = document.getElementById('glossary-search').value.toLowerCase();

  document.querySelectorAll('.glossary-term').forEach(term => {
    const text = term.textContent.toLowerCase();
    term.style.display = text.includes(query) ? '' : 'none';
  });

  // Hide empty categories
  document.querySelectorAll('.glossary-category').forEach(cat => {
    const visibleTerms = cat.querySelectorAll('.glossary-term');
    const hasVisible = Array.from(visibleTerms).some(t => t.style.display !== 'none');
    cat.style.display = hasVisible || !query ? '' : 'none';
  });
}
";

        return SharedAssets.WrapPage("Glossary", "glossary.html", body.ToString(), script);
    }
}
