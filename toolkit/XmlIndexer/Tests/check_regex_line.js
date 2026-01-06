// check_regex_line.js - Debug regex escaping in mods.html
// Usage: node check_regex_line.js <path-to-mods.html>

const fs = require('fs');

const htmlFile = process.argv[2];
if (!htmlFile) {
    console.error('Usage: node check_regex_line.js <path-to-mods.html>');
    process.exit(1);
}
const html = fs.readFileSync(htmlFile, 'utf8');

// Find the escaped line
const idx = html.indexOf('const escaped = op.replace');
if (idx > 0) {
    const line = html.substring(idx, idx + 100);
    console.log('Found line:');
    console.log(line);
    
    // Show each character
    console.log('\nCharacter by character (first 60):');
    for (let i = 0; i < 60; i++) {
        const c = line[i];
        console.log(i, JSON.stringify(c), c.charCodeAt(0));
    }
}
