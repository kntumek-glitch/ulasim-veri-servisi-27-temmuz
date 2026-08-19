const { Client } = require('pg'); 
const client = new Client({ connectionString: 'postgresql://postgres@localhost:5432/TransportDb' }); 
async function run() { 
    await client.connect(); 
    const res = await client.query(`SELECT stop_sequence, stop_id, arrival_time FROM "GtfsStopTimes" WHERE trip_id = 'IZBAN_1793' ORDER BY stop_sequence LIMIT 30`); 
    res.rows.forEach(r => console.log(r.stop_sequence, r.stop_id, r.arrival_time)); 
    await client.end(); 
} 
run();
