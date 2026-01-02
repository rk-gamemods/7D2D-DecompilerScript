# Common Modding Patterns

This file documents common patterns for modding 7 Days to Die using Harmony patches.

## Harmony Patch Types

### Prefix Patches
Run before the original method. Can skip execution by returning `false`.

```csharp
[HarmonyPatch(typeof(TargetClass), "MethodName")]
class MyPatch
{
    static bool Prefix(/* parameters */)
    {
        // Return false to skip original method
        return true;
    }
}
```

### Postfix Patches
Run after the original method. Can modify the return value.

```csharp
[HarmonyPatch(typeof(TargetClass), "MethodName")]
class MyPatch
{
    static void Postfix(ref ReturnType __result)
    {
        // Modify __result to change return value
    }
}
```

### Transpiler Patches
Modify the IL instructions of the original method.

```csharp
[HarmonyPatch(typeof(TargetClass), "MethodName")]
class MyPatch
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        // Return modified instructions
    }
}
```

## Common Patch Targets

### Inventory Operations

| Target | Method | Use Case |
|--------|--------|----------|
| `Bag` | `DecItem` | Intercept item removal |
| `Bag` | `AddItem` | Intercept item addition |
| `ItemStack` | `count` setter | Track stack changes |
| `XUiM_PlayerInventory` | various | UI inventory operations |

### Crafting

| Target | Method | Use Case |
|--------|--------|----------|
| `CraftingManager` | `Craft` | Modify crafting behavior |
| `Recipe` | `GetRecipe` | Change recipe lookup |
| `XUiC_CraftingQueue` | various | UI crafting operations |

### Player Actions

| Target | Method | Use Case |
|--------|--------|----------|
| `EntityPlayerLocal` | `OnDied` | Death handling |
| `EntityPlayerLocal` | `OnUpdateLive` | Per-frame player logic |
| `PlayerMoveController` | `Update` | Movement handling |

### Container/Loot

| Target | Method | Use Case |
|--------|--------|----------|
| `TileEntityLootContainer` | `SetUserAccessing` | Container open/close |
| `LootContainer` | `Spawn` | Loot generation |
| `TEFeatureStorage` | various | Storage features |

## Best Practices

1. **Use specific method overloads**: Many methods have multiple overloads
2. **Check for null**: Game state may not be fully initialized
3. **Handle multiplayer**: Some code only runs on server or client
4. **Test with other mods**: Use the compat command to check for conflicts
5. **Cache references**: Avoid repeated GetComponent calls in Update loops

## XML Modding Patterns

### Append new items
```xml
<append xpath="/items">
    <item name="myNewItem">
        ...
    </item>
</append>
```

### Modify existing properties
```xml
<set xpath="/items/item[@name='gunPistol']/property[@name='DamageEntity']/@value">50</set>
```

### Remove elements
```xml
<remove xpath="/items/item[@name='obsoleteItem']"/>
```

### Conditional modifications
Use `insertAfter` or `insertBefore` for ordered insertions.
