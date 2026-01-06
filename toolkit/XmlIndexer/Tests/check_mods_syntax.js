// check_mods_syntax.js - Validate JavaScript syntax in mods.html
// Usage: node check_mods_syntax.js <path-to-mods.html>

const fs = require('fs');
const vm = require('vm');

const htmlFile = process.argv[2];
if (!htmlFile) {
    console.error('Usage: node check_mods_syntax.js <path-to-mods.html>');
    process.exit(1);
}
const html = fs.readFileSync(htmlFile, 'utf8');

// Extract all script content
const scriptMatch = html.match(/<script>([\s\S]*?)<\/script>/);
if (!scriptMatch) {
    console.log('ERROR: Could not extract script');
    process.exit(1);
}

const script = scriptMatch[1];
console.log('Script length:', script.length);

// Try to syntax check
try {
    new vm.Script(script, { filename: 'mods.html' });
    console.log('SYNTAX CHECK: PASSED');
} catch(e) {
    console.log('SYNTAX ERROR:', e.message);
    console.log(e.stack);
}
