import sqlite3
conn = sqlite3.connect('game_trace.db')
cursor = conn.cursor()
cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
print("Tables:", [x[0] for x in cursor.fetchall()])
cursor.execute("SELECT COUNT(*) FROM game_trace")
print("Total records:", cursor.fetchone()[0])
conn.close()
