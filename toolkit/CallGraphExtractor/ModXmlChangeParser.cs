using System.Xml.Linq;

namespace CallGraphExtractor;

/// <summary>
/// Parses mod Config/*.xml files to extract XML modifications (xpath set/append/remove operations).
/// 7D2D mods use xpath-style XML to modify game XML files.
/// </summary>
public class ModXmlChangeParser
{
    private readonly SqliteWriter _db;
    private readonly bool _verbose;
    private int _changeCount;
    
    public ModXmlChangeParser(SqliteWriter db, bool verbose = false)
    {
        _db = db;
        _verbose = verbose;
    }
    
    public int ChangeCount => _changeCount;
    
    /// <summary>
    /// Parse XML changes for a specific mod.
    /// </summary>
    public void ParseModXmlChanges(string modPath, long modId)
    {
        var configDir = Path.Combine(modPath, "Config");
        if (!Directory.Exists(configDir))
            return;
        
        var xmlFiles = Directory.GetFiles(configDir, "*.xml", SearchOption.AllDirectories);
        
        foreach (var xmlFile in xmlFiles)
        {
            try
            {
                ParseXmlFile(xmlFile, modId);
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.WriteLine($"      Warning: Failed to parse {Path.GetFileName(xmlFile)}: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Parse a single mod XML file for xpath operations.
    /// </summary>
    private void ParseXmlFile(string filePath, long modId)
    {
        var doc = XDocument.Load(filePath);
        var root = doc.Root;
        if (root == null) return;
        
        // Determine target file from filename or element structure
        var fileName = Path.GetFileName(filePath);
        var targetFile = DetermineTargetFile(fileName, root);
        
        // Look for xpath operations
        ParseXpathOperations(root, modId, targetFile);
        
        // Also look for traditional append/set elements
        ParseTraditionalOperations(root, modId, targetFile);
    }
    
    /// <summary>
    /// Parse xpath attribute-based operations.
    /// Example: <set xpath="/items/item[@name='gunPistol']/property[@name='Tags']/@value">...</set>
    /// </summary>
    private void ParseXpathOperations(XElement root, long modId, string targetFile)
    {
        // Common xpath operation elements
        var operationElements = new[] { "set", "append", "insertAfter", "insertBefore", "remove", "removeattribute" };
        
        foreach (var elementName in operationElements)
        {
            foreach (var element in root.Descendants().Where(e => 
                e.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)))
            {
                var xpath = element.Attribute("xpath")?.Value;
                if (string.IsNullOrEmpty(xpath))
                    continue;
                
                var operation = element.Name.LocalName.ToLower();
                var newValue = element.Value;
                
                // Try to extract property name from xpath
                var propertyName = ExtractPropertyNameFromXpath(xpath);
                
                _db.InsertXmlChange(
                    modId: modId,
                    xmlFile: DetermineTargetFromXpath(xpath, targetFile),
                    xpath: xpath,
                    operation: operation,
                    propertyName: propertyName,
                    oldValue: null, // We don't know the old value
                    newValue: string.IsNullOrEmpty(newValue) ? null : newValue
                );
                _changeCount++;
                
                if (_verbose)
                    Console.WriteLine($"      {operation}: {xpath}");
            }
        }
    }
    
    /// <summary>
    /// Parse traditional 7D2D mod operations (append blocks, items, etc).
    /// Example: <append xpath="/items"><item name="myCustomItem">...</item></append>
    /// </summary>
    private void ParseTraditionalOperations(XElement root, long modId, string targetFile)
    {
        // Look for configs element which is common wrapper
        var configRoot = root.Name.LocalName == "configs" ? root : 
                        root.Element("configs") ?? root;
        
        // Handle appendrecipe, appendblock, appenditem, etc.
        foreach (var element in configRoot.Elements())
        {
            var elementName = element.Name.LocalName.ToLower();
            
            // Skip xpath operation elements (handled above)
            if (elementName is "set" or "append" or "insertafter" or "insertbefore" or "remove")
            {
                continue;
            }
            
            // Check for xpath attribute even on other elements
            var xpath = element.Attribute("xpath")?.Value;
            if (!string.IsNullOrEmpty(xpath))
            {
                // This is a modification using xpath
                _db.InsertXmlChange(
                    modId: modId,
                    xmlFile: DetermineTargetFromXpath(xpath, targetFile),
                    xpath: xpath,
                    operation: elementName,
                    propertyName: null,
                    oldValue: null,
                    newValue: element.ToString()
                );
                _changeCount++;
            }
            else if (IsGameElement(elementName))
            {
                // This is likely a direct addition (item, block, recipe, etc.)
                var itemName = element.Attribute("name")?.Value ?? element.Attribute("id")?.Value;
                var inferredXpath = $"/{InferParentElement(elementName)}";
                
                _db.InsertXmlChange(
                    modId: modId,
                    xmlFile: InferTargetFile(elementName),
                    xpath: inferredXpath,
                    operation: "append",
                    propertyName: null,
                    oldValue: null,
                    newValue: itemName != null ? $"<{elementName} name=\"{itemName}\">...</{elementName}>" : element.ToString()
                );
                _changeCount++;
            }
        }
    }
    
    /// <summary>
    /// Extract property name from xpath like /items/item[@name='x']/property[@name='Tags']/@value
    /// </summary>
    private string? ExtractPropertyNameFromXpath(string xpath)
    {
        // Match property[@name='PropertyName'] or @name='PropertyName'
        var match = System.Text.RegularExpressions.Regex.Match(
            xpath, 
            @"property\[@name=['""]([^'""]+)['""]\]", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (match.Success)
            return match.Groups[1].Value;
        
        // Match attribute access like @PropertyName
        match = System.Text.RegularExpressions.Regex.Match(xpath, @"/@([a-zA-Z_][a-zA-Z0-9_]*)$");
        if (match.Success && match.Groups[1].Value != "value")
            return match.Groups[1].Value;
        
        return null;
    }
    
    /// <summary>
    /// Determine the target file from an xpath.
    /// </summary>
    private string DetermineTargetFromXpath(string xpath, string fallback)
    {
        var lowerXpath = xpath.ToLower();
        
        if (lowerXpath.Contains("/items") || lowerXpath.Contains("/item["))
            return "items.xml";
        if (lowerXpath.Contains("/blocks") || lowerXpath.Contains("/block["))
            return "blocks.xml";
        if (lowerXpath.Contains("/recipes") || lowerXpath.Contains("/recipe["))
            return "recipes.xml";
        if (lowerXpath.Contains("/buffs") || lowerXpath.Contains("/buff["))
            return "buffs.xml";
        if (lowerXpath.Contains("/entityclasses") || lowerXpath.Contains("/entity_class["))
            return "entityclasses.xml";
        if (lowerXpath.Contains("/progression"))
            return "progression.xml";
        if (lowerXpath.Contains("/loot"))
            return "loot.xml";
        if (lowerXpath.Contains("/quests"))
            return "quests.xml";
        if (lowerXpath.Contains("/traders"))
            return "traders.xml";
        if (lowerXpath.Contains("/vehicles"))
            return "vehicles.xml";
        
        return fallback;
    }
    
    /// <summary>
    /// Determine target file from the mod xml filename.
    /// </summary>
    private string DetermineTargetFile(string fileName, XElement root)
    {
        var lowerName = fileName.ToLower();
        
        // Direct matches
        if (lowerName.Contains("items")) return "items.xml";
        if (lowerName.Contains("blocks")) return "blocks.xml";
        if (lowerName.Contains("recipes")) return "recipes.xml";
        if (lowerName.Contains("buffs")) return "buffs.xml";
        if (lowerName.Contains("entityclasses")) return "entityclasses.xml";
        if (lowerName.Contains("progression")) return "progression.xml";
        if (lowerName.Contains("loot")) return "loot.xml";
        if (lowerName.Contains("quests")) return "quests.xml";
        if (lowerName.Contains("traders")) return "traders.xml";
        
        // Infer from root element or children
        var rootName = root.Name.LocalName.ToLower();
        if (rootName == "items" || root.Elements("item").Any()) return "items.xml";
        if (rootName == "blocks" || root.Elements("block").Any()) return "blocks.xml";
        if (rootName == "recipes" || root.Elements("recipe").Any()) return "recipes.xml";
        
        return fileName;
    }
    
    /// <summary>
    /// Check if element name represents a game element type.
    /// </summary>
    private bool IsGameElement(string elementName)
    {
        var lowerName = elementName.ToLower();
        return lowerName is "item" or "block" or "recipe" or "buff" or "entity_class" 
            or "quest" or "trader" or "vehicle" or "lootcontainer" or "lootgroup"
            or "progression" or "skill" or "perk" or "book";
    }
    
    /// <summary>
    /// Infer parent element for xpath construction.
    /// </summary>
    private string InferParentElement(string elementName)
    {
        return elementName.ToLower() switch
        {
            "item" => "items",
            "block" => "blocks",
            "recipe" => "recipes",
            "buff" => "buffs",
            "entity_class" => "entityclasses",
            "quest" => "quests",
            "trader" => "traders",
            "vehicle" => "vehicles",
            _ => elementName + "s"
        };
    }
    
    /// <summary>
    /// Infer target file from element type.
    /// </summary>
    private string InferTargetFile(string elementName)
    {
        return InferParentElement(elementName) + ".xml";
    }
}
