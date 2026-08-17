import json
import random

# Izmir center approx (Konak)
CENTER_LAT = 38.419
CENTER_LON = 27.128

# Define some key regions/districts for variety (still relatively central)
# Bornova, Buca, Karsiyaka, Balcova, Gaziemir
REGIONS = [
    {"name": "Konak", "lat": 38.419, "lon": 27.128, "range": 0.05},
    {"name": "Bornova", "lat": 38.461, "lon": 27.218, "range": 0.04},
    {"name": "Buca", "lat": 38.384, "lon": 27.170, "range": 0.04},
    {"name": "Karsiyaka", "lat": 38.457, "lon": 27.114, "range": 0.05},
    {"name": "Balcova", "lat": 38.389, "lon": 27.046, "range": 0.03},
    {"name": "Gaziemir", "lat": 38.324, "lon": 27.135, "range": 0.03}
]

def get_random_point(region):
    # offset by +/- range
    lat_offset = (random.random() * 2 - 1) * region["range"]
    lon_offset = (random.random() * 2 - 1) * region["range"]
    return {
        "lat": round(region["lat"] + lat_offset, 5),
        "lon": round(region["lon"] + lon_offset, 5),
        "region": region["name"]
    }

def main():
    random.seed(42) # For reproducibility
    dataset = []
    
    for i in range(40):
        origin_region = random.choice(REGIONS)
        # Ensure destination is in a different region mostly, but sometimes same
        dest_region = random.choice(REGIONS)
        
        origin = get_random_point(origin_region)
        dest = get_random_point(dest_region)
        
        dataset.append({
            "id": i + 1,
            "origin": {"lat": origin["lat"], "lon": origin["lon"]},
            "destination": {"lat": dest["lat"], "lon": dest["lon"]},
            "description": f"From {origin['region']} to {dest['region']}"
        })
        
    with open("tests/40_od_dataset.json", "w", encoding="utf-8") as f:
        json.dump(dataset, f, indent=2, ensure_ascii=False)
        
    print(f"Generated 40 OD pairs in tests/40_od_dataset.json")

if __name__ == "__main__":
    main()
