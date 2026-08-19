import psycopg2
conn = psycopg2.connect('host=localhost port=5432 dbname=TransportDb user=postgres')
c = conn.cursor()
c.execute('SELECT stop_sequence, stop_id, arrival_time FROM ""GtfsStopTimes"" WHERE trip_id = ''IZBAN_1793'' ORDER BY stop_sequence;')
for row in c.fetchall(): print(row)
