#!/usr/bin/env python3
"""
Semantic Mapper for 7D2D Mod Ecosystem Analyzer

This script takes JSONL traces exported from XmlIndexer and sends them to a local
LLM (via LM Studio API) to generate human-readable descriptions.

Usage:
    python semantic_mapper.py input_traces.jsonl output_mappings.jsonl [--model MODEL_NAME]

Prerequisites:
    1. Install LM Studio: https://lmstudio.ai/
    2. Download a 7B-13B model (e.g., Mistral-7B-Instruct, Llama-2-13B-Chat)
    3. Start the local server in LM Studio (Settings > Local Server > Start)
    4. Default endpoint: http://localhost:1234/v1/chat/completions

Recommended models (in order of quality/speed tradeoff):
    - mistral-7b-instruct-v0.2.Q4_K_M.gguf     (fast, good quality)
    - llama-2-13b-chat.Q4_K_M.gguf             (slower, better quality)
    - codellama-13b-instruct.Q4_K_M.gguf       (good for code understanding)
"""

import json
import sys
import time
import argparse
from pathlib import Path
from typing import Optional

try:
    import requests
except ImportError:
    print("Error: 'requests' library not installed.")
    print("Run: pip install requests")
    sys.exit(1)


# LM Studio default endpoint
LM_STUDIO_URL = "http://localhost:1234/v1/chat/completions"

# System prompt explaining the task
SYSTEM_PROMPT = """You are an expert at translating game modding technical concepts into plain English that players can understand.

Your job is to explain what game mod code and XML properties DO from a player's perspective.

Guidelines:
- Write 1-2 sentences maximum
- Focus on WHAT CHANGES for the player, not HOW it works technically
- Use everyday language (no programming terms)
- Mention specific in-game effects when possible
- Be concrete: "increases backpack size" not "modifies inventory"

Examples of good descriptions:
- "Allows crafting using items from nearby storage containers, not just your inventory"
- "Plays a glass breaking sound when eating food from jars"
- "Increases the base backpack from 45 to 72 slots"
- "Zombies take 20% more headshot damage"
- "Vending machines show prices before you interact with them"

Examples of BAD descriptions (too technical):
- "Modifies the XUiM_PlayerInventory class to expand item slots"
- "Patches ItemActionEat.ExecuteAction for audio playback"
- "Overrides the GetBindingValue method"
"""


def call_llm(trace: dict, model_name: Optional[str] = None) -> Optional[dict]:
    """Send a trace to the LLM and get a description."""
    
    # Build the user prompt with context
    entity_type = trace.get("entity_type", "unknown")
    entity_name = trace.get("entity_name", "unknown")
    parent = trace.get("parent_context", "")
    code_trace = trace.get("code_trace", "")
    game_context = trace.get("game_context", "")
    usage = trace.get("usage_examples", "")
    
    user_prompt = f"""Describe what this {entity_type} does for a 7 Days to Die player:

Name: {entity_name}
{f"Parent/Context: {parent}" if parent else ""}
{f"Game System: {game_context}" if game_context else ""}
{f"Usage: {usage}" if usage else ""}

Code/XML:
```
{code_trace[:2000]}  
```

Write a 1-2 sentence description that a player (not a programmer) would understand.
Focus on the gameplay effect, not the technical implementation."""

    payload = {
        "model": model_name or "local-model",
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user_prompt}
        ],
        "temperature": 0.3,  # Lower = more consistent
        "max_tokens": 200,
        "stream": False
    }
    
    try:
        response = requests.post(LM_STUDIO_URL, json=payload, timeout=60)
        response.raise_for_status()
        
        result = response.json()
        content = result.get("choices", [{}])[0].get("message", {}).get("content", "").strip()
        
        if content:
            # Return the trace with description filled in
            trace["layman_description"] = content
            trace["llm_model"] = model_name or "local-model"
            return trace
            
    except requests.exceptions.ConnectionError:
        print("\n⚠️  Cannot connect to LM Studio. Make sure the local server is running.")
        print("   In LM Studio: Settings > Local Server > Start Server")
        return None
    except requests.exceptions.Timeout:
        print(f"\n⚠️  Timeout for {entity_name}")
        return None
    except Exception as e:
        print(f"\n⚠️  Error for {entity_name}: {e}")
        return None
    
    return None


