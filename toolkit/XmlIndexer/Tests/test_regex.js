// Test regex escaping for XPath operators
const testOps = ['starts-with(', 'contains(', 'not(', '//', '..', '@'];

for (const op of testOps) {
    console.log('\nTesting:', JSON.stringify(op));
    
    // The correct regex to escape special chars in JavaScript
    // We need to escape: . * + ? ^ $ { } ( ) | [ ] \
    const escaped = op.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    console.log('Escaped:', JSON.stringify(escaped));
    
    try {
        const pattern = new RegExp('(' + escaped + ')', 'g');
        console.log('Pattern:', pattern.toString());
        console.log('Works: YES');
    } catch (e) {
        console.log('ERROR:', e.message);
    }
}
