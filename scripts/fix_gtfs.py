import os
import zipfile

def clean_gtfs(zip_path, output_path):
    extract_dir = "gtfs_temp"
    if not os.path.exists(extract_dir):
        os.makedirs(extract_dir)

    print(f"Extracting {zip_path}...")
    with zipfile.ZipFile(zip_path, 'r') as zip_ref:
        zip_ref.extractall(extract_dir)

    # Read valid trip IDs
    valid_trips = set()
    trips_path = os.path.join(extract_dir, "trips.txt")
    if os.path.exists(trips_path):
        with open(trips_path, 'r', encoding='utf-8-sig') as f:
            header = f.readline().strip().split(',')
            if 'trip_id' in header:
                trip_idx = header.index('trip_id')
                for line in f:
                    cols = line.strip().split(',')
                    if len(cols) > trip_idx:
                        valid_trips.add(cols[trip_idx].strip('"'))
        print(f"Found {len(valid_trips)} valid trips.")

    # Clean stop_times.txt
    stop_times_path = os.path.join(extract_dir, "stop_times.txt")
    if os.path.exists(stop_times_path):
        valid_lines = []
        invalid_count = 0
        with open(stop_times_path, 'r', encoding='utf-8-sig') as f:
            header_line = f.readline().strip()
            header = header_line.split(',')
            valid_lines.append(header_line)
            if 'trip_id' in header:
                trip_idx = header.index('trip_id')
                for line in f:
                    line_strip = line.strip()
                    cols = line_strip.split(',')
                    if len(cols) > trip_idx:
                        tid = cols[trip_idx].strip('"')
                        if tid in valid_trips:
                            valid_lines.append(line_strip)
                        else:
                            invalid_count += 1
                    else:
                        invalid_count += 1
        print(f"Removed {invalid_count} invalid stop_times.")
        
        with open(stop_times_path, 'w', encoding='utf-8') as f:
            f.write("\n".join(valid_lines) + "\n")

    # Repackage
    print(f"Repackaging to {output_path}...")
    with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(extract_dir):
            for file in files:
                zipf.write(os.path.join(root, file), file)
    
    print("Done.")

if __name__ == "__main__":
    import sys
    if len(sys.argv) > 2:
        clean_gtfs(sys.argv[1], sys.argv[2])
    else:
        print("Usage: python fix_gtfs.py <input.zip> <output.zip>")
