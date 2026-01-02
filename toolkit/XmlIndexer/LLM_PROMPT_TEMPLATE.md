You are writing plain-English descriptions of 7 Days to Die game data. The file `toolkit/XmlIndexer/sample_trace.jsonl` contains 16,399 traces that need descriptions.

═══════════════════════════════════════════════════════════════════════
CRITICAL RULES - READ CAREFULLY
═══════════════════════════════════════════════════════════════════════
1. You MUST write descriptions yourself. NEVER create scripts or code.
2. Each batch = EXACTLY 100 traces. Write descriptions for ALL 100.
3. File naming: batch_NNN.jsonl where NNN = 001, 002, 003, etc.
4. ALWAYS use subagents to write batches (see WORKFLOW below).
5. ALWAYS verify each batch file was created before importing it.

═══════════════════════════════════════════════════════════════════════
COMPLETE WORKFLOW - FOLLOW EVERY STEP IN ORDER
═══════════════════════════════════════════════════════════════════════

STEP 1: FIND THE NEXT BATCH NUMBER
──────────────────────────────────────────────────────────────────────
Run this command to find existing batch files:

  cd c:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript
  Get-ChildItem toolkit/XmlIndexer/batch_*.jsonl | Select-Object Name

Look at the output:
  - If NO files exist → Next batch is 001, start at line 1
  - If batch_001.jsonl exists → Next batch is 002, start at line 101
  - If batch_002.jsonl exists → Next batch is 003, start at line 201
  - If batch_003.jsonl exists → Next batch is 004, start at line 301

Calculate the next batch:
  NEXT_BATCH = (highest_batch_number + 1)
  START_LINE = ((NEXT_BATCH - 1) × 100) + 1
  END_LINE = NEXT_BATCH × 100

Example: If batch_003.jsonl is the last file:
  NEXT_BATCH = 004
  START_LINE = 301
  END_LINE = 400

STEP 2: LAUNCH SUBAGENT TO WRITE THE BATCH
──────────────────────────────────────────────────────────────────────
Use runSubagent tool with these EXACT parameters:

  description: "Write batch_NNN (lines XXX-YYY)"
  
  prompt: "You are writing game descriptions for 7 Days to Die.

TASK: Read traces from sample_trace.jsonl and write descriptions.

1. Read the file:
   - File: toolkit/XmlIndexer/sample_trace.jsonl
   - Use read_file tool with offset=XXX and limit=100
   - This gets lines XXX through YYY

2. For EACH of the 100 traces, write these fields:
   - entity_type: Copy exactly from the trace
   - entity_name: Copy exactly from the trace
   - layman_description: 1 sentence a new player can understand
   - technical_description: 1 sentence for modders
   - player_impact: 1 sentence about gameplay effect

