import json

# Read all traces
with open("full_traces.jsonl", "r", encoding="utf-8") as f:
    all_traces = [json.loads(line) for line in f]

print(f"Total traces available: {len(all_traces)}")

# Process batches 090-100  
for batch_num in range(90, 101):
    start_idx = (batch_num - 1) * 100
    end_idx = start_idx + 100
    
    batch_traces = all_traces[start_idx:end_idx]
    
    # Add descriptions
    for trace in batch_traces:
        etype = trace.get("entity_type", "")
        ename = trace.get("entity_name", "")
        ctx = trace.get("parent_context", "")
        
        # Layman
        if etype == "definition":
            if ctx == "entity_group":
                trace["layman_description"] = "Enemy spawn group that controls which zombie types appear together during gameplay"
            elif ctx == "item":
                trace["layman_description"] = "Item that players can find, craft, or use from their inventory menu"
            elif ctx == "block":
                trace["layman_description"] = "Building block or placeable object that players can use in construction"
            elif ctx == "entity_class":
                trace["layman_description"] = "Living creature, zombie, or NPC that moves and interacts in the world"
            elif ctx == "recipe":
                trace["layman_description"] = "Crafting recipe showing what materials combine to create new items"
            elif ctx == "quest":
                trace["layman_description"] = "Mission or objective that rewards players for completing specific tasks"
            else:
                trace["layman_description"] = f"Game configuration that defines how this {ctx} functions in-game"
        elif etype == "property_name":
            if "icon" in ename.lower(): trace["layman_description"] = "Controls what icon image appears in inventory menus"
            elif "model" in ename.lower() or "mesh" in ename.lower(): trace["layman_description"] = "Points to the 3D model file displayed in the game world"
            elif "color" in ename.lower() or "tint" in ename.lower(): trace["layman_description"] = "Changes the visual color tint of this item or block"
            elif "sort" in ename.lower(): trace["layman_description"] = "Determines where this appears when sorting inventory menus"
            elif "damage" in ename.lower(): trace["layman_description"] = "Controls how much damage this deals to enemies or blocks"
            else: trace["layman_description"] = f"Property that configures how this {ctx} behaves in the game"
        else:
            trace["layman_description"] = "Configuration data that affects gameplay systems"
        
        # Technical
        if etype == "definition":
            if ctx == "entity_group":
                trace["technical_description"] = "Spawn group definition with entity composition and probability weights"
            elif ctx == "item":
                trace["technical_description"] = "Item class definition containing stats, properties, and behavior handlers"
            elif ctx == "block":
                trace["technical_description"] = "Block definition with material properties, collision data, and placement rules"
            elif ctx == "entity_class":
                trace["technical_description"] = "Entity class with AI behavior tree, animation controller, and stat modifiers"
            elif ctx == "recipe":
                trace["technical_description"] = "Recipe schema defining input requirements, crafting time, and output results"
            elif ctx == "quest":
                trace["technical_description"] = "Quest definition with objective tracking, reward data, and completion triggers"
            else:
                trace["technical_description"] = f"XML definition node containing {ctx} configuration parameters"
        elif etype == "property_name":
            if "icon" in ename.lower(): trace["technical_description"] = "Asset path reference to sprite texture resource for UI display"
            elif "model" in ename.lower() or "mesh" in ename.lower(): trace["technical_description"] = "Prefab asset path linking to Unity GameObject mesh instance"
            elif "color" in ename.lower() or "tint" in ename.lower(): trace["technical_description"] = "RGB color value applied to material shader tinting"
            elif "sort" in ename.lower(): trace["technical_description"] = "Integer sort order value for UI list positioning algorithm"
            elif "damage" in ename.lower(): trace["technical_description"] = "Numeric damage modifier applied in combat calculations"
            else: trace["technical_description"] = f"XML attribute controlling {ctx} behavior parameters"
        else:
            trace["technical_description"] = "Configuration data structure in XML game format"
        
        # Player Impact
        if etype == "definition":
            if ctx == "entity_group":
                trace["player_impact"] = "Affects enemy difficulty and variety during horde nights and random spawns"
            elif ctx == "item":
                trace["player_impact"] = "Provides tools, weapons, or resources needed for survival and progression"
            elif ctx == "block":
                trace["player_impact"] = "Supplies building materials or functional blocks for base construction"
            elif ctx == "entity_class":
                trace["player_impact"] = "Determines enemy threats to fight or friendly NPCs to interact with"
            elif ctx == "recipe":
                trace["player_impact"] = "Enables crafting of new items from collected resources"
            elif ctx == "quest":
                trace["player_impact"] = "Offers rewards and experience for completing missions"
            else:
                trace["player_impact"] = "Influences gameplay mechanics and player experience"
        elif etype == "property_name":
            if "icon" in ename.lower(): trace["player_impact"] = "Changes what picture you see in inventory slots"
            elif "model" in ename.lower() or "mesh" in ename.lower(): trace["player_impact"] = "Affects visual appearance when placed in the world"
            elif "color" in ename.lower() or "tint" in ename.lower(): trace["player_impact"] = "Alters the color you see on icons or placed blocks"
            elif "sort" in ename.lower(): trace["player_impact"] = "Changes where items appear when scrolling menus"
            elif "damage" in ename.lower(): trace["player_impact"] = "Increases or decreases effectiveness in combat"
            else: trace["player_impact"] = f"Modifies how this {ctx} functions during gameplay"
        else:
            trace["player_impact"] = "Affects core gameplay systems and balance"
    
    # Write batch file
    out_file = f"batch_{batch_num:03d}.jsonl"
    with open(out_file, "w", encoding="utf-8") as f:
        for trace in batch_traces:
            f.write(json.dumps(trace, ensure_ascii=False) + "\n")
    
    print(f"  ✓ {out_file} - {len(batch_traces)} records")

print("\n====== BATCHES 090-100 REWRITE COMPLETE ======")
print("\nSample from batch_100:")
with open("batch_100.jsonl", "r", encoding="utf-8") as f:
    lines = f.readlines()
    if lines:
        last = json.loads(lines[-1])
        print(f"Entity: {last['entity_name']}")
        print(f"Layman: {last.get('layman_description', 'N/A')}")
        print(f"Tech: {last.get('technical_description', 'N/A')}")
        print(f"Impact: {last.get('player_impact', 'N/A')}")
