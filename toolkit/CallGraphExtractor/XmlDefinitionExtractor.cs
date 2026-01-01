using System.Xml;
using System.Xml.Linq;

namespace CallGraphExtractor;

/// <summary>
/// Extracts property definitions from 7D2D game XML files.
/// Parses items.xml, blocks.xml, recipes.xml, etc. to catalog all properties
/// that code might access via GetValue/GetInt/etc.
/// </summary>
public class XmlDefinitionExtractor
{
    private readonly bool _verbose;
    private int _definitionCount = 0;
    
    // Known XML file types and their element patterns
    private static readonly Dictionary<string, string[]> KnownFilePatterns = new()
    {
        ["items.xml"] = ["item"],
        ["blocks.xml"] = ["block"],
        ["recipes.xml"] = ["recipe"],
        ["progression.xml"] = ["progression", "skill", "perk", "book", "crafting_skill"],
        ["buffs.xml"] = ["buff"],
        ["entityclasses.xml"] = ["entity_class"],
        ["loot.xml"] = ["lootcontainer", "lootgroup", "lootprobtemplate", "lootqualitytemplate"],
        ["biomes.xml"] = ["biome", "biomespawnrule"],
        ["quests.xml"] = ["quest", "objective", "quest_tier_list"],
        ["gamestages.xml"] = ["gamestage", "spawner"],
        ["gameevents.xml"] = ["action_sequence", "game_event", "twitch_action"],
        ["sounds.xml"] = ["SoundDataNode"],
        ["vehicles.xml"] = ["vehicle"],
        ["traders.xml"] = ["trader", "trader_info"],
        ["materials.xml"] = ["material"],
        ["rwgmixer.xml"] = ["prefab_rule", "cell_rule", "hub_rule"],
        ["shapes.xml"] = ["shape"],
        ["item_modifiers.xml"] = ["item_modifier"],
        ["weathersurvival.xml"] = ["weather"],
        ["archetypes.xml"] = ["archetype"],
        ["dialogs.xml"] = ["dialog"],
        ["npc.xml"] = ["npc"],
        ["spawning.xml"] = ["spawn", "biome_spawn"],
        ["ui_display.xml"] = ["ui_display_info", "item_display_info"],
        ["twitch.xml"] = ["twitch_action", "twitch_vote", "twitch_extension"],
        ["physicsbodies.xml"] = ["physics_body"],
        ["loadingscreen.xml"] = ["tip"],
        ["localization.txt"] = [], // Special handling needed
    };
    
    public XmlDefinitionExtractor(bool verbose = false)
    {
        _verbose = verbose;
    }
    
    /// <summary>
    /// Extract XML definitions from all XML files in the game's Data/Config folder.
    /// </summary>
    public void ExtractFromGameData(string gameDataConfigPath, SqliteWriter db)
    {
        if (!Directory.Exists(gameDataConfigPath))
        {
            Console.WriteLine($"Warning: Game Data/Config path not found: {gameDataConfigPath}");
            return;
        }
        
        Console.WriteLine($"Extracting XML definitions from {gameDataConfigPath}...");
        
        var xmlFiles = Directory.GetFiles(gameDataConfigPath, "*.xml", SearchOption.TopDirectoryOnly);
        Console.WriteLine($"  Found {xmlFiles.Length} XML files");
        
        using var transaction = db.BeginTransaction();
        
        foreach (var xmlFile in xmlFiles)
        {
            var fileName = Path.GetFileName(xmlFile);
            try
            {
                ExtractFromFile(xmlFile, fileName, db);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Failed to parse {fileName}: {ex.Message}");
            }
        }
        
        transaction.Commit();
        
        Console.WriteLine($"  Extracted {_definitionCount:N0} XML definitions");
    }
    
    /// <summary>
    /// Extract definitions from a single XML file.
    /// </summary>
    private void ExtractFromFile(string filePath, string fileName, SqliteWriter db)
    {
        var beforeCount = _definitionCount;
        
        // Load XML with line info
        var doc = XDocument.Load(filePath, LoadOptions.SetLineInfo);
        var root = doc.Root;
        if (root == null) return;
        
        // Get known element types for this file, or use generic extraction
        var knownElements = KnownFilePatterns.GetValueOrDefault(fileName.ToLower(), Array.Empty<string>());
        
        // Extract from root's children
        foreach (var element in root.Elements())
        {
            ExtractElement(element, fileName, "/" + root.Name.LocalName, db, 0);
        }
        
        if (_verbose && _definitionCount > beforeCount)
        {
            Console.WriteLine($"    {fileName}: {_definitionCount - beforeCount} definitions");
        }
    }
    
