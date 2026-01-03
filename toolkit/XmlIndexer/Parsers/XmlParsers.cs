using System.Xml;
using System.Xml.Linq;
using XmlIndexer.Models;

namespace XmlIndexer.Parsers;

/// <summary>
/// Parses 7D2D XML configuration files into in-memory data structures.
/// </summary>
public class XmlParsers
{
    // In-memory storage collections
    private readonly List<XmlDefinition> _definitions;
    private readonly List<XmlProperty> _properties;
    private readonly List<XmlReference> _references;
    private readonly Dictionary<string, int> _stats;
    private long _nextDefId = 1;

    public XmlParsers(
        List<XmlDefinition> definitions,
        List<XmlProperty> properties,
        List<XmlReference> references,
        Dictionary<string, int> stats)
    {
        _definitions = definitions;
        _properties = properties;
        _references = references;
        _stats = stats;
    }

    /// <summary>Build cross-references for inheritance (extends) after all parsing complete.</summary>
    public void BuildExtendsReferences()
    {
        foreach (var def in _definitions.Where(d => !string.IsNullOrEmpty(d.Extends)))
        {
            _references.Add(new XmlReference("xml", def.Id, def.File, def.Line, def.Type, def.Extends!, "extends"));
        }
    }

    // ==========================================================================
    // Individual XML File Parsers
    // ==========================================================================

    public void ParseItems(XDocument doc)
    {
        Console.Write("  items.xml... ");
        int count = 0;
        foreach (var item in doc.Descendants("item"))
        {
            var name = item.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)item;
            var extends = item.Elements("property")
                .FirstOrDefault(p => p.Attribute("name")?.Value == "Extends")?.Attribute("value")?.Value
                ?? item.Attribute("parent")?.Value;

            var defId = AddDefinition("item", name, "items.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, item, "items.xml");
            count++;
        }
        UpdateStats("item", count);
        Console.WriteLine($"{count}");
    }

    public void ParseBlocks(XDocument doc)
    {
        Console.Write("  blocks.xml... ");
        int count = 0;
        foreach (var block in doc.Descendants("block"))
        {
            var name = block.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)block;
            var extends = block.Elements("property")
                .FirstOrDefault(p => p.Attribute("name")?.Value == "Extends")?.Attribute("value")?.Value;

            var defId = AddDefinition("block", name, "blocks.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, block, "blocks.xml");
            count++;
        }
        UpdateStats("block", count);
        Console.WriteLine($"{count}");
    }

