# Process batches 111-120 with descriptions for 7 Days to Die entities
# This script reads sample_trace.jsonl and creates 10 batch files with layman/technical descriptions and player impact

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Configuration
$sourceFile = "sample_trace.jsonl"
$outputDir = "."
$batchMappings = @{
    111 = 11001
    112 = 11101
    113 = 11201
    114 = 11301
    115 = 11401
    116 = 11501
    117 = 11601
    118 = 11701
    119 = 11801
    120 = 11901
}

# Helper function to generate descriptions based on entity data
function Get-EntityDescriptions {
    param($entity)
    
    $entityName = $entity.entity_name
    $entityType = $entity.entity_type
    $gameContext = $entity.game_context
    $codeTrace = $entity.code_trace
    
    # Initialize descriptions
    $layman = ""
    $technical = ""
    $playerImpact = ""
    
    # Pattern matching for entity types
    switch -Regex ($entityName) {
        # Zombie melee attacks
        '^meleeHandZombie' {
            $zombieType = $entityName -replace 'meleeHandZombie', ''
            $layman = "This defines how zombies attack players in close combat."
            $technical = "Zombie melee attack configuration with damage values, range, and animation settings."
            $playerImpact = "Determines how much damage you take when a zombie hits you in melee combat."
        }
        # Animal attacks
        '^meleeHand(Bear|Coyote|Wolf|Snake|Boar)' {
            $animal = if ($entityName -match 'Bear') { "bears" } 
                     elseif ($entityName -match 'Coyote') { "coyotes" }
                     elseif ($entityName -match 'Wolf') { "wolves" }
                     elseif ($entityName -match 'Snake') { "snakes" }
                     else { "boars" }
            $layman = "This defines how $animal attack players."
            $technical = "Animal melee attack definition with damage properties, range, and hit detection."
            $playerImpact = "Controls how much damage $animal deal when they attack you."
        }
        # Player tools - Axes
        '^meleeToolAxe' {
            $tier = if ($entityName -match 'T0|Stone') { "basic" }
                   elseif ($entityName -match 'T1|Iron') { "intermediate" }
                   elseif ($entityName -match 'T2|Steel') { "advanced" }
                   elseif ($entityName -match 'T3|Chainsaw') { "motorized" }
                   else { "standard" }
            $layman = "A $tier axe used for chopping wood and trees."
            $technical = "Harvesting tool with wood gathering bonus, damage stats, and durability properties."
            $playerImpact = "Your primary tool for gathering wood efficiently from trees and wooden objects."
        }
        # Player tools - Pickaxes
        '^meleeToolPick' {
            $tier = if ($entityName -match 'T0|Stone') { "basic" }
                   elseif ($entityName -match 'T1|Iron') { "intermediate" }
                   elseif ($entityName -match 'T2|Steel') { "advanced" }
                   elseif ($entityName -match 'T3|Auger') { "motorized" }
                   else { "standard" }
            $layman = "A $tier pickaxe for mining stone and ore."
            $technical = "Mining tool with stone/ore harvesting bonus, damage values, and degradation rates."
            $playerImpact = "Essential tool for gathering stone, iron, and other minerals from rocks and terrain."
        }
        # Player tools - Shovels
        '^meleeToolShovel' {
            $tier = if ($entityName -match 'T0|Stone') { "basic" }
                   elseif ($entityName -match 'T1|Iron') { "intermediate" }
                   elseif ($entityName -match 'T2|Steel') { "advanced" }
                   else { "standard" }
            $layman = "A $tier shovel for digging dirt and sand."
            $technical = "Terrain modification tool with earth block harvesting bonus and durability."
            $playerImpact = "Used to dig up dirt, sand, and clay quickly for building and terrain shaping."
        }
        # Repair tools
        '^meleeToolRepair' {
            $layman = "A hammer used to repair and upgrade blocks in your buildings."
            $technical = "Block repair/upgrade tool with quality level and durability properties."
            $playerImpact = "Essential for maintaining your base defenses and upgrading building blocks."
        }
        # Salvage tools
        '^meleeToolSalvage' {
            $tier = if ($entityName -match 'T1|Wrench') { "basic" }
                   elseif ($entityName -match 'T2|Ratchet') { "improved" }
                   elseif ($entityName -match 'T3|Impact') { "powered" }
                   else { "standard" }
            $layman = "A $tier wrench for salvaging mechanical parts from vehicles and appliances."
            $technical = "Salvaging tool with mechanical parts harvesting bonus and item disassembly speed."
            $playerImpact = "Used to scrap cars, machines, and appliances for valuable mechanical parts and resources."
        }
        # Flashlight
        '^meleeToolFlashlight' {
            $layman = "A handheld flashlight that illuminates dark areas."
            $technical = "Light source item with battery consumption, brightness level, and melee damage."
            $playerImpact = "Provides portable lighting for exploring caves and buildings at night without occupying weapon slot."
        }
        # Torch
        '^meleeToolTorch' {
            $layman = "A burning torch that provides light and can set enemies on fire."
            $technical = "Temporary light source with fire damage, fuel duration, and illumination radius."
            $playerImpact = "Early-game lighting option that doubles as a weapon with fire damage."
        }
        # Wire tool
        '^meleeToolWire' {
            $layman = "A wire tool used to connect electrical devices and traps."
            $technical = "Electrical wiring tool for connecting power sources to devices and triggers."
            $playerImpact = "Required to wire up traps, lights, and automated defenses in your base."
        }
        # Paint tool
        '^meleeToolPaint' {
            $layman = "A paintbrush used to color and texture building blocks."
            $technical = "Block painting tool allowing texture and color customization of placed blocks."
            $playerImpact = "Customize the appearance of your buildings with different colors and textures."
        }
        # Melee weapons - Clubs
        '^meleeWpnClub' {
            $tier = if ($entityName -match 'T0|Wooden') { "basic wooden" }
                   elseif ($entityName -match 'T1|Baseball|Candy') { "reinforced" }
                   elseif ($entityName -match 'T3|Steel') { "advanced steel" }
                   else { "standard" }
            $layman = "A $tier club for bashing enemies."
            $technical = "Blunt melee weapon with stamina cost, attack speed, block damage, and stun chance."
            $playerImpact = "Heavy-hitting melee weapon effective against armored targets with high knockdown potential."
        }
        # Melee weapons - Knives/Blades
        '^meleeWpnBlade' {
            $tier = if ($entityName -match 'T0|Bone') { "primitive" }
                   elseif ($entityName -match 'T1|Hunting|Candy') { "combat" }
                   elseif ($entityName -match 'T3|Machete') { "military-grade" }
                   else { "standard" }
            $layman = "A $tier knife for quick slashing attacks."
            $technical = "Blade weapon with fast attack speed, bleeding damage, and entity harvesting bonus."
            $playerImpact = "Fast-attacking melee weapon that causes bleeding and improves meat/hide harvesting from animals."
        }
        # Melee weapons - Spears
        '^meleeWpnSpear' {
            $tier = if ($entityName -match 'T0|Stone') { "primitive" }
                   elseif ($entityName -match 'T1|Iron') { "reinforced" }
                   elseif ($entityName -match 'T3|Steel') { "combat" }
                   else { "standard" }
            $layman = "A $tier spear with long reach for keeping enemies at distance."
            $technical = "Ranged melee weapon with extended range, piercing damage, and throwable capability."
            $playerImpact = "Long-range melee option that lets you attack enemies from safety while keeping distance."
        }
        # Melee weapons - Sledgehammers
        '^meleeWpnSledge' {
            $tier = if ($entityName -match 'T0|Stone') { "crude" }
                   elseif ($entityName -match 'T1|Iron') { "reinforced" }
                   elseif ($entityName -match 'T3|Steel') { "heavy-duty" }
                   else { "standard" }
            $layman = "A $tier sledgehammer for devastating area attacks."
            $technical = "Heavy melee weapon with splash damage, high block destruction, stamina cost, and knockdown."
            $playerImpact = "Powerful weapon that damages multiple enemies and destroys blocks quickly but drains stamina."
        }
        # Melee weapons - Knuckles
        '^meleeWpnKnuckles' {
            $tier = if ($entityName -match 'T0|Leather') { "basic" }
                   elseif ($entityName -match 'T1|Iron') { "reinforced" }
                   elseif ($entityName -match 'T3|Steel') { "combat" }
                   else { "standard" }
            $layman = "A pair of $tier knuckles for rapid punching attacks."
            $technical = "Fast melee weapon with combo attacks, low stamina cost, and critical hit bonuses."
            $playerImpact = "Extremely fast-attacking weapon ideal for hit-and-run tactics with low stamina drain."
        }
        # Melee weapons - Batons
        '^meleeWpnBaton' {
            $tier = if ($entityName -match 'T0|Pipe') { "makeshift pipe" }
                   elseif ($entityName -match 'T2|Stun') { "electric stun" }
                   else { "standard" }
            $layman = "A $tier baton for crowd control."
            $technical = "Non-lethal melee weapon with stun effect, shock damage, and mobility bonuses."
            $playerImpact = "Stuns enemies temporarily, allowing you to escape or reposition during combat."
        }
        # Ranged weapons - Bows
        '^gunBow' {
            $tier = if ($entityName -match 'T0|Primitive') { "primitive" }
                   elseif ($entityName -match 'T1|Wooden|Iron') { "recurve" }
                   elseif ($entityName -match 'T3|Compound') { "compound" }
                   else { "standard" }
            $layman = "A $tier bow for silent ranged attacks."
            $technical = "Ranged weapon with arrow projectiles, draw time, accuracy, and stealth properties."
            $playerImpact = "Silent ranged weapon that doesn't attract zombies, ideal for stealth gameplay."
        }
        # Ranged weapons - Handguns
        '^gunHandgun' {
            $tier = if ($entityName -match 'T0|Pipe') { "makeshift pipe" }
                   elseif ($entityName -match 'T1|Pistol') { "9mm" }
                   elseif ($entityName -match 'T2|Magnum') { ".44 magnum" }
                   elseif ($entityName -match 'T3|Desert|SMG') { "tactical" }
                   else { "standard" }
            $layman = "A $tier pistol for close-range combat."
            $technical = "Semi-automatic firearm with magazine capacity, recoil, accuracy, and mod slots."
            $playerImpact = "Reliable sidearm with moderate damage and good accuracy for general combat."
        }
        # Ranged weapons - Rifles
        '^gunRifle' {
            $tier = if ($entityName -match 'T0|Pipe') { "crude pipe" }
                   elseif ($entityName -match 'T1|Hunting') { "bolt-action hunting" }
                   elseif ($entityName -match 'T2|Lever') { "lever-action" }
                   elseif ($entityName -match 'T3|Sniper') { "tactical sniper" }
                   else { "standard" }
            $layman = "A $tier rifle for long-range precision shots."
            $technical = "Long-range firearm with scope compatibility, high damage, accuracy, and penetration."
            $playerImpact = "Powerful long-range weapon capable of headshots and taking down tough enemies from distance."
        }
        # Ranged weapons - Shotguns
        '^gunShotgun' {
            $tier = if ($entityName -match 'T0|Pipe') { "improvised pipe" }
                   elseif ($entityName -match 'T1|Double') { "double-barrel" }
                   elseif ($entityName -match 'T2|Pump') { "pump-action" }
                   elseif ($entityName -match 'T3|Auto') { "automatic" }
                   else { "standard" }
            $layman = "A $tier shotgun for devastating close-range damage."
            $technical = "Spread-pattern firearm with pellet count, close-range damage multiplier, and knockback."
            $playerImpact = "Extremely powerful at close range but ineffective at distance, ideal for defending doorways."
        }
        # Ranged weapons - Machine Guns
        '^gunMG' {
            $tier = if ($entityName -match 'T0|Pipe') { "makeshift pipe" }
                   elseif ($entityName -match 'T1|AK47') { "assault rifle" }
                   elseif ($entityName -match 'T2|Tactical') { "tactical rifle" }
                   elseif ($entityName -match 'T3|M60') { "heavy machine gun" }
                   else { "automatic" }
            $layman = "A $tier automatic weapon for sustained fire."
            $technical = "Fully automatic firearm with high rate of fire, magazine size, recoil pattern, and suppression."
            $playerImpact = "Spray multiple enemies with continuous fire but burns through ammunition quickly."
        }
        # Ranged weapons - Rocket Launcher
        '^gunExplosives.*Rocket' {
            $layman = "A rocket launcher that fires explosive projectiles."
            $technical = "Heavy weapon with explosive splash damage, reload time, and block destruction capability."
            $playerImpact = "Devastating area-of-effect weapon for groups of enemies and demolishing structures."
        }
        # Weapon/Tool Parts
        'Parts$' {
            $itemType = if ($entityName -match 'gun') { "weapon" } else { "tool" }
            $layman = "Spare parts used to craft or repair this $itemType."
            $technical = "Crafting component required for assembling or repairing the corresponding item."
            $playerImpact = "Collect these to craft higher-quality versions of this $itemType at workbenches."
        }
        # Armor mods - Schematics
        '^modArmor.*Schematic' {
            $modType = $entityName -replace 'modArmor', '' -replace 'Schematic', ''
            $layman = "A schematic that teaches you how to craft an armor modification."
            $technical = "Recipe unlock item that adds armor mod crafting recipe to your menu permanently."
            $playerImpact = "Learn to craft armor mods that enhance your protective gear with special bonuses."
        }
        # Gun mods - Schematics
        '^modGun.*Schematic' {
            $modType = $entityName -replace 'modGun', '' -replace 'Schematic', ''
            $layman = "A schematic that teaches you how to craft a weapon modification."
            $technical = "Recipe unlock item that adds gun mod crafting recipe to your menu permanently."
            $playerImpact = "Learn to craft weapon mods that improve accuracy, damage, or add special effects to guns."
        }
        # Melee mods - Schematics
        '^modMelee.*Schematic' {
            $modType = $entityName -replace 'modMelee', '' -replace 'Schematic', ''
            $layman = "A schematic that teaches you how to craft a melee weapon modification."
            $technical = "Recipe unlock item that adds melee mod crafting recipe to your menu permanently."
            $playerImpact = "Learn to craft mods that enhance your melee weapons with elemental damage or special effects."
        }
        # Vehicle mods - Schematics
        '^modVehicle.*Schematic' {
            $modType = $entityName -replace 'modVehicle', '' -replace 'Schematic', ''
            $layman = "A schematic that teaches you how to craft a vehicle modification."
            $technical = "Recipe unlock item that adds vehicle mod crafting recipe to your menu permanently."
            $playerImpact = "Learn to craft vehicle upgrades that improve speed, storage, or add special features."
        }
        # Fuel tank mods - Schematics
        '^modFuelTank.*Schematic' {
            $size = if ($entityName -match 'Small') { "small" } else { "large" }
            $layman = "A schematic for crafting a $size fuel tank for tools and vehicles."
            $technical = "Recipe unlock for fuel tank modification that increases gas capacity."
            $playerImpact = "Increases how long your motorized tools and vehicles can run before refueling."
        }
        # Robotic drone mods - Schematics
        '^modRoboticDrone.*Schematic' {
            $modType = $entityName -replace 'modRoboticDrone', '' -replace 'ModSchematic', ''
            $layman = "A schematic for crafting a robotic drone modification."
            $technical = "Recipe unlock for drone upgrade that adds functionality or improvements."
            $playerImpact = "Customize your drone with special abilities like healing, cargo carrying, or armor."
        }
        # Quest items - Treasure maps
        '^qt_' {
            $layman = "A treasure map that starts a quest to find buried loot."
            $technical = "Quest item that initiates treasure hunt quest with location marker and rewards."
            $playerImpact = "Follow the map to discover hidden treasure chests with valuable loot."
        }
        # Quest reward bundles
        '^questReward.*Bundle' {
            $bundleType = $entityName -replace 'questReward', '' -replace 'Bundle', '' -replace 'Legendary', '' -replace 'Crafting', ''
            $layman = "A reward bundle containing supplies and equipment."
            $technical = "Quest completion reward container that generates multiple items when used."
            $playerImpact = "Open this to receive useful items and resources as quest rewards."
        }
        # Notes/Admin items
        '^note.*Admin' {
            $layman = "An administrator item for testing and debugging."
            $technical = "Developer tool for spawning items, granting achievements, or testing game systems."
            $playerImpact = "Only available in creative/developer mode for testing purposes."
        }
        # Old cash/currency
        '^oldCash' {
            $layman = "Pre-apocalypse paper money that can be sold to traders."
            $technical = "Currency item with economic value and fuel properties for burning."
            $playerImpact = "Sell to traders for Duke's Casino Tokens or burn as emergency fuel source."
        }
        # Missing item placeholder
        '^missingItem' {
            $layman = "A placeholder item representing missing or undefined content."
            $technical = "Error fallback item displayed when game cannot load proper item data."
            $playerImpact = "This appears when there's an error loading item definitions."
        }
        # Generic fallback for undefined patterns
        default {
            # Try to extract meaningful info from game context
            switch ($gameContext) {
                'Armor' {
                    $layman = "A piece of protective gear or armor modification."
                    $technical = "Armor item with protection values, durability, and modification slots."
                    $playerImpact = "Equip this to reduce damage from enemy attacks."
                }
                'Ammunition' {
                    $layman = "Ammunition used for ranged weapons."
                    $technical = "Projectile item with damage type, penetration, and stack size properties."
                    $playerImpact = "Required to fire guns and other ranged weapons."
                }
                'Melee Weapons' {
                    $layman = "A melee weapon for close combat."
                    $technical = "Close-range weapon with damage values, attack speed, and durability."
                    $playerImpact = "Use this to fight enemies in close quarters."
                }
                'Ranged Weapons' {
                    $layman = "A ranged weapon for attacking from distance."
                    $technical = "Projectile weapon with accuracy, damage, and ammunition requirements."
                    $playerImpact = "Attack enemies from afar while staying safer."
                }
                { $_ -match 'Items|Equipment' } {
                    $layman = "A usable item in your inventory."
                    $technical = "Game item with properties defining behavior and interactions."
                    $playerImpact = "Can be equipped, used, or placed in the world."
                }
                'Enemies' {
                    $layman = "An enemy or creature attack definition."
                    $technical = "AI entity attack configuration with damage and behavior properties."
                    $playerImpact = "Defines how enemies damage players and blocks."
                }
                default {
                    $layman = "A game entity with specific properties."
                    $technical = "Item or entity definition with configurable game mechanics."
                    $playerImpact = "Part of the game's item and entity system."
                }
            }
        }
    }
    
    return @{
        layman_description = $layman
        technical_description = $technical
        player_impact = $playerImpact
    }
}

