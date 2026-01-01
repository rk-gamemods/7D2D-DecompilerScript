-- 7D2D Mod Maintenance Toolkit - SQLite Schema
-- This schema stores the call graph and mod compatibility data

-- ============================================================================
-- CORE TABLES: Call Graph
-- ============================================================================

-- Types (classes, structs, interfaces, enums)
CREATE TABLE IF NOT EXISTS types (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL,                 -- Simple name: "EntityPlayer"
    namespace TEXT,                     -- "Assembly-CSharp" or null
    full_name TEXT NOT NULL UNIQUE,     -- "EntityPlayer" or "SomeNamespace.SomeClass"
    kind TEXT NOT NULL,                 -- 'class', 'struct', 'interface', 'enum'
    base_type TEXT,                     -- Parent class full name (null if none)
    assembly TEXT,                      -- Source assembly: "Assembly-CSharp", "Assembly-CSharp-firstpass"
    file_path TEXT,                     -- Source file path
    line_number INTEGER,                -- Line where type is declared
    is_abstract INTEGER DEFAULT 0,
    is_sealed INTEGER DEFAULT 0,
    is_static INTEGER DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_types_name ON types(name);
CREATE INDEX IF NOT EXISTS idx_types_base ON types(base_type);
CREATE INDEX IF NOT EXISTS idx_types_assembly ON types(assembly);

-- Methods (including constructors, properties, etc.)
CREATE TABLE IF NOT EXISTS methods (
    id INTEGER PRIMARY KEY,
    type_id INTEGER NOT NULL,           -- FK to types
    name TEXT NOT NULL,                 -- Method name: "GetItemCount"
    signature TEXT NOT NULL,            -- Full signature: "GetItemCount(string, int)"
    return_type TEXT,                   -- Return type: "int", "void", etc.
    assembly TEXT,                      -- Source assembly: "Assembly-CSharp", "Assembly-CSharp-firstpass"
    file_path TEXT,                     -- Source file path
    line_number INTEGER,                -- Line where method starts
    end_line INTEGER,                   -- Line where method ends
    is_static INTEGER DEFAULT 0,
    is_virtual INTEGER DEFAULT 0,
    is_override INTEGER DEFAULT 0,
    is_abstract INTEGER DEFAULT 0,
    access TEXT,                        -- 'public', 'private', 'protected', 'internal'
    FOREIGN KEY (type_id) REFERENCES types(id)
);

CREATE INDEX IF NOT EXISTS idx_methods_type ON methods(type_id);
CREATE INDEX IF NOT EXISTS idx_methods_name ON methods(name);
CREATE INDEX IF NOT EXISTS idx_methods_sig ON methods(signature);
CREATE INDEX IF NOT EXISTS idx_methods_assembly ON methods(assembly);

-- Method calls (edges in call graph)
CREATE TABLE IF NOT EXISTS calls (
    id INTEGER PRIMARY KEY,
    caller_id INTEGER NOT NULL,         -- FK to methods (who makes the call)
    callee_id INTEGER NOT NULL,         -- FK to methods (who is called)
    file_path TEXT,                     -- Where the call occurs
    line_number INTEGER,                -- Line of the call
    call_type TEXT DEFAULT 'direct',    -- 'direct', 'virtual', 'delegate', 'reflection'
    FOREIGN KEY (caller_id) REFERENCES methods(id),
    FOREIGN KEY (callee_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_calls_caller ON calls(caller_id);
CREATE INDEX IF NOT EXISTS idx_calls_callee ON calls(callee_id);

-- Interface implementations
CREATE TABLE IF NOT EXISTS implements (
    id INTEGER PRIMARY KEY,
    type_id INTEGER NOT NULL,           -- FK to types (the implementing class)
    interface_name TEXT NOT NULL,       -- Full interface name
    FOREIGN KEY (type_id) REFERENCES types(id)
);

CREATE INDEX IF NOT EXISTS idx_implements_type ON implements(type_id);
CREATE INDEX IF NOT EXISTS idx_implements_interface ON implements(interface_name);

-- ============================================================================
-- FTS5: Full-Text Search on Method Bodies
-- ============================================================================

-- Note: Using content-backed FTS5 (stores data in the virtual table)
CREATE VIRTUAL TABLE IF NOT EXISTS method_bodies USING fts5(
    method_id UNINDEXED,    -- FK to methods (not searchable, just for joins)
    body,                   -- Full method body text (searchable)
    tokenize='porter'       -- Use porter stemming
);

-- ============================================================================
-- EXTERNAL CALLS: Calls to Unity, BCL, third-party libraries
-- ============================================================================

-- Track calls that go outside our codebase (into Unity, System, etc.)
-- These are the "boundary crossings" where crashes often originate
CREATE TABLE IF NOT EXISTS external_calls (
    id INTEGER PRIMARY KEY,
    caller_id INTEGER NOT NULL,         -- FK to methods (game method making the call)
    target_assembly TEXT,               -- "UnityEngine", "System", "mscorlib", etc.
    target_type TEXT NOT NULL,          -- Full type name
    target_method TEXT NOT NULL,        -- Method name
    target_signature TEXT,              -- Full signature if resolvable
    file_path TEXT,                     -- Where the call occurs
    line_number INTEGER,
    FOREIGN KEY (caller_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_external_caller ON external_calls(caller_id);
CREATE INDEX IF NOT EXISTS idx_external_assembly ON external_calls(target_assembly);
CREATE INDEX IF NOT EXISTS idx_external_target ON external_calls(target_type, target_method);

-- ============================================================================
-- GAME XML: Property Definitions from Game Data Files
-- ============================================================================

-- All properties defined in game XML files (items.xml, blocks.xml, etc.)
-- This catalogs what the game expects/reads from XML configuration
CREATE TABLE IF NOT EXISTS xml_definitions (
    id INTEGER PRIMARY KEY,
    file_name TEXT NOT NULL,            -- 'items.xml', 'blocks.xml', 'recipes.xml', etc.
    element_type TEXT NOT NULL,         -- 'item', 'block', 'recipe', 'progression', etc.
    element_name TEXT,                  -- 'gunPistol', 'frameShapes', etc. (name attribute)
    element_xpath TEXT NOT NULL,        -- Full xpath: '/items/item[@name="gunPistol"]'
    property_name TEXT,                 -- Property name if this is a property element
    property_value TEXT,                -- The value (for reference, may change with mods)
    property_class TEXT,                -- For items: class="Weapon", etc.
    line_number INTEGER                 -- Line in original XML file
);

CREATE INDEX IF NOT EXISTS idx_xmldef_file ON xml_definitions(file_name);
CREATE INDEX IF NOT EXISTS idx_xmldef_element ON xml_definitions(element_type, element_name);
CREATE INDEX IF NOT EXISTS idx_xmldef_property ON xml_definitions(property_name);
CREATE INDEX IF NOT EXISTS idx_xmldef_xpath ON xml_definitions(element_xpath);

-- ============================================================================
-- XML PROPERTY ACCESS: Code that reads XML properties
-- ============================================================================

-- Track where code reads XML properties (GetValue, GetInt, Contains, etc.)
-- Links XML properties to the code that uses them
CREATE TABLE IF NOT EXISTS xml_property_access (
    id INTEGER PRIMARY KEY,
    method_id INTEGER NOT NULL,         -- FK to methods (where the access occurs)
    property_name TEXT NOT NULL,        -- The property name string literal
    access_method TEXT NOT NULL,        -- 'GetValue', 'GetInt', 'GetBool', 'GetFloat', 'Contains', etc.
    receiver_type TEXT,                 -- Type of object being accessed: 'DynamicProperties', 'XElement', etc.
    file_path TEXT,
    line_number INTEGER,
    context TEXT,                       -- Additional context (surrounding code snippet)
    FOREIGN KEY (method_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_propaccess_method ON xml_property_access(method_id);
CREATE INDEX IF NOT EXISTS idx_propaccess_property ON xml_property_access(property_name);
CREATE INDEX IF NOT EXISTS idx_propaccess_type ON xml_property_access(receiver_type, access_method);

-- ============================================================================
-- MOD REGISTRY: Track all mods being analyzed
-- ============================================================================

CREATE TABLE IF NOT EXISTS mods (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,          -- Mod folder name / identifier
    display_name TEXT,                  -- From ModInfo.xml if available
    version TEXT,                       -- Mod version
    author TEXT,                        -- Mod author
    description TEXT,                   -- Mod description
    mod_path TEXT,                      -- Full path to mod folder
    has_harmony INTEGER DEFAULT 0,      -- 1 if mod uses Harmony patches
    has_xml_changes INTEGER DEFAULT 0,  -- 1 if mod has XML modifications
    analyzed_at TEXT                    -- ISO timestamp of analysis
);

CREATE INDEX IF NOT EXISTS idx_mods_name ON mods(name);

-- ============================================================================
-- MOD CODE: Types and methods from mods (same structure as game code)
-- ============================================================================

-- Mod types - links to mods table
CREATE TABLE IF NOT EXISTS mod_types (
    id INTEGER PRIMARY KEY,
    mod_id INTEGER NOT NULL,            -- FK to mods
    name TEXT NOT NULL,
    namespace TEXT,
    full_name TEXT NOT NULL,
    kind TEXT NOT NULL,                 -- 'class', 'struct', 'interface'
    base_type TEXT,
    file_path TEXT,
    line_number INTEGER,
    is_harmony_patch INTEGER DEFAULT 0, -- 1 if this is a [HarmonyPatch] class
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

CREATE INDEX IF NOT EXISTS idx_modtypes_mod ON mod_types(mod_id);
CREATE INDEX IF NOT EXISTS idx_modtypes_name ON mod_types(name);
CREATE INDEX IF NOT EXISTS idx_modtypes_harmony ON mod_types(is_harmony_patch);

-- Mod methods
CREATE TABLE IF NOT EXISTS mod_methods (
    id INTEGER PRIMARY KEY,
    mod_type_id INTEGER NOT NULL,       -- FK to mod_types
    name TEXT NOT NULL,
    signature TEXT NOT NULL,
    return_type TEXT,
    file_path TEXT,
    line_number INTEGER,
    end_line INTEGER,
    is_prefix INTEGER DEFAULT 0,        -- Harmony prefix
    is_postfix INTEGER DEFAULT 0,       -- Harmony postfix
    is_transpiler INTEGER DEFAULT 0,    -- Harmony transpiler
    is_finalizer INTEGER DEFAULT 0,     -- Harmony finalizer
    FOREIGN KEY (mod_type_id) REFERENCES mod_types(id)
);

CREATE INDEX IF NOT EXISTS idx_modmethods_type ON mod_methods(mod_type_id);
CREATE INDEX IF NOT EXISTS idx_modmethods_name ON mod_methods(name);

-- Mod method bodies for FTS
CREATE VIRTUAL TABLE IF NOT EXISTS mod_method_bodies USING fts5(
    mod_method_id UNINDEXED,
    body,
    tokenize='porter'
);

-- ============================================================================
-- MOD TABLES: Harmony Patches (detailed)
-- ============================================================================

CREATE TABLE IF NOT EXISTS harmony_patches (
    id INTEGER PRIMARY KEY,
    mod_id INTEGER,                     -- FK to mods (null for legacy data)
    mod_name TEXT NOT NULL,             -- Mod identifier (kept for backward compat)
    patch_class TEXT,                   -- The mod's patch class name
    patch_method TEXT,                  -- Prefix/Postfix/Transpiler method name
    target_type TEXT NOT NULL,          -- Game class being patched
    target_method TEXT NOT NULL,        -- Game method being patched
    target_signature TEXT,              -- Full signature if specified
    patch_type TEXT NOT NULL,           -- 'Prefix', 'Postfix', 'Transpiler', 'Finalizer'
    priority INTEGER DEFAULT 400,       -- Harmony priority (default 400)
    before TEXT,                        -- Harmony 'before' attribute (JSON array)
    after TEXT,                         -- Harmony 'after' attribute (JSON array)
    argument_types TEXT,                -- JSON array of argument types if specified
    game_method_id INTEGER,             -- FK to methods (resolved target in game code)
    mod_method_id INTEGER,              -- FK to mod_methods (the patch implementation)
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (mod_id) REFERENCES mods(id),
    FOREIGN KEY (game_method_id) REFERENCES methods(id),
    FOREIGN KEY (mod_method_id) REFERENCES mod_methods(id)
);

CREATE INDEX IF NOT EXISTS idx_patches_mod ON harmony_patches(mod_id);
CREATE INDEX IF NOT EXISTS idx_patches_modname ON harmony_patches(mod_name);
CREATE INDEX IF NOT EXISTS idx_patches_target ON harmony_patches(target_type, target_method);
CREATE INDEX IF NOT EXISTS idx_patches_game_method ON harmony_patches(game_method_id);

-- ============================================================================
-- MOD TABLES: XML Changes (detailed)
-- ============================================================================

CREATE TABLE IF NOT EXISTS xml_changes (
    id INTEGER PRIMARY KEY,
    mod_id INTEGER,                     -- FK to mods (null for legacy data)
    mod_name TEXT NOT NULL,             -- Mod identifier
    file_name TEXT NOT NULL,            -- Target file: 'items.xml', 'blocks.xml', etc.
    xpath TEXT NOT NULL,                -- XPath expression to target node
    operation TEXT NOT NULL,            -- 'set', 'append', 'remove', 'insertAfter', 'insertBefore', 'setattribute', 'removeattribute'
    attribute TEXT,                     -- Attribute name if applicable
    value TEXT,                         -- New value if applicable
    xml_def_id INTEGER,                 -- FK to xml_definitions (what this modifies)
    file_path TEXT,                     -- Source XML file in mod
    line_number INTEGER,
    FOREIGN KEY (mod_id) REFERENCES mods(id),
    FOREIGN KEY (xml_def_id) REFERENCES xml_definitions(id)
);

CREATE INDEX IF NOT EXISTS idx_xml_mod ON xml_changes(mod_id);
CREATE INDEX IF NOT EXISTS idx_xml_modname ON xml_changes(mod_name);
CREATE INDEX IF NOT EXISTS idx_xml_target ON xml_changes(file_name, xpath);
CREATE INDEX IF NOT EXISTS idx_xml_def ON xml_changes(xml_def_id);

-- ============================================================================
-- CONFLICT DETECTION: Pre-computed conflict analysis
-- ============================================================================

-- Direct conflicts between mods (computed during analysis)
CREATE TABLE IF NOT EXISTS mod_conflicts (
    id INTEGER PRIMARY KEY,
    mod1_id INTEGER NOT NULL,           -- First mod
    mod2_id INTEGER NOT NULL,           -- Second mod
    conflict_type TEXT NOT NULL,        -- 'harmony_same_target', 'xml_same_path', 'xml_same_property'
    severity TEXT NOT NULL,             -- 'high', 'medium', 'low'
    target_description TEXT,            -- Human-readable description of conflict target
    details TEXT,                       -- JSON with detailed conflict info
    FOREIGN KEY (mod1_id) REFERENCES mods(id),
    FOREIGN KEY (mod2_id) REFERENCES mods(id)
);

CREATE INDEX IF NOT EXISTS idx_conflicts_mod1 ON mod_conflicts(mod1_id);
CREATE INDEX IF NOT EXISTS idx_conflicts_mod2 ON mod_conflicts(mod2_id);
CREATE INDEX IF NOT EXISTS idx_conflicts_type ON mod_conflicts(conflict_type);

-- ============================================================================
-- METADATA
-- ============================================================================

CREATE TABLE IF NOT EXISTS metadata (
    key TEXT PRIMARY KEY,
    value TEXT
);

-- Store version info, build timestamps, etc.
-- Example entries:
-- ('schema_version', '1')
-- ('game_version', 'V1.1 b14')
-- ('build_timestamp', '2024-12-31T10:30:00')
-- ('source_path', 'C:\...\7D2DCodebase')