    public void ParseEntityClasses(XDocument doc)
    {
        Console.Write("  entityclasses.xml... ");
        int count = 0;
        foreach (var entity in doc.Descendants("entity_class"))
        {
            var name = entity.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)entity;
            var extends = entity.Attribute("extends")?.Value;

            var defId = AddDefinition("entity_class", name, "entityclasses.xml", lineInfo.LineNumber, extends);
            ParseProperties(defId, entity, "entityclasses.xml");
            count++;
        }
        UpdateStats("entity_class", count);
        Console.WriteLine($"{count}");
    }

    public void ParseEntityGroups(XDocument doc)
    {
        Console.Write("  entitygroups.xml... ");
        int count = 0;
        foreach (var group in doc.Descendants("entitygroup"))
        {
            var name = group.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)group;
            var defId = AddDefinition("entity_group", name, "entitygroups.xml", lineInfo.LineNumber, null);

            var content = group.Value;
            if (!string.IsNullOrWhiteSpace(content))
            {
                foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var entityName = line.Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(entityName))
                        AddReference("xml", defId, "entitygroups.xml", lineInfo.LineNumber, "entity_class", entityName, "group_member");
                }
            }
            count++;
        }
        UpdateStats("entity_group", count);
        Console.WriteLine($"{count}");
    }

    public void ParseBuffs(XDocument doc)
    {
        Console.Write("  buffs.xml... ");
        int count = 0;
        foreach (var buff in doc.Descendants("buff"))
        {
            var name = buff.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)buff;
            var defId = AddDefinition("buff", name, "buffs.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, buff, "buffs.xml");
            ParseTriggeredEffects(defId, buff);
            count++;
        }
        UpdateStats("buff", count);
        Console.WriteLine($"{count}");
    }

    public void ParseRecipes(XDocument doc)
    {
        Console.Write("  recipes.xml... ");
        int count = 0;
        foreach (var recipe in doc.Descendants("recipe"))
        {
            var name = recipe.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)recipe;
            var defId = AddDefinition("recipe", name, "recipes.xml", lineInfo.LineNumber, null);

            AddReference("xml", defId, "recipes.xml", lineInfo.LineNumber, "item", name, "recipe_output");

            foreach (var ingredient in recipe.Elements("ingredient"))
            {
                var ingredientName = ingredient.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(ingredientName))
                    AddReference("xml", defId, "recipes.xml", lineInfo.LineNumber, "item", ingredientName, "recipe_ingredient");
            }
            count++;
        }
        UpdateStats("recipe", count);
        Console.WriteLine($"{count}");
    }

    public void ParseSounds(XDocument doc)
    {
        Console.Write("  sounds.xml... ");
        int count = 0;
        foreach (var sound in doc.Descendants("SoundDataNode"))
        {
            var name = sound.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)sound;
            AddDefinition("sound", name, "sounds.xml", lineInfo.LineNumber, null);
            count++;
        }
        UpdateStats("sound", count);
        Console.WriteLine($"{count}");
    }

    public void ParseVehicles(XDocument doc)
    {
        Console.Write("  vehicles.xml... ");
        int count = 0;
        foreach (var vehicle in doc.Descendants("vehicle"))
        {
            var name = vehicle.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)vehicle;
            var defId = AddDefinition("vehicle", name, "vehicles.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, vehicle, "vehicles.xml");
            count++;
        }
        UpdateStats("vehicle", count);
        Console.WriteLine($"{count}");
    }

    public void ParseQuests(XDocument doc)
    {
        Console.Write("  quests.xml... ");
        int count = 0;
        foreach (var quest in doc.Descendants("quest"))
        {
            var id = quest.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var lineInfo = (IXmlLineInfo)quest;
            var defId = AddDefinition("quest", id, "quests.xml", lineInfo.LineNumber, null);
            ParseProperties(defId, quest, "quests.xml");
            count++;
        }
        UpdateStats("quest", count);
        Console.WriteLine($"{count}");
    }

    public void ParseGameEvents(XDocument doc)
    {
        Console.Write("  gameevents.xml... ");
        int count = 0;
        foreach (var seq in doc.Descendants("action_sequence"))
        {
            var name = seq.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)seq;
            AddDefinition("game_event", name, "gameevents.xml", lineInfo.LineNumber, null);
            count++;
        }
        UpdateStats("game_event", count);
        Console.WriteLine($"{count}");
    }

    public void ParseProgression(XDocument doc)
    {
        Console.Write("  progression.xml... ");
        int skillCount = 0, perkCount = 0;

        foreach (var skill in doc.Descendants("skill"))
        {
            var name = skill.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)skill;
            AddDefinition("skill", name, "progression.xml", lineInfo.LineNumber, null);
            skillCount++;
        }

        foreach (var perk in doc.Descendants("perk"))
        {
            var name = perk.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)perk;
            AddDefinition("perk", name, "progression.xml", lineInfo.LineNumber, null);
            perkCount++;
        }

        UpdateStats("skill", skillCount);
        UpdateStats("perk", perkCount);
        Console.WriteLine($"{skillCount} skills, {perkCount} perks");
    }

    public void ParseLoot(XDocument doc)
    {
        Console.Write("  loot.xml... ");
        int containerCount = 0, groupCount = 0;

        foreach (var container in doc.Descendants("lootcontainer"))
        {
            var id = container.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var lineInfo = (IXmlLineInfo)container;
            AddDefinition("loot_container", id, "loot.xml", lineInfo.LineNumber, null);
            containerCount++;
        }

        foreach (var group in doc.Descendants("lootgroup"))
        {
            var name = group.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var lineInfo = (IXmlLineInfo)group;
            var defId = AddDefinition("loot_group", name, "loot.xml", lineInfo.LineNumber, null);

            foreach (var item in group.Descendants("item"))
            {
                var itemName = item.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(itemName))
                    AddReference("xml", defId, "loot.xml", lineInfo.LineNumber, "item", itemName, "loot_entry");
            }
            groupCount++;
        }

        UpdateStats("loot_container", containerCount);
        UpdateStats("loot_group", groupCount);
        Console.WriteLine($"{containerCount} containers, {groupCount} groups");
    }

    // ==========================================================================
    // Property & Reference Parsing Helpers
    // ==========================================================================

    private void ParseProperties(long defId, XElement element, string fileName)
    {
        foreach (var prop in element.Elements("property"))
        {
            var name = prop.Attribute("name")?.Value;
            var value = prop.Attribute("value")?.Value;
            var propClass = prop.Attribute("class")?.Value;
            var lineInfo = (IXmlLineInfo)prop;

            if (!string.IsNullOrEmpty(name))
            {
                AddProperty(defId, name, value, propClass, lineInfo.LineNumber);
                TrackPropertyReferences(defId, name, value, lineInfo.LineNumber, fileName);
            }
        }

        // Also parse nested property classes
        foreach (var propClass in element.Elements("property").Where(p => p.Attribute("class") != null))
        {
            var className = propClass.Attribute("class")?.Value;
            foreach (var nested in propClass.Elements("property"))
            {
                var name = nested.Attribute("name")?.Value;
                var value = nested.Attribute("value")?.Value;
                var lineInfo = (IXmlLineInfo)nested;

                if (!string.IsNullOrEmpty(name))
                    AddProperty(defId, name, value, className, lineInfo.LineNumber);
            }
        }
    }

    private void ParseTriggeredEffects(long defId, XElement buff)
    {
        foreach (var effect in buff.Descendants("triggered_effect"))
        {
            var action = effect.Attribute("action")?.Value;
            var lineInfo = (IXmlLineInfo)effect;

            var buffRef = effect.Attribute("buff")?.Value;
            if (!string.IsNullOrEmpty(buffRef))
            {
                foreach (var b in buffRef.Split(','))
                    AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "buff", b.Trim(), $"triggered_effect:{action}");
            }

            var sound = effect.Attribute("sound")?.Value;
            if (!string.IsNullOrEmpty(sound))
                AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "sound", sound, $"triggered_effect:{action}");

            var eventRef = effect.Attribute("event")?.Value;
            if (!string.IsNullOrEmpty(eventRef))
                AddReference("xml", defId, "buffs.xml", lineInfo.LineNumber, "game_event", eventRef, $"triggered_effect:{action}");
        }
    }

    private void TrackPropertyReferences(long defId, string propName, string? value, int line, string fileName)
    {
        if (string.IsNullOrEmpty(value)) return;

        var referenceProps = new Dictionary<string, string>
        {
            { "Extends", "item" },
            { "HandItem", "item" },
            { "BuffOnEat", "buff" },
            { "BuffOnExecute", "buff" },
            { "SpawnEntityName", "entity_class" },
            { "SoundIdle", "sound" },
            { "SoundDeath", "sound" },
            { "SoundAttack", "sound" },
            { "SoundRandom", "sound" },
            { "LootListOnDeath", "loot_group" },
        };

        if (referenceProps.TryGetValue(propName, out var targetType))
            AddReference("xml", defId, fileName, line, targetType, value, $"property:{propName}");
    }

    // ==========================================================================
    // Storage Helpers
    // ==========================================================================

    private long AddDefinition(string type, string name, string file, int line, string? extends)
    {
        var id = _nextDefId++;
        _definitions.Add(new XmlDefinition(id, type, name, file, line, extends));
        return id;
    }

    private void AddProperty(long defId, string name, string? value, string? propClass, int line)
    {
        _properties.Add(new XmlProperty(defId, name, value, propClass, line));
    }

    private void AddReference(string srcType, long? srcDefId, string srcFile, int line, string tgtType, string tgtName, string ctx)
    {
        _references.Add(new XmlReference(srcType, srcDefId, srcFile, line, tgtType, tgtName, ctx));
    }

    private void UpdateStats(string type, int count)
    {
        _stats[type] = count;
    }
}
