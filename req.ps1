$body = @{
    Origin = @{Lat=38.45727089547822; Lon=27.11795523541855}
    Destination = @{Lat=38.478850587268624; Lon=27.20177946573503}
    DateTime = "2026-08-19T09:00:00+00:00"
    SearchMode = 0
    MaxTransfers = 2
    MaxWalkingMeters = 3000
    MaxResults = 10
    IncludeIntermediateStops = $true
    IncludeWalkingGeometry = $true
} | ConvertTo-Json
Invoke-RestMethod -Uri http://localhost:5108/api/v2/journey-plans/search -Method Post -Body $body -ContentType "application/json" | ConvertTo-Json -Depth 10 | Out-File C:\Users\HP\.gemini\antigravity-ide\brain\c7b1f780-603a-46c1-b19d-79348ba01718\scratch\response_v2.json
