"""
Command-line interface for 7D2D mod maintenance toolkit.
"""

import click
from pathlib import Path

from . import __version__


@click.group()
@click.version_option(version=__version__)
def main():
    """7D2D Mod Maintenance Toolkit - AI-first code analysis tools."""
    pass


@main.command()
@click.option('--source', '-s', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to source code directory')
@click.option('--output', '-o', type=click.Path(path_type=Path),
              required=True, help='Path to output SQLite database')
@click.option('--refs', '-r', type=click.Path(exists=True, path_type=Path),
              help='Path to reference DLLs for type resolution')
def build(source: Path, output: Path, refs: Path | None):
    """Build call graph database from source code."""
    click.echo(f"Building call graph from: {source}")
    click.echo(f"Output database: {output}")
    
    # TODO: Call C# extractor
    click.echo("Build command not yet implemented - see Commit 5")


@main.command()
@click.argument('method')
@click.option('--db', '-d', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to call graph database')
@click.option('--json', 'as_json', is_flag=True, help='Output as JSON')
def callers(method: str, db: Path, as_json: bool):
    """Find all methods that call the specified method."""
    click.echo(f"Finding callers of: {method}")
    
    # TODO: Implement query
    click.echo("Callers command not yet implemented - see Commit 6")


@main.command()
@click.argument('method')
@click.option('--db', '-d', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to call graph database')
@click.option('--json', 'as_json', is_flag=True, help='Output as JSON')
def callees(method: str, db: Path, as_json: bool):
    """Find all methods called by the specified method."""
    click.echo(f"Finding callees of: {method}")
    
    # TODO: Implement query
    click.echo("Callees command not yet implemented - see Commit 6")


@main.command()
@click.argument('from_method')
@click.argument('to_method')
@click.option('--db', '-d', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to call graph database')
@click.option('--json', 'as_json', is_flag=True, help='Output as JSON')
def chain(from_method: str, to_method: str, db: Path, as_json: bool):
    """Trace execution path between two methods."""
    click.echo(f"Tracing path: {from_method} -> {to_method}")
    
    # TODO: Implement igraph path finding
    click.echo("Chain command not yet implemented - see Commit 7")


@main.command()
@click.argument('keyword')
@click.option('--db', '-d', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to call graph database')
@click.option('--json', 'as_json', is_flag=True, help='Output as JSON')
def search(keyword: str, db: Path, as_json: bool):
    """Search method bodies for keyword."""
    click.echo(f"Searching for: {keyword}")
    
    # TODO: Implement FTS5 search
    click.echo("Search command not yet implemented - see Commit 9")


@main.command()
@click.argument('mods', nargs=-1, required=True)
@click.option('--db', '-d', type=click.Path(exists=True, path_type=Path),
              required=True, help='Path to call graph database')
@click.option('--json', 'as_json', is_flag=True, help='Output as JSON')
def compat(mods: tuple[str, ...], db: Path, as_json: bool):
    """Check compatibility between multiple mods."""
    click.echo(f"Checking compatibility of: {', '.join(mods)}")
    
    # TODO: Implement compatibility check
    click.echo("Compat command not yet implemented - see Commit 12")


if __name__ == '__main__':
    main()
