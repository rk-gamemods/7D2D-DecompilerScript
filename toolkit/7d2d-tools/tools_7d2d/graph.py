"""
Graph analysis using igraph for path finding and reachability.
"""

from pathlib import Path
from typing import Any

import igraph as ig

from .db import CallGraphDB


class CallGraph:
    """igraph-based call graph for path analysis."""
    
    def __init__(self, db: CallGraphDB):
        self.db = db
        self._graph: ig.Graph | None = None
        self._id_to_vertex: dict[int, int] = {}
        self._vertex_to_id: dict[int, int] = {}
    
    def load(self):
        """Load graph from database."""
        edges = self.db.get_all_edges()
        
        # Collect unique method IDs
        method_ids = set()
        for caller_id, callee_id in edges:
            method_ids.add(caller_id)
            method_ids.add(callee_id)
        
        # Create mapping: method_id <-> vertex index
        method_ids = sorted(method_ids)
        self._id_to_vertex = {mid: idx for idx, mid in enumerate(method_ids)}
        self._vertex_to_id = {idx: mid for mid, idx in self._id_to_vertex.items()}
        
        # Build graph
        vertex_edges = [
            (self._id_to_vertex[caller], self._id_to_vertex[callee])
            for caller, callee in edges
        ]
        
        self._graph = ig.Graph(
            n=len(method_ids),
            edges=vertex_edges,
            directed=True
        )
        
        # Store method IDs as vertex attributes
        self._graph.vs['method_id'] = list(self._vertex_to_id.values())
    
    @property
    def graph(self) -> ig.Graph:
        """Get the igraph Graph object, loading if needed."""
        if self._graph is None:
            self.load()
        return self._graph
    
    def find_path(self, from_id: int, to_id: int) -> list[int] | None:
        """Find shortest path between two methods. Returns list of method IDs."""
        if from_id not in self._id_to_vertex or to_id not in self._id_to_vertex:
            return None
        
        from_v = self._id_to_vertex[from_id]
        to_v = self._id_to_vertex[to_id]
        
        paths = self.graph.get_shortest_paths(from_v, to_v, mode='out')
        if not paths or not paths[0]:
            return None
        
        return [self._vertex_to_id[v] for v in paths[0]]
    
    def find_all_paths(self, from_id: int, to_id: int, max_depth: int = 10) -> list[list[int]]:
        """Find all paths between two methods up to max_depth."""
        if from_id not in self._id_to_vertex or to_id not in self._id_to_vertex:
            return []
        
        from_v = self._id_to_vertex[from_id]
        to_v = self._id_to_vertex[to_id]
        
        # Use BFS-like approach with depth limit
        all_paths = []
        self._find_paths_recursive(from_v, to_v, [], set(), max_depth, all_paths)
        
        return [[self._vertex_to_id[v] for v in path] for path in all_paths]
    
    def _find_paths_recursive(self, current: int, target: int, 
                               path: list[int], visited: set[int],
                               depth: int, results: list[list[int]]):
        """Recursive path finding helper."""
        if depth < 0:
            return
        
        path = path + [current]
        
        if current == target:
            results.append(path)
            return
        
        if current in visited:
            return
        
        visited = visited | {current}
        
        for neighbor in self.graph.neighbors(current, mode='out'):
            self._find_paths_recursive(neighbor, target, path, visited, depth - 1, results)
    
    def get_reachable(self, from_id: int) -> set[int]:
        """Get all methods reachable from the given method."""
        if from_id not in self._id_to_vertex:
            return set()
        
        from_v = self._id_to_vertex[from_id]
        reachable_v = self.graph.subcomponent(from_v, mode='out')
        
        return {self._vertex_to_id[v] for v in reachable_v}
    
    def get_reverse_reachable(self, to_id: int) -> set[int]:
        """Get all methods that can reach the given method."""
        if to_id not in self._id_to_vertex:
            return set()
        
        to_v = self._id_to_vertex[to_id]
        reachable_v = self.graph.subcomponent(to_v, mode='in')
        
        return {self._vertex_to_id[v] for v in reachable_v}


def build_path_result(db: CallGraphDB, path: list[int]) -> dict[str, Any]:
    """Convert a path of method IDs to a detailed result."""
    chain = []
    files = []
    
    for method_id in path:
        info = db.get_method_info(method_id)
        if info:
            chain.append(f"{info['type_name']}.{info['signature']}")
            if info['file_path']:
                files.append(f"{info['file_path']}:{info['line_number']}")
            else:
                files.append("unknown")
    
    return {
        "depth": len(path),
        "chain": chain,
        "files": files
    }
