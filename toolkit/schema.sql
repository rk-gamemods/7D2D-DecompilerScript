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
-- MOD TABLES: Harmony Patches
-- ============================================================================

CREATE TABLE IF NOT EXISTS harmony_patches (
    id INTEGER PRIMARY KEY,
    mod_name TEXT NOT NULL,             -- Mod identifier
    patch_class TEXT,                   -- The mod's patch class name
    patch_method TEXT,                  -- Prefix/Postfix/Transpiler method name
    target_type TEXT NOT NULL,          -- Game class being patched
    target_method TEXT NOT NULL,        -- Game method being patched
    target_signature TEXT,              -- Full signature if specified
    patch_type TEXT NOT NULL,           -- 'Prefix', 'Postfix', 'Transpiler', 'Finalizer'
    priority INTEGER DEFAULT 400,       -- Harmony priority (default 400)
    before TEXT,                        -- Harmony 'before' attribute
    after TEXT,                         -- Harmony 'after' attribute
    file_path TEXT,                     -- Source file in mod
    line_number INTEGER                 -- Line of [HarmonyPatch] attribute
);

CREATE INDEX IF NOT EXISTS idx_patches_mod ON harmony_patches(mod_name);
CREATE INDEX IF NOT EXISTS idx_patches_target ON harmony_patches(target_type, target_method);

-- ============================================================================
-- MOD TABLES: XML Changes
-- ============================================================================

CREATE TABLE IF NOT EXISTS xml_changes (
    id INTEGER PRIMARY KEY,
    mod_name TEXT NOT NULL,             -- Mod identifier
    file_name TEXT NOT NULL,            -- Target file: 'items.xml', 'blocks.xml', etc.
    xpath TEXT NOT NULL,                -- XPath expression to target node
    operation TEXT NOT NULL,            -- 'set', 'append', 'remove', 'insertAfter', 'insertBefore', 'setattribute', 'removeattribute'
    attribute TEXT,                     -- Attribute name if applicable
    value TEXT,                         -- New value if applicable
    file_path TEXT,                     -- Source XML file in mod
    line_number INTEGER                 -- Line of the change
);

CREATE INDEX IF NOT EXISTS idx_xml_mod ON xml_changes(mod_name);
CREATE INDEX IF NOT EXISTS idx_xml_target ON xml_changes(file_name, xpath);

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
