"""
Database access layer for the call graph SQLite database.
"""

import sqlite3
from pathlib import Path
from typing import Any


class CallGraphDB:
    """Wrapper for the call graph SQLite database."""
    
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self._conn: sqlite3.Connection | None = None
    
    def connect(self) -> sqlite3.Connection:
        """Get or create database connection."""
        if self._conn is None:
            self._conn = sqlite3.connect(self.db_path)
            self._conn.row_factory = sqlite3.Row
        return self._conn
    
    def close(self):
        """Close database connection."""
        if self._conn:
            self._conn.close()
            self._conn = None
    
    def __enter__(self):
        self.connect()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()
    
    def query(self, sql: str, params: tuple = ()) -> list[sqlite3.Row]:
        """Execute a query and return all results."""
        conn = self.connect()
        cursor = conn.execute(sql, params)
        return cursor.fetchall()
    
    def query_one(self, sql: str, params: tuple = ()) -> sqlite3.Row | None:
        """Execute a query and return first result."""
        conn = self.connect()
        cursor = conn.execute(sql, params)
        return cursor.fetchone()
    
    # ==========================================================================
    # Call Graph Queries
    # ==========================================================================
    
    def get_method_by_name(self, name: str) -> list[dict[str, Any]]:
        """Find methods by name (may return multiple for overloads)."""
        sql = """
            SELECT m.id, m.name, m.signature, m.return_type, 
                   t.full_name as type_name, m.file_path, m.line_number
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.name = ? OR m.signature LIKE ?
        """
        rows = self.query(sql, (name, f"{name}(%"))
        return [dict(row) for row in rows]
    
    def get_callers(self, method_id: int) -> list[dict[str, Any]]:
        """Get all methods that call the specified method."""
        sql = """
            SELECT m.id, m.name, m.signature, t.full_name as type_name,
                   c.file_path, c.line_number
            FROM calls c
            JOIN methods m ON c.caller_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE c.callee_id = ?
        """
        rows = self.query(sql, (method_id,))
        return [dict(row) for row in rows]
    
    def get_callees(self, method_id: int) -> list[dict[str, Any]]:
        """Get all methods called by the specified method."""
        sql = """
            SELECT m.id, m.name, m.signature, t.full_name as type_name,
                   c.file_path, c.line_number
            FROM calls c
            JOIN methods m ON c.callee_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE c.caller_id = ?
        """
        rows = self.query(sql, (method_id,))
        return [dict(row) for row in rows]
    
    def get_all_edges(self) -> list[tuple[int, int]]:
        """Get all call edges for graph construction."""
        sql = "SELECT caller_id, callee_id FROM calls"
        rows = self.query(sql)
        return [(row['caller_id'], row['callee_id']) for row in rows]
    
    def get_method_info(self, method_id: int) -> dict[str, Any] | None:
        """Get full info for a method by ID."""
        sql = """
            SELECT m.*, t.full_name as type_name
            FROM methods m
            JOIN types t ON m.type_id = t.id
            WHERE m.id = ?
        """
        row = self.query_one(sql, (method_id,))
        return dict(row) if row else None
    
    # ==========================================================================
    # FTS5 Search
    # ==========================================================================
    
    def search_method_bodies(self, query: str, limit: int = 50) -> list[dict[str, Any]]:
        """Full-text search on method bodies."""
        sql = """
            SELECT m.id, m.name, m.signature, t.full_name as type_name,
                   m.file_path, m.line_number,
                   snippet(method_bodies, 1, '>>>', '<<<', '...', 32) as snippet
            FROM method_bodies
            JOIN methods m ON method_bodies.method_id = m.id
            JOIN types t ON m.type_id = t.id
            WHERE method_bodies MATCH ?
            LIMIT ?
        """
        rows = self.query(sql, (query, limit))
        return [dict(row) for row in rows]
    
    # ==========================================================================
    # Mod Compatibility Queries
    # ==========================================================================
    
    def find_patch_conflicts(self) -> list[dict[str, Any]]:
        """Find methods patched by multiple mods."""
        sql = """
            SELECT target_type, target_method, 
                   GROUP_CONCAT(mod_name) as mods,
                   COUNT(*) as patch_count
            FROM harmony_patches
            GROUP BY target_type, target_method
            HAVING COUNT(DISTINCT mod_name) > 1
        """
        rows = self.query(sql)
        return [dict(row) for row in rows]
    
    def find_xml_conflicts(self) -> list[dict[str, Any]]:
        """Find XML nodes modified by multiple mods."""
        sql = """
            SELECT file_name, xpath,
                   GROUP_CONCAT(mod_name) as mods,
                   COUNT(*) as change_count
            FROM xml_changes
            GROUP BY file_name, xpath
            HAVING COUNT(DISTINCT mod_name) > 1
        """
        rows = self.query(sql)
        return [dict(row) for row in rows]
    
    def get_mod_patches(self, mod_name: str) -> list[dict[str, Any]]:
        """Get all Harmony patches for a specific mod."""
        sql = """
            SELECT * FROM harmony_patches WHERE mod_name = ?
        """
        rows = self.query(sql, (mod_name,))
        return [dict(row) for row in rows]
    
    def get_mod_xml_changes(self, mod_name: str) -> list[dict[str, Any]]:
        """Get all XML changes for a specific mod."""
        sql = """
            SELECT * FROM xml_changes WHERE mod_name = ?
        """
        rows = self.query(sql, (mod_name,))
        return [dict(row) for row in rows]