    /// <summary>
    /// Recursively extract an element and its properties.
    /// </summary>
    private void ExtractElement(XElement element, string fileName, string parentXPath, SqliteWriter db, int depth)
    {
        var elementName = element.Name.LocalName;
        var lineInfo = (IXmlLineInfo)element;
        var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : (int?)null;
        
        // Build xpath for this element
        var nameAttr = element.Attribute("name")?.Value 
                    ?? element.Attribute("id")?.Value 
                    ?? element.Attribute("Name")?.Value;
        
        string xpath;
        if (!string.IsNullOrEmpty(nameAttr))
        {
            xpath = $"{parentXPath}/{elementName}[@name=\"{nameAttr}\"]";
        }
        else
        {
            // Use index if no name attribute
            var siblings = element.Parent?.Elements(elementName).ToList();
            var index = siblings?.IndexOf(element) ?? 0;
            xpath = $"{parentXPath}/{elementName}[{index + 1}]";
        }
        
        // Determine element type (block, item, recipe, etc.)
        var elementType = DetermineElementType(element, fileName);
        
        // Extract the element itself
        var classAttr = element.Attribute("class")?.Value;
        
        // Record the element definition
        db.InsertXmlDefinition(
            fileName: fileName,
            elementType: elementType,
            elementName: nameAttr,
            elementXpath: xpath,
            propertyName: null,
            propertyValue: null,
            propertyClass: classAttr,
            lineNumber: lineNumber
        );
        _definitionCount++;
        
        // Extract property children
        foreach (var propElement in element.Elements("property"))
        {
            ExtractProperty(propElement, fileName, elementType, nameAttr, xpath, db);
        }
        
        // For deeper elements (like effect_group, triggered_effect, etc.), also extract
        if (depth < 3) // Limit recursion depth
        {
            foreach (var child in element.Elements())
            {
                var childName = child.Name.LocalName;
                
                // Skip property elements (already handled above)
                if (childName == "property") continue;
                
                // Recursively extract nested elements of interest
                if (IsInterestingElement(childName))
                {
                    ExtractElement(child, fileName, xpath, db, depth + 1);
                }
            }
        }
    }
    
    /// <summary>
    /// Extract a property element.
    /// </summary>
    private void ExtractProperty(XElement propElement, string fileName, string elementType, 
                                  string? parentName, string parentXPath, SqliteWriter db)
    {
        var propName = propElement.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(propName)) return;
        
        var propValue = propElement.Attribute("value")?.Value;
        var propClass = propElement.Attribute("class")?.Value;
        
        var lineInfo = (IXmlLineInfo)propElement;
        var lineNumber = lineInfo.HasLineInfo() ? lineInfo.LineNumber : (int?)null;
        
        var xpath = $"{parentXPath}/property[@name=\"{propName}\"]";
        
        db.InsertXmlDefinition(
            fileName: fileName,
            elementType: elementType,
            elementName: parentName,
            elementXpath: xpath,
            propertyName: propName,
            propertyValue: propValue,
            propertyClass: propClass,
            lineNumber: lineNumber
        );
        _definitionCount++;
        
        // Some properties have nested properties (like effect_group)
        foreach (var nested in propElement.Elements("property"))
        {
            ExtractProperty(nested, fileName, elementType, parentName, xpath, db);
        }
    }
    
    /// <summary>
    /// Determine the element type based on the element name and file context.
    /// </summary>
    private string DetermineElementType(XElement element, string fileName)
    {
        var name = element.Name.LocalName.ToLower();
        
        // Direct matches
        if (name == "item" || name == "block" || name == "recipe" || name == "buff" 
            || name == "vehicle" || name == "shape" || name == "material")
        {
            return name;
        }
        
        // File-based inference
        return fileName.ToLower() switch
        {
            "items.xml" => "item",
            "blocks.xml" => "block",
            "recipes.xml" => "recipe",
            "buffs.xml" => "buff",
            "progression.xml" when name == "skill" => "skill",
            "progression.xml" when name == "perk" => "perk",
            "progression.xml" when name == "book" => "book",
            "progression.xml" => "progression",
            "entityclasses.xml" => "entity_class",
            "loot.xml" => name, // lootcontainer, lootgroup, etc.
            "biomes.xml" => "biome",
            "quests.xml" => name, // quest, objective, etc.
            "traders.xml" => "trader",
            "sounds.xml" => "sound",
            "vehicles.xml" => "vehicle",
            "gameevents.xml" => name,
            "gamestages.xml" => name,
            "twitch.xml" => name,
            _ => name
        };
    }
    
    /// <summary>
    /// Determine if an element is worth extracting recursively.
    /// </summary>
    private bool IsInterestingElement(string elementName)
    {
        var lower = elementName.ToLower();
        return lower switch
        {
            "effect_group" => true,
            "triggered_effect" => true,
            "passive_effect" => true,
            "requirement" => true,
            "action" => true,
            "objective" => true,
            "reward" => true,
            "item_modifier" => true,
            "drop" => true,
            "buff_modifier" => true,
            "display_entry" => true,
            "sound" => true,
            _ => false
        };
    }
    
    public int DefinitionCount => _definitionCount;
}
