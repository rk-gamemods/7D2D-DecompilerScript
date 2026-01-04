-- XmlIndexer Test Validation SQL Script
-- Run: sqlite3 ecosystem.db < test_validation.sql
-- This script validates that the database contains expected data after a full report

-- ========================================
-- BASELINE METRICS VALIDATION
-- ========================================

.mode column
.headers on
.width 35 15 15

-- Check xml_definitions count
SELECT 
    'xml_definitions' as metric,
    COUNT(*) as actual,
    CASE WHEN COUNT(*) = 15534 THEN 'PASS' ELSE 'FAIL' END as status
FROM xml_definitions;

-- Check mods count  
SELECT 
    'mods' as metric,
    COUNT(*) as actual,
    CASE WHEN COUNT(*) = 38 THEN 'PASS' ELSE 'FAIL' END as status
FROM mods;

-- Check harmony_patches count
SELECT 
    'harmony_patches' as metric,
    COUNT(*) as actual,
    CASE WHEN COUNT(*) = 13 THEN 'PASS' ELSE 'FAIL' END as status
FROM harmony_patches;

-- Check mod_xml_operations count
SELECT 
    'mod_xml_operations' as metric,
    COUNT(*) as actual,
    CASE WHEN COUNT(*) = 82 THEN 'PASS' ELSE 'FAIL' END as status
FROM mod_xml_operations;

-- Check mod_csharp_dependencies count
SELECT 
    'mod_csharp_dependencies' as metric,
    COUNT(*) as actual,
    CASE WHEN COUNT(*) = 21 THEN 'PASS' ELSE 'FAIL' END as status
FROM mod_csharp_dependencies;

-- ========================================
-- DEFINITION TYPE BREAKDOWN
-- ========================================

.print ""
.print "=== Definition Types ==="

SELECT 
    definition_type,
    COUNT(*) as count
FROM xml_definitions
GROUP BY definition_type
ORDER BY count DESC;

-- ========================================
-- MOD TYPE BREAKDOWN
-- ========================================

.print ""
.print "=== Mod Types ==="

SELECT 
    mod_type,
    COUNT(*) as count
FROM mods
GROUP BY mod_type
ORDER BY count DESC;

-- ========================================
-- XML OPERATION TYPES
-- ========================================

.print ""
.print "=== XML Operations ==="

SELECT 
    operation_type,
    COUNT(*) as count
FROM mod_xml_operations
GROUP BY operation_type
ORDER BY count DESC;

-- ========================================
-- HARMONY PATCH TYPES  
-- ========================================

.print ""
.print "=== Harmony Patches ==="

SELECT 
    patch_type,
    COUNT(*) as count
FROM harmony_patches
GROUP BY patch_type
ORDER BY count DESC;

-- ========================================
-- C# DEPENDENCY TYPES
-- ========================================

.print ""
.print "=== C# Dependencies ==="

SELECT 
    dependency_type,
    COUNT(*) as count
FROM mod_csharp_dependencies
GROUP BY dependency_type
ORDER BY count DESC;

-- ========================================
-- TEST MOD VALIDATION
-- ========================================

.print ""
.print "=== Test Mods Status ==="

-- Verify test mods exist
SELECT 
    name,
    mod_type,
    CASE 
        WHEN name LIKE '_Conflict%' THEN 'conflict_test'
        WHEN name LIKE '_Harmony%' THEN 'harmony_test'
        WHEN name LIKE '_Xml%' THEN 'xml_test'
        ELSE 'production'
    END as test_category
FROM mods
WHERE name LIKE '_%Test%'
ORDER BY name;

-- ========================================
-- ECOSYSTEM VIEW VALIDATION
-- ========================================

.print ""
.print "=== Ecosystem Status ==="

SELECT 
    'active_entities' as metric,
    COUNT(*) as count
FROM ecosystem_entities
WHERE is_removed = 0;

SELECT 
    'modified_entities' as metric,
    COUNT(*) as count  
FROM ecosystem_entities
WHERE is_modified = 1;

SELECT 
    'removed_entities' as metric,
    COUNT(*) as count
FROM ecosystem_entities
WHERE is_removed = 1;

SELECT 
    'depended_entities' as metric,
    COUNT(*) as count
FROM ecosystem_entities
WHERE is_depended = 1;

-- ========================================
-- TRANSITIVE REFS (if built)
-- ========================================

.print ""
.print "=== Transitive References ==="

SELECT 
    'transitive_refs' as metric,
    COALESCE(COUNT(*), 0) as count
FROM transitive_refs;

-- ========================================
-- CONFLICT DETECTION (if built)
-- ========================================

.print ""
.print "=== Indirect Conflicts ==="

SELECT 
    severity,
    COUNT(*) as count
FROM indirect_conflicts
GROUP BY severity
ORDER BY 
    CASE severity 
        WHEN 'HIGH' THEN 1 
        WHEN 'MEDIUM' THEN 2 
        WHEN 'LOW' THEN 3 
    END;

.print ""
.print "=== Validation Complete ==="
