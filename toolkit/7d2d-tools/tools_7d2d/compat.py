"""
Mod compatibility analysis.
"""

from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .db import CallGraphDB
from .graph import CallGraph


@dataclass
class Conflict:
    """Represents a compatibility conflict between mods."""
    type: str           # 'direct_patch', 'xml_collision', 'indirect_behavioral'
    severity: str       # 'high', 'medium', 'low'
    target: str         # What's being conflicted over
    mods_involved: list[str]
    details: list[dict[str, Any]]
    resolution: str     # Suggested resolution


@dataclass
class LoadOrderSuggestion:
    """Suggested load order between mods."""
    recommendation: str
    reason: str


@dataclass
class CompatibilityReport:
    """Full compatibility report for a set of mods."""
    mods: list[str]
    conflicts: list[Conflict]
    load_order_suggestions: list[LoadOrderSuggestion]
    
    @property
    def is_compatible(self) -> bool:
        return len(self.conflicts) == 0
    
    @property
    def is_compatible_with_caveats(self) -> bool:
        high_severity = sum(1 for c in self.conflicts if c.severity == 'high')
        return high_severity == 0
    
    def to_dict(self) -> dict[str, Any]:
        """Convert to JSON-serializable dict."""
        return {
            "query": "compat",
            "mods": self.mods,
            "conflicts": [
                {
                    "type": c.type,
                    "severity": c.severity,
                    "target": c.target,
                    "mods_involved": c.mods_involved,
                    "details": c.details,
                    "resolution": c.resolution
                }
                for c in self.conflicts
            ],
            "load_order_suggestions": [
                {
                    "recommendation": s.recommendation,
                    "reason": s.reason
                }
                for s in self.load_order_suggestions
            ],
            "summary": {
                "total_conflicts": len(self.conflicts),
                "high_severity": sum(1 for c in self.conflicts if c.severity == 'high'),
                "medium_severity": sum(1 for c in self.conflicts if c.severity == 'medium'),
                "low_severity": sum(1 for c in self.conflicts if c.severity == 'low'),
                "compatible": self.is_compatible,
                "compatible_with_caveats": self.is_compatible_with_caveats
            }
        }


def check_compatibility(db: CallGraphDB, mod_names: list[str]) -> CompatibilityReport:
    """
    Check compatibility between multiple mods.
    
    Args:
        db: Database with mod patch information
        mod_names: List of mod names to check
        
    Returns:
        CompatibilityReport with all conflicts and suggestions
    """
    conflicts = []
    suggestions = []
    
    # 1. Find direct Harmony patch conflicts
    patch_conflicts = db.find_patch_conflicts()
    for conflict in patch_conflicts:
        involved_mods = conflict['mods'].split(',')
        # Filter to only mods we're checking
        involved_mods = [m for m in involved_mods if m in mod_names]
        if len(involved_mods) > 1:
            conflicts.append(Conflict(
                type='direct_patch',
                severity='high',
                target=f"{conflict['target_type']}.{conflict['target_method']}",
                mods_involved=involved_mods,
                details=[],  # TODO: Add per-mod patch details
                resolution="Both mods patch the same method. Test thoroughly for conflicts."
            ))
    
    # 2. Find XML collisions
    xml_conflicts = db.find_xml_conflicts()
    for conflict in xml_conflicts:
        involved_mods = conflict['mods'].split(',')
        involved_mods = [m for m in involved_mods if m in mod_names]
        if len(involved_mods) > 1:
            conflicts.append(Conflict(
                type='xml_collision',
                severity='medium',
                target=f"{conflict['file_name']}:{conflict['xpath']}",
                mods_involved=involved_mods,
                details=[],
                resolution="Multiple mods modify the same XML node. Last-loaded wins."
            ))
    
    # 3. TODO: Indirect behavioral conflicts (requires call graph analysis)
    
    # 4. TODO: Load order suggestions based on patch types
    
    return CompatibilityReport(
        mods=mod_names,
        conflicts=conflicts,
        load_order_suggestions=suggestions
    )
