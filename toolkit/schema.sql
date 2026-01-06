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
-- ECOSYSTEM: XML Definitions and Entity Tracking
-- ============================================================================

-- Game XML definitions (items, blocks, buffs, etc.)
CREATE TABLE IF NOT EXISTS xml_definitions (
    id INTEGER PRIMARY KEY,
    definition_type TEXT NOT NULL,      -- 'item', 'block', 'buff', 'recipe', etc.
    name TEXT NOT NULL,                 -- Entity name from XML
    file_path TEXT NOT NULL,            -- Source XML file path
    line_number INTEGER,                -- Line in source file
    extends TEXT                        -- Parent definition if using Extends
);

CREATE INDEX IF NOT EXISTS idx_xml_def_type_name ON xml_definitions(definition_type, name);
CREATE INDEX IF NOT EXISTS idx_xml_def_name ON xml_definitions(name);

-- XML properties for each definition
CREATE TABLE IF NOT EXISTS xml_properties (
    id INTEGER PRIMARY KEY,
    definition_id INTEGER,              -- FK to xml_definitions
    property_name TEXT NOT NULL,
    property_value TEXT,
    property_class TEXT,                -- For nested <property class="Action0"> etc.
    line_number INTEGER,
    FOREIGN KEY (definition_id) REFERENCES xml_definitions(id)
);

CREATE INDEX IF NOT EXISTS idx_xml_props_def ON xml_properties(definition_id);
CREATE INDEX IF NOT EXISTS idx_xml_props_name ON xml_properties(property_name);

-- XML cross-references between definitions
CREATE TABLE IF NOT EXISTS xml_references (
    id INTEGER PRIMARY KEY,
    source_type TEXT NOT NULL,          -- 'xml' or 'csharp'
    source_def_id INTEGER,              -- FK to xml_definitions if source_type='xml'
    source_file TEXT NOT NULL,
    source_line INTEGER,
    target_type TEXT NOT NULL,          -- 'item', 'block', 'buff', etc.
    target_name TEXT NOT NULL,
    reference_context TEXT
);

CREATE INDEX IF NOT EXISTS idx_xml_refs_target ON xml_references(target_type, target_name);

-- Ecosystem entities (unified view of all game entities)
CREATE TABLE IF NOT EXISTS ecosystem_entities (
    id INTEGER PRIMARY KEY,
    entity_type TEXT NOT NULL,          -- 'item', 'block', 'buff', etc.
    entity_name TEXT NOT NULL,
    source TEXT NOT NULL,               -- 'vanilla' or mod name
    status TEXT DEFAULT 'active',       -- 'active', 'modified', 'removed'
    modified_by TEXT,                   -- Mod that modified this
    removed_by TEXT,                    -- Mod that removed this
    depended_on_by TEXT                 -- JSON array of mods that depend on this
);

CREATE INDEX IF NOT EXISTS idx_ecosystem_type ON ecosystem_entities(entity_type, entity_name);

-- Semantic mappings for entities (AI-generated descriptions)
CREATE TABLE IF NOT EXISTS semantic_mappings (
    id INTEGER PRIMARY KEY,
    entity_type TEXT NOT NULL,
    entity_name TEXT NOT NULL,
    parent_context TEXT,                -- Parent entity if applicable
    layman_description TEXT,            -- Plain English description
    technical_description TEXT,         -- Technical/modding description
    player_impact TEXT,                 -- How this affects gameplay
    related_systems TEXT,               -- Related game systems
    example_usage TEXT,                 -- Example mod usage
    generated_by TEXT DEFAULT 'llm',
    confidence REAL DEFAULT 0.8,
    llm_model TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(entity_type, entity_name, parent_context)
);

CREATE INDEX IF NOT EXISTS idx_semantic_entity ON semantic_mappings(entity_type, entity_name);

-- Entity type statistics
CREATE TABLE IF NOT EXISTS xml_stats (
    definition_type TEXT PRIMARY KEY,
    count INTEGER
);

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
    name TEXT UNIQUE NOT NULL,          -- Mod folder name / identifier
    has_xml INTEGER DEFAULT 0,          -- 1 if mod has XML modifications
    has_dll INTEGER DEFAULT 0,          -- 1 if mod has DLL/Harmony
    xml_operations INTEGER DEFAULT 0,   -- Count of XML operations
    csharp_dependencies INTEGER DEFAULT 0, -- Count of C# dependencies
    conflicts INTEGER DEFAULT 0,        -- Detected conflict count
    cautions INTEGER DEFAULT 0          -- Detected caution count
);

