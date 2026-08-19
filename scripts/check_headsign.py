import sqlite3
conn = sqlite3.connect('GtfsData.db')
cursor = conn.cursor()
cursor.execute("SELECT RouteId, DirectionId, TripHeadsign FROM GtfsTrips WHERE RouteId LIKE 'ESHOT_%' LIMIT 20")
print(cursor.fetchall())