def process_traces(input_path: str, output_path: str, model_name: Optional[str] = None, 
                   batch_size: int = 50, delay: float = 0.1):
    """Process all traces in the input file."""
    
    input_file = Path(input_path)
    if not input_file.exists():
        print(f"Error: Input file not found: {input_path}")
        sys.exit(1)
    
    # Count lines for progress
    with open(input_file, 'r', encoding='utf-8') as f:
        total_lines = sum(1 for line in f if line.strip())
    
    print(f"╔══════════════════════════════════════════════════════════════════╗")
    print(f"║  SEMANTIC MAPPER FOR 7D2D MOD ANALYZER                           ║")
    print(f"╚══════════════════════════════════════════════════════════════════╝")
    print()
    print(f"  Input:  {input_path}")
    print(f"  Output: {output_path}")
    print(f"  Model:  {model_name or 'local-model (auto-detect)'}")
    print(f"  Traces: {total_lines}")
    print()
    
    # Test connection first
    print("Testing LM Studio connection...", end=" ", flush=True)
    test_trace = {"entity_type": "test", "entity_name": "test", "code_trace": "test"}
    if call_llm(test_trace, model_name) is None:
        # Error message already printed
        sys.exit(1)
    print("✓ Connected!")
    print()
    
    processed = 0
    errors = 0
    
    with open(input_file, 'r', encoding='utf-8') as infile, \
         open(output_path, 'w', encoding='utf-8') as outfile:
        
        for line_num, line in enumerate(infile, 1):
            line = line.strip()
            if not line:
                continue
            
            try:
                trace = json.loads(line)
            except json.JSONDecodeError:
                print(f"⚠️  Skipping invalid JSON on line {line_num}")
                errors += 1
                continue
            
            entity_name = trace.get("entity_name", "unknown")
            entity_type = trace.get("entity_type", "?")
            
            # Progress indicator
            pct = (line_num * 100) // total_lines
            print(f"\r  [{pct:3d}%] Processing: {entity_type}/{entity_name[:40]:<40}", end="", flush=True)
            
            result = call_llm(trace, model_name)
            
            if result:
                outfile.write(json.dumps(result) + "\n")
                processed += 1
            else:
                errors += 1
            
            # Small delay to avoid hammering the API
            if delay > 0:
                time.sleep(delay)
            
            # Periodic flush
            if line_num % batch_size == 0:
                outfile.flush()
    
    print()
    print()
    print(f"╔══════════════════════════════════════════════════════════════════╗")
    print(f"║  COMPLETED                                                       ║")
    print(f"╚══════════════════════════════════════════════════════════════════╝")
    print(f"  Processed: {processed}")
    print(f"  Errors:    {errors}")
    print(f"  Output:    {output_path}")
    print()
    print(f"Next step:")
    print(f"  XmlIndexer import-semantic-mappings <database.db> {output_path}")


def main():
    parser = argparse.ArgumentParser(
        description="Generate human-readable descriptions for 7D2D mod traces using local LLM",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python semantic_mapper.py traces.jsonl mappings.jsonl
  python semantic_mapper.py traces.jsonl mappings.jsonl --model mistral-7b-instruct

Prerequisites:
  1. Install LM Studio (https://lmstudio.ai/)
  2. Download a model (7B-13B recommended)
  3. Start the local server (Settings > Local Server > Start)
"""
    )
    
    parser.add_argument("input", help="Input JSONL file with traces from XmlIndexer")
    parser.add_argument("output", help="Output JSONL file for import back to XmlIndexer")
    parser.add_argument("--model", "-m", help="Model name (default: auto-detect from LM Studio)")
    parser.add_argument("--delay", "-d", type=float, default=0.1, 
                        help="Delay between API calls in seconds (default: 0.1)")
    parser.add_argument("--batch", "-b", type=int, default=50,
                        help="Flush output every N items (default: 50)")
    parser.add_argument("--url", "-u", default=LM_STUDIO_URL,
                        help=f"LM Studio API URL (default: {LM_STUDIO_URL})")
    
    args = parser.parse_args()
    
    global LM_STUDIO_URL
    LM_STUDIO_URL = args.url
    
    process_traces(args.input, args.output, args.model, args.batch, args.delay)


if __name__ == "__main__":
    main()