Write-Host "Processing batches 111-120..." -ForegroundColor Cyan
Write-Host ""

foreach ($batchNum in 111..120) {
    $offset = $batchMappings[$batchNum]
    $outputFile = "batch_$batchNum.jsonl"
    
    Write-Host "Processing Batch $batchNum (lines $offset-$($offset+99))..." -ForegroundColor Yellow
    
    # Read source file lines
    $allLines = Get-Content $sourceFile
    $batchLines = $allLines[($offset-1)..($offset+98)]
    
    # Process each line
    $processedLines = @()
    $count = 0
    
    foreach ($line in $batchLines) {
        $count++
        if ($count % 10 -eq 0) {
            Write-Host "  Processed $count/100 entities..." -ForegroundColor Gray
        }
        
        $entity = $line | ConvertFrom-Json
        $descriptions = Get-EntityDescriptions -entity $entity
        
        # Create output object with only required fields
        $output = [PSCustomObject]@{
            entity_type = $entity.entity_type
            entity_name = $entity.entity_name
            layman_description = $descriptions.layman_description
            technical_description = $descriptions.technical_description
            player_impact = $descriptions.player_impact
        }
        
        $processedLines += ($output | ConvertTo-Json -Compress -Depth 10)
    }
    
    # Write output file
    $processedLines | Out-File -FilePath $outputFile -Encoding UTF8
    Write-Host "  ✓ Created $outputFile with 100 entities" -ForegroundColor Green
    Write-Host ""
}

Write-Host "All batches processed successfully!" -ForegroundColor Green
Write-Host "Created 10 files: batch_111.jsonl through batch_120.jsonl" -ForegroundColor Cyan
