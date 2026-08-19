$scenarios = @()
$idCounter = 1

function Create-Scenario ($category, $oLat, $oLon, $dLat, $dLon, $mode, $time, $expectedTransfers, $isRouteFound, $expectedArrTime, $expectedDepTime) {
    global $idCounter
    $scenario = @{
        scenario_id = "SCENARIO-$($idCounter.ToString('D3'))"
        category = $category
        request = @{
            origin = @{ lat = $oLat; lon = $oLon }
            destination = @{ lat = $dLat; lon = $dLon }
            search_date = "2024-10-15"
            search_time = $time
            search_mode = $mode
            max_walking_meters = 2000
            max_transfers = $expectedTransfers
        }
        assertions = @{
            is_route_found = $isRouteFound
            max_allowed_transfers = $expectedTransfers
            latest_arrival_time = $expectedArrTime
            min_departure_time_for_arrive_by = $expectedDepTime
        }
    }
    $global:idCounter++
    return $scenario
}

$locs = @{
    Konak = @(38.4192, 27.1287)
    Alsancak = @(38.4343, 27.1422)
    Bornova = @(38.4636, 27.2185)
    Buca = @(38.3846, 27.1687)
    Karsiyaka = @(38.4552, 27.1179)
    Gaziemir = @(38.3241, 27.1328)
    Balcova = @(38.3900, 27.0450)
    Cigli = @(38.4891, 27.0543)
    Ocean = @(38.0000, 26.0000)
    WalkingClose1 = @(38.4200, 27.1300)
    WalkingClose2 = @(38.4205, 27.1305)
}

# 10x 0-transfer
for ($i=0; $i -lt 10; $i++) {
    # Expected arrival is roughly 45 mins later to give generous test leeway
    $scenarios += Create-Scenario "0-transfer" $locs.Konak[0] $locs.Konak[1] $locs.Alsancak[0] $locs.Alsancak[1] "DEPART_AT" "08:00:00" 0 $true "08:45:00" $null
}

# 10x 1-transfer
for ($i=0; $i -lt 10; $i++) {
    $scenarios += Create-Scenario "1-transfer" $locs.Buca[0] $locs.Buca[1] $locs.Karsiyaka[0] $locs.Karsiyaka[1] "DEPART_AT" "08:30:00" 1 $true "10:30:00" $null
}

# 8x 2-transfer
for ($i=0; $i -lt 8; $i++) {
    $scenarios += Create-Scenario "2-transfer" $locs.Balcova[0] $locs.Balcova[1] $locs.Cigli[0] $locs.Cigli[1] "DEPART_AT" "09:00:00" 2 $true "12:00:00" $null
}

# 4x ARRIVE_BY
for ($i=0; $i -lt 4; $i++) {
    $scenarios += Create-Scenario "ARRIVE_BY" $locs.Gaziemir[0] $locs.Gaziemir[1] $locs.Bornova[0] $locs.Bornova[1] "ARRIVE_BY" "08:00:00" 1 $true $null "06:00:00"
}

# 3x Night
for ($i=0; $i -lt 3; $i++) {
    $scenarios += Create-Scenario "Night" $locs.Konak[0] $locs.Konak[1] $locs.Buca[0] $locs.Buca[1] "DEPART_AT" "24:30:00" 0 $true "25:30:00" $null
}

# 3x No route
for ($i=0; $i -lt 3; $i++) {
    $scenarios += Create-Scenario "NoRoute" $locs.Ocean[0] $locs.Ocean[1] $locs.Ocean[0] ($locs.Ocean[1]+0.01) "DEPART_AT" "12:00:00" 0 $false $null $null
}

# 2x Walking optimization
for ($i=0; $i -lt 2; $i++) {
    $scenarios += Create-Scenario "WalkingOptimization" $locs.WalkingClose1[0] $locs.WalkingClose1[1] $locs.WalkingClose2[0] $locs.WalkingClose2[1] "DEPART_AT" "14:00:00" 0 $true "14:15:00" $null
}

$scenarios | ConvertTo-Json -Depth 10 | Set-Content "tests\TransportDataService.Tests\Acceptance\journey-golden-scenarios.json" -Encoding UTF8
Write-Host "Created journey-golden-scenarios.json"
