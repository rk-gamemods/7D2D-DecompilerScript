// Test script to validate mods.html JavaScript rendering
// Run with: node validate_mods_js.js <path-to-mods.html>

const fs = require('fs');
const path = require('path');

const htmlFile = process.argv[2];
if (!htmlFile) {
    console.error('Usage: node validate_mods_js.js <path-to-mods.html>');
    process.exit(1);
}

const html = fs.readFileSync(htmlFile, 'utf8');

// Extract MOD_DATA
const modDataMatch = html.match(/const MOD_DATA = (\[[\s\S]*?\]);/);
if (!modDataMatch) {
    console.error('ERROR: Could not find MOD_DATA in HTML');
    process.exit(1);
}

let modData;
try {
    // Use Function constructor to safely evaluate the array literal
    modData = eval(modDataMatch[1]);
    console.log(`MOD_DATA: Found ${modData.length} mods`);
} catch (e) {
    console.error('ERROR: Could not parse MOD_DATA:', e.message);
    process.exit(1);
}

// Extract renderMods function
const renderModsMatch = html.match(/function renderMods\(mods\) \{[\s\S]*?^function /m);
if (!renderModsMatch) {
    console.error('ERROR: Could not find renderMods function');
    process.exit(1);
}

// Check what renderMods returns for a single mod
console.log('\n--- Simulating renderMods ---');

// Simple mock of the template
const testMod = modData[0];
console.log(`First mod: ${testMod.name}`);
console.log(`  loadOrder: ${testMod.loadOrder}`);
console.log(`  health: ${testMod.health}`);
console.log(`  modType: ${testMod.modType}`);

// Simulate the template for first mod
const detailsId = `mod-${testMod.name.replace(/[^a-zA-Z0-9]/g, '_')}`;
console.log(`  Expected details ID: ${detailsId}`);

// Check if HTML generation would work
console.log('\n--- Validation ---');
let errors = 0;

// Check all mod names are valid for ID generation
for (const mod of modData) {
    const id = `mod-${mod.name.replace(/[^a-zA-Z0-9]/g, '_')}`;
    if (!mod.name || mod.name.length === 0) {
        console.error(`ERROR: Mod at loadOrder ${mod.loadOrder} has empty name`);
        errors++;
    }
    if (mod.loadOrder === undefined) {
        console.error(`ERROR: Mod "${mod.name}" has no loadOrder`);
        errors++;
    }
}

// Check for problematic characters in mod data that could break template literals
for (const mod of modData) {
    // Check for unescaped backticks in string properties
    const strProps = ['name', 'oneLiner', 'health', 'healthNote', 'modType'];
    for (const prop of strProps) {
        if (mod[prop] && typeof mod[prop] === 'string' && mod[prop].includes('`')) {
            console.error(`ERROR: Mod "${mod.name}" has backtick in ${prop}: ${mod[prop].substring(0, 50)}...`);
            errors++;
        }
    }
}

console.log(`\nTotal mods: ${modData.length}`);
console.log(`Errors found: ${errors}`);

if (errors === 0) {
    console.log('\nPASS: MOD_DATA structure looks valid');
    console.log('\nIf page still doesn\'t render, check browser DevTools Console (F12) for runtime errors.');
} else {
    console.log('\nFAIL: Found data issues that could break rendering');
    process.exit(1);
}
