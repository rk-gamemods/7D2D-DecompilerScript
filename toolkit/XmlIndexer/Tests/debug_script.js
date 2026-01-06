// debug_script.js - Debug JavaScript issues in mods.html
// Usage: node debug_script.js <path-to-mods.html>

const fs = require('fs');

const htmlFile = process.argv[2];
if (!htmlFile) {
    console.error('Usage: node debug_script.js <path-to-mods.html>');
    process.exit(1);
}
const html = fs.readFileSync(htmlFile, 'utf8');

const scriptStart = html.indexOf('<script>') + 8;
const scriptEnd = html.indexOf('</script>');
const script = html.substring(scriptStart, scriptEnd);

console.log('Script length:', script.length);

// Look for unmatched template literals
const backticks = script.split('`').length - 1;
console.log('Backtick count:', backticks, '(should be even):', backticks % 2 === 0 ? 'OK' : 'PROBLEM');

// Check arrow functions count
const arrows = (script.match(/=>/g) || []).length;
console.log('Arrow functions:', arrows);

// Check for smart quotes (copy-pasted from Word etc)
const smartQuotes = (script.match(/[\u201C\u201D\u2018\u2019]/g) || []).length;
console.log('Smart quotes found:', smartQuotes, smartQuotes > 0 ? '- PROBLEM!' : '- OK');

// Check for \r\n inside template literals that might be problematic
const crlfInTemplates = (script.match(/`[^`]*\r\n[^`]*`/g) || []).length;
console.log('CRLF in template literals:', crlfInTemplates);

// Try to find where MOD_DATA is defined
const modDataPos = script.indexOf('const MOD_DATA');
console.log('\nMOD_DATA position:', modDataPos);

// Check what's around that position
if (modDataPos > 0) {
    console.log('Before MOD_DATA (50 chars):');
    console.log(JSON.stringify(script.substring(modDataPos - 50, modDataPos)));
}

// Also check DOMContentLoaded
const domPos = script.indexOf('DOMContentLoaded');
console.log('\nDOMContentLoaded positions:');
let pos = 0;
while ((pos = script.indexOf('DOMContentLoaded', pos)) !== -1) {
    console.log('  at', pos);
    pos++;
}