3. Output format (one JSON object per line):
{\"entity_type\":\"property_name\",\"entity_name\":\"ExampleName\",\"layman_description\":\"What players see.\",\"technical_description\":\"Technical details.\",\"player_impact\":\"How it affects gameplay.\"}

4. STYLE RULES - MUST FOLLOW:
   NEVER use: UI, sprite, prefab, atlas, inheritance, child/parent, sort key, component, hex, RGB, reference, definition, parameter
   ALWAYS use: inventory, menu, picture, icon, 3D shape, copied from, setting, value
   Write for players, NOT programmers.

5. Save your output:
   - Use create_file tool
   - Path: c:\\Users\\Admin\\Documents\\GIT\\GameMods\\7D2DMods\\7D2D-DecompilerScript\\toolkit\\XmlIndexer\\batch_NNN.jsonl
   - Content: ALL 100 JSON lines (one per line, no commas between them)

6. IMPORTANT: Write ALL 100 descriptions. Do not skip any. Do not write code."

Replace NNN, XXX, and YYY with actual numbers:
  - NNN = batch number (001, 002, 003, etc.)
  - XXX = START_LINE from Step 1
  - YYY = END_LINE from Step 1

STEP 3: VERIFY THE SUBAGENT CREATED THE FILE
──────────────────────────────────────────────────────────────────────
After the subagent completes, verify the file exists:

  Test-Path toolkit/XmlIndexer/batch_NNN.jsonl

If the file does NOT exist:
  - The subagent failed to save the file
  - Check if the subagent returned JSON output in its response
  - If yes, use create_file to save it yourself:
    
    create_file(
      filePath: "c:\\Users\\Admin\\Documents\\GIT\\GameMods\\7D2DMods\\7D2D-DecompilerScript\\toolkit\\XmlIndexer\\batch_NNN.jsonl",
      content: [paste the subagent's JSON output here]
    )

STEP 4: IMPORT THE BATCH INTO THE DATABASE
──────────────────────────────────────────────────────────────────────
Run this command:

  cd c:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer
  dotnet run -- import-semantic-mappings ecosystem.db batch_NNN.jsonl

Look for the output:
  "Imported: XX mappings"
  
If the import fails or imports 0 mappings:
  - Check if the file exists
  - Check if the JSON format is correct (use read_file to inspect it)

STEP 5: REPEAT FOR NEXT BATCH
──────────────────────────────────────────────────────────────────────
Go back to STEP 1 and process the next batch.

STEP 6: CHECK OVERALL PROGRESS (Every 10 batches)
──────────────────────────────────────────────────────────────────────
Run this command:

  cd c:\Users\Admin\Documents\GIT\GameMods\7D2DMods\7D2D-DecompilerScript\toolkit\XmlIndexer
  dotnet run -- semantic-status ecosystem.db

This shows how many traces have been mapped.


═══════════════════════════════════════════════════════════════════════
PROCESSING MULTIPLE BATCHES IN PARALLEL (OPTIONAL)
═══════════════════════════════════════════════════════════════════════
You CAN launch multiple subagents at once for different batches:

Example: Launch 3 subagents simultaneously:
  - Subagent 1: batch_004 (lines 301-400)
  - Subagent 2: batch_005 (lines 401-500)  
  - Subagent 3: batch_006 (lines 501-600)

AFTER all 3 complete:
  1. Verify ALL 3 files exist (use Test-Path for each)
  2. Import them one at a time in order
  3. Check semantic-status

═══════════════════════════════════════════════════════════════════════
STYLE RULES FOR DESCRIPTIONS
═══════════════════════════════════════════════════════════════════════
GOAL: Write for a player who just bought the game, NOT a programmer.

FORBIDDEN WORDS (never use these):
  ❌ UI, sprite, prefab, atlas, inheritance, child/parent, sort key
  ❌ component, hex, RGB, reference, definition, parameter

USE INSTEAD:
  ✓ inventory, menu, picture, icon, 3D shape, copied from, setting, value

GOOD EXAMPLES:
  ✓ "Which picture shows for this item in your inventory"
  ✓ "Controls where this block appears in the building menu"  
  ✓ "How many slots this container has"

BAD EXAMPLES:
  ❌ "Sprite name used as the item's inventory icon"
  ❌ "Secondary sort key used by UI lists"
  ❌ "Integer parameter for grid size"

═══════════════════════════════════════════════════════════════════════
TROUBLESHOOTING COMMON ISSUES
═══════════════════════════════════════════════════════════════════════

PROBLEM: Subagent didn't create the file
SOLUTION: Check if subagent returned JSON in its response. If yes, use 
          create_file to save it yourself.

PROBLEM: Import says "0 mappings imported"  
SOLUTION: Read the batch file with read_file. Check if JSON format is 
          correct (one object per line, no commas between lines).

PROBLEM: Lost track of which batch is next
SOLUTION: Run Get-ChildItem toolkit/XmlIndexer/batch_*.jsonl and look
          at the highest number. Next batch = that number + 1.

PROBLEM: Not sure if a batch was imported
SOLUTION: Run semantic-status and check the total count. Each successful
          batch adds ~80-100 mappings.

═══════════════════════════════════════════════════════════════════════
QUICK REFERENCE
═══════════════════════════════════════════════════════════════════════

Find next batch:
  Get-ChildItem toolkit/XmlIndexer/batch_*.jsonl | Select-Object Name

Verify file exists:  
  Test-Path toolkit/XmlIndexer/batch_NNN.jsonl

Import batch:
  cd toolkit/XmlIndexer
  dotnet run -- import-semantic-mappings ecosystem.db batch_NNN.jsonl

Check progress:
  cd toolkit/XmlIndexer  
  dotnet run -- semantic-status ecosystem.db

Total traces to process: 16,399
Target: ~164 batches (16,399 ÷ 100)