CREATE INDEX IF NOT EXISTS idx_mods_name ON mods(name);

-- Mod C# dependencies (what game code the mod relies on)
CREATE TABLE IF NOT EXISTS mod_csharp_deps (
    id INTEGER PRIMARY KEY,
    mod_id INTEGER,                     -- FK to mods
    dependency_type TEXT NOT NULL,      -- 'type', 'method', 'property', etc.
    dependency_name TEXT NOT NULL,      -- Name of the dependency
    source_file TEXT,                   -- File in mod where dependency is used
    line_number INTEGER,
    pattern TEXT,                       -- Pattern matched to find this
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

-- Mod XML operations (what XML changes the mod makes)
CREATE TABLE IF NOT EXISTS mod_xml_operations (
    id INTEGER PRIMARY KEY,
    mod_id INTEGER,                     -- FK to mods
    operation TEXT NOT NULL,            -- 'set', 'append', 'remove', etc.
    xpath TEXT NOT NULL,                -- XPath target
    target_type TEXT,                   -- 'item', 'block', etc.
    target_name TEXT,                   -- Target entity name
    property_name TEXT,                 -- Property being modified
    new_value TEXT,                     -- New value if applicable
    element_content TEXT,               -- Full element content if complex
    file_path TEXT,                     -- Source file in mod
    line_number INTEGER,
    impact_status TEXT,                 -- 'safe', 'caution', 'conflict'
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

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
-- EVENT FLOW ANALYSIS: Track event subscriptions and invocations
-- ============================================================================

-- Event subscriptions: who listens to what events
CREATE TABLE IF NOT EXISTS event_subscriptions (
    id INTEGER PRIMARY KEY,
    subscriber_method_id INTEGER,       -- Method that contains the subscription (FK to methods or mod_methods)
    subscriber_type TEXT NOT NULL,      -- Full type name containing the subscriber
    event_owner_type TEXT NOT NULL,     -- Type that owns the event (e.g., EntityPlayerLocal)
    event_name TEXT NOT NULL,           -- Event field name (e.g., DragAndDropItemChanged)
    handler_method TEXT NOT NULL,       -- Method invoked when event fires
    handler_type TEXT,                  -- Type containing the handler (if different from subscriber)
    subscription_type TEXT DEFAULT 'add', -- 'add' (+=) or 'remove' (-=)
    is_mod INTEGER DEFAULT 0,           -- 1 if from mod code
    mod_id INTEGER,                     -- FK to mods if is_mod=1
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (subscriber_method_id) REFERENCES methods(id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

CREATE INDEX IF NOT EXISTS idx_eventsub_event ON event_subscriptions(event_owner_type, event_name);
CREATE INDEX IF NOT EXISTS idx_eventsub_handler ON event_subscriptions(handler_method);
CREATE INDEX IF NOT EXISTS idx_eventsub_subscriber ON event_subscriptions(subscriber_type);
CREATE INDEX IF NOT EXISTS idx_eventsub_mod ON event_subscriptions(mod_id);

-- Event invocations: where events are fired
CREATE TABLE IF NOT EXISTS event_fires (
    id INTEGER PRIMARY KEY,
    firing_method_id INTEGER,           -- Method that fires the event
    firing_type TEXT NOT NULL,          -- Type containing the fire statement
    event_owner_type TEXT NOT NULL,     -- Type that owns the event
    event_name TEXT NOT NULL,           -- Event being fired
    fire_method TEXT DEFAULT 'Invoke',  -- 'Invoke', 'DynamicInvoke', or 'direct'
    is_conditional INTEGER DEFAULT 0,   -- 1 if fired conditionally (null check etc)
    is_mod INTEGER DEFAULT 0,           -- 1 if from mod code
    mod_id INTEGER,                     -- FK to mods if is_mod=1
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (firing_method_id) REFERENCES methods(id),
    FOREIGN KEY (mod_id) REFERENCES mods(id)
);

CREATE INDEX IF NOT EXISTS idx_eventfire_event ON event_fires(event_owner_type, event_name);
CREATE INDEX IF NOT EXISTS idx_eventfire_firing ON event_fires(firing_type);
CREATE INDEX IF NOT EXISTS idx_eventfire_mod ON event_fires(mod_id);

-- Event declarations: what events exist and their signatures
CREATE TABLE IF NOT EXISTS event_declarations (
    id INTEGER PRIMARY KEY,
    type_id INTEGER,                    -- FK to types (owning type)
    owning_type TEXT NOT NULL,          -- Full type name
    event_name TEXT NOT NULL,           -- Event field name
    delegate_type TEXT,                 -- Delegate type (e.g., Action, EventHandler)
    is_public INTEGER DEFAULT 1,
    file_path TEXT,
    line_number INTEGER,
    FOREIGN KEY (type_id) REFERENCES types(id)
);

CREATE INDEX IF NOT EXISTS idx_eventdecl_type ON event_declarations(owning_type);
CREATE INDEX IF NOT EXISTS idx_eventdecl_name ON event_declarations(event_name);

-- ============================================================================
-- BEHAVIORAL FLOWS: Pre-computed cause-effect chains
-- ============================================================================

-- Pre-computed flows from triggers to outcomes
CREATE TABLE IF NOT EXISTS behavioral_flows (
    id INTEGER PRIMARY KEY,
    trigger_description TEXT NOT NULL,  -- "Player moves item to container"
    trigger_type TEXT,                  -- 'method_call', 'event_fire', 'user_action'
    trigger_method_id INTEGER,          -- Initial method (FK to methods)
    trigger_event TEXT,                 -- Or initial event name
    outcome_description TEXT NOT NULL,  -- "Challenge progress updates"
    outcome_method_id INTEGER,          -- Final method affected
    flow_json TEXT NOT NULL,            -- Full flow as JSON tree structure
    mods_involved TEXT,                 -- JSON array of mod names involved
    verified INTEGER DEFAULT 0,         -- 1 if manually verified working
    notes TEXT,                         -- Additional context
    FOREIGN KEY (trigger_method_id) REFERENCES methods(id),
    FOREIGN KEY (outcome_method_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_flow_trigger ON behavioral_flows(trigger_description);
CREATE INDEX IF NOT EXISTS idx_flow_outcome ON behavioral_flows(outcome_description);

-- ============================================================================
-- EFFECTIVE BEHAVIOR: Methods with patches applied
-- ============================================================================

-- Cached effective behavior of methods with patches
CREATE TABLE IF NOT EXISTS effective_methods (
    id INTEGER PRIMARY KEY,
    method_id INTEGER NOT NULL UNIQUE,  -- FK to methods (game method)
    vanilla_behavior TEXT,              -- Summary of what vanilla code does
    has_prefix INTEGER DEFAULT 0,
    has_postfix INTEGER DEFAULT 0,
    has_transpiler INTEGER DEFAULT 0,
    has_finalizer INTEGER DEFAULT 0,
    prefix_mods TEXT,                   -- JSON: [{mod, priority, effect}]
    postfix_mods TEXT,                  -- JSON: [{mod, priority, effect}]
    transpiler_mods TEXT,               -- JSON: [{mod, effect}]
    effective_behavior TEXT,            -- Summary of net effect with all patches
    affected_callers INTEGER DEFAULT 0, -- Count of methods that call this
    last_updated TEXT,                  -- ISO timestamp
    FOREIGN KEY (method_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_effective_method ON effective_methods(method_id);
CREATE INDEX IF NOT EXISTS idx_effective_patched ON effective_methods(has_prefix, has_postfix, has_transpiler);

-- ============================================================================
-- METADATA
-- ============================================================================

CREATE TABLE IF NOT EXISTS metadata (
    key TEXT PRIMARY KEY,
    value TEXT
);

-- Store version info, build timestamps, etc.
-- Example entries:
-- ('schema_version', '3')  -- Updated for consolidation
-- ('game_version', 'V1.1 b14')
-- ('build_timestamp', '2024-12-31T10:30:00')
-- ('source_path', 'C:\...\7D2DCodebase')

-- ============================================================================
-- CACHING: Query result cache for performance
-- ============================================================================

CREATE TABLE IF NOT EXISTS query_cache (
    query_hash TEXT PRIMARY KEY,
    query_text TEXT,
    result_json TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    hit_count INTEGER DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_query_cache_hash ON query_cache(query_hash);

-- ============================================================================
-- AI SUMMARIES: LLM-generated method summaries
-- ============================================================================

CREATE TABLE IF NOT EXISTS method_summaries (
    method_id INTEGER PRIMARY KEY,
    summary TEXT,                       -- Plain English description
    complexity TEXT,                    -- 'simple', 'moderate', 'complex'
    side_effects TEXT,                  -- Description of side effects
    thread_safety TEXT,                 -- Thread safety notes
    generated_at TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (method_id) REFERENCES methods(id)
);

CREATE INDEX IF NOT EXISTS idx_method_summaries_method ON method_summaries(method_id);

-- ============================================================================
-- TRANSITIVE REFERENCES: Pre-computed entity dependency chains
-- ============================================================================
-- Purpose: Store every A→B relationship including indirect ones
-- so we don't have to recompute chains every time we ask "what depends on X?"
--
-- Example: gunPistolT3 → extends → gunPistolT2 → extends → gunPistolT1
-- Stored as 3 rows:
--   gunPistolT3 → gunPistolT2 (depth 1)
--   gunPistolT3 → gunPistolT1 (depth 2, through gunPistolT2)
--   gunPistolT3 → gunPistolMaster (depth 3, through T2 and T1)

CREATE TABLE IF NOT EXISTS transitive_references (
    id INTEGER PRIMARY KEY,
    source_def_id INTEGER NOT NULL,     -- The entity that depends on something
    target_def_id INTEGER NOT NULL,     -- The entity being depended on
    path_depth INTEGER NOT NULL,        -- How many hops (1 = direct, 2+ = indirect)
    path_json TEXT NOT NULL,            -- Full chain as JSON: ["extends:gunPistolT2", "extends:gunPistolT1"]
    reference_types TEXT,               -- Unique ref types in path: "extends,triggered_effect"
    UNIQUE(source_def_id, target_def_id),
    FOREIGN KEY (source_def_id) REFERENCES xml_definitions(id),
    FOREIGN KEY (target_def_id) REFERENCES xml_definitions(id)
);

CREATE INDEX IF NOT EXISTS idx_transitive_source ON transitive_references(source_def_id);
CREATE INDEX IF NOT EXISTS idx_transitive_target ON transitive_references(target_def_id);
CREATE INDEX IF NOT EXISTS idx_transitive_depth ON transitive_references(path_depth);

-- ============================================================================
-- MOD INDIRECT CONFLICTS: Stored conflict analysis results
-- ============================================================================
-- Purpose: Store detected indirect mod interactions with severity classification
-- so we don't re-analyze every time we ask "do these mods conflict?"
--
-- Severity framing (preserve for tooltips):
-- "Most overlaps are informational, not problems. 
--  Severity indicates how much attention something deserves, not that something is wrong."
--
-- LOW = "Here's an interaction you might want to know about"
-- MEDIUM = "Impactful gameplay change, worth verifying this is what you want"
-- HIGH = "Will likely cause errors or broken functionality"

CREATE TABLE IF NOT EXISTS mod_indirect_conflicts (
    id INTEGER PRIMARY KEY,
    mod1_id INTEGER NOT NULL,
    mod2_id INTEGER NOT NULL,
    shared_entity_id INTEGER NOT NULL,  -- The entity both mods affect (directly or indirectly)
    mod1_entity_id INTEGER,             -- What mod1 directly touches
    mod2_entity_id INTEGER,             -- What mod2 directly touches
    mod1_operation TEXT,                -- 'set', 'append', 'remove', etc.
    mod2_operation TEXT,
    mod1_path_json TEXT,                -- How mod1 reaches shared entity
    mod2_path_json TEXT,                -- How mod2 reaches shared entity
    severity TEXT NOT NULL,             -- 'low', 'medium', 'high'
    pattern_id TEXT NOT NULL,           -- 'L1', 'M2', 'H5', etc.
    pattern_name TEXT,                  -- Human readable: 'Additive stacking', 'Deleted entity referenced'
    explanation TEXT,                   -- Auto-generated description of the interaction
    FOREIGN KEY (mod1_id) REFERENCES mods(id),
    FOREIGN KEY (mod2_id) REFERENCES mods(id),
    FOREIGN KEY (shared_entity_id) REFERENCES xml_definitions(id)
);

CREATE INDEX IF NOT EXISTS idx_indirect_mod1 ON mod_indirect_conflicts(mod1_id);
CREATE INDEX IF NOT EXISTS idx_indirect_mod2 ON mod_indirect_conflicts(mod2_id);
CREATE INDEX IF NOT EXISTS idx_indirect_shared ON mod_indirect_conflicts(shared_entity_id);
CREATE INDEX IF NOT EXISTS idx_indirect_severity ON mod_indirect_conflicts(severity);
CREATE INDEX IF NOT EXISTS idx_indirect_pattern ON mod_indirect_conflicts(pattern_id);

-- ============================================================================
-- VIEWS: Convenient query shortcuts for common questions
-- ============================================================================

-- View: Inheritance hotspots - entities with many dependents
-- Use: "What are the most dangerous entities to modify?"
CREATE VIEW IF NOT EXISTS v_inheritance_hotspots AS
SELECT 
    d.name as base_entity,
    d.definition_type,
    COUNT(*) as dependent_count,
    CASE 
        WHEN COUNT(*) > 100 THEN 'CRITICAL'
        WHEN COUNT(*) > 50 THEN 'HIGH'
        WHEN COUNT(*) > 20 THEN 'MEDIUM'
        ELSE 'LOW'
    END as risk_level
FROM transitive_references tr
JOIN xml_definitions d ON tr.target_def_id = d.id
GROUP BY tr.target_def_id
ORDER BY dependent_count DESC;

-- View: Entity type counts (replaces hardcoded markdown tables)
-- Use: "How many items, blocks, buffs, etc. exist?"
CREATE VIEW IF NOT EXISTS v_entity_counts AS
SELECT definition_type, COUNT(*) as count
FROM xml_definitions
GROUP BY definition_type
ORDER BY count DESC;

-- View: Reference type distribution
-- Use: "What kinds of references exist and how many?"
CREATE VIEW IF NOT EXISTS v_reference_type_counts AS
SELECT 
    reference_context,
    COUNT(*) as count,
    COUNT(DISTINCT target_name) as unique_targets
FROM xml_references
GROUP BY reference_context
ORDER BY count DESC;

-- View: Most referenced buffs
-- Use: "Which buffs are most connected to other systems?"
CREATE VIEW IF NOT EXISTS v_most_referenced_buffs AS
SELECT 
    target_name as buff_name,
    COUNT(*) as reference_count,
    GROUP_CONCAT(DISTINCT reference_context) as reference_types
FROM xml_references
WHERE target_type = 'buff'
GROUP BY target_name
ORDER BY reference_count DESC;

-- View: Conflict summary by severity
-- Use: "Give me an overview of mod interactions"
CREATE VIEW IF NOT EXISTS v_conflict_summary AS
SELECT 
    severity,
    COUNT(*) as conflict_count,
    GROUP_CONCAT(DISTINCT pattern_id) as patterns_found
FROM mod_indirect_conflicts
GROUP BY severity
ORDER BY 
    CASE severity 
        WHEN 'high' THEN 1 
        WHEN 'medium' THEN 2 
        WHEN 'low' THEN 3 
    END;

-- View: Mod interaction matrix
-- Use: "Which mod pairs have the most interactions?"
CREATE VIEW IF NOT EXISTS v_mod_interaction_matrix AS
SELECT 
    m1.name as mod1_name,
    m2.name as mod2_name,
    COUNT(*) as interaction_count,
    SUM(CASE WHEN mic.severity = 'high' THEN 1 ELSE 0 END) as high_count,
    SUM(CASE WHEN mic.severity = 'medium' THEN 1 ELSE 0 END) as medium_count,
    SUM(CASE WHEN mic.severity = 'low' THEN 1 ELSE 0 END) as low_count
FROM mod_indirect_conflicts mic
JOIN mods m1 ON mic.mod1_id = m1.id
JOIN mods m2 ON mic.mod2_id = m2.id
GROUP BY mic.mod1_id, mic.mod2_id
ORDER BY high_count DESC, medium_count DESC, interaction_count DESC;
