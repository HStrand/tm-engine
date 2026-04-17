"""Aggregate self-time by frame from a speedscope-format trace."""
import json
import sys
from collections import defaultdict

path = sys.argv[1]
filter_substr = sys.argv[2] if len(sys.argv) > 2 else None

with open(path, 'r', encoding='utf-8') as f:
    doc = json.load(f)

frames = doc['shared']['frames']
frame_names = [f['name'] for f in frames]

self_time = defaultdict(float)
incl_time = defaultdict(float)
total_time = 0.0

for prof in doc['profiles']:
    events = prof['events']
    # Walk events: between adjacent events, the top-of-stack frame accrues self-time;
    # all frames in the stack accrue inclusive time.
    stack = []
    prev_at = events[0]['at'] if events else 0
    for ev in events:
        at = ev['at']
        dt = at - prev_at
        if dt > 0 and stack:
            self_time[stack[-1]] += dt
            for fidx in set(stack):
                incl_time[fidx] += dt
            total_time += dt
        if ev['type'] == 'O':
            stack.append(ev['frame'])
        elif ev['type'] == 'C':
            if stack and stack[-1] == ev['frame']:
                stack.pop()
            else:
                # unmatched close — try to find
                try:
                    idx = len(stack) - 1 - stack[::-1].index(ev['frame'])
                    stack.pop(idx)
                except ValueError:
                    pass
        prev_at = at

def fmt(t):
    return f"{t:>10.1f}"

def print_top(d, label, n=40, filter_substr=None):
    print(f"\n=== Top {n} frames by {label} ===")
    items = sorted(d.items(), key=lambda kv: -kv[1])
    if filter_substr:
        items = [(fi, t) for fi, t in items if filter_substr.lower() in frame_names[fi].lower()]
    for fi, t in items[:n]:
        pct = 100.0 * t / total_time if total_time else 0
        name = frame_names[fi]
        print(f"{fmt(t)}  {pct:5.1f}%  {name}")

print(f"Total sampled time (per-unit): {total_time:.1f}")
print_top(self_time, "SELF time", 40)
print_top(incl_time, "INCLUSIVE time", 40)

# Filtered: TmEngine domain frames
print("\n=== Top 40 TmEngine.Domain frames by SELF time ===")
items = sorted(self_time.items(), key=lambda kv: -kv[1])
for fi, t in [x for x in items if 'TmEngine' in frame_names[x[0]]][:40]:
    pct = 100.0 * t / total_time if total_time else 0
    print(f"{fmt(t)}  {pct:5.1f}%  {frame_names[fi]}")

print("\n=== Top 40 TmEngine.Domain frames by INCLUSIVE time ===")
items = sorted(incl_time.items(), key=lambda kv: -kv[1])
for fi, t in [x for x in items if 'TmEngine' in frame_names[x[0]]][:40]:
    pct = 100.0 * t / total_time if total_time else 0
    print(f"{fmt(t)}  {pct:5.1f}%  {frame_names[fi]}")

# Serialization hot paths
print("\n=== Top 30 Newtonsoft/Serialization frames by SELF time ===")
items = sorted(self_time.items(), key=lambda kv: -kv[1])
for fi, t in [x for x in items if 'Newtonsoft' in frame_names[x[0]] or 'Serializ' in frame_names[x[0]] or 'JsonConvert' in frame_names[x[0]]][:30]:
    pct = 100.0 * t / total_time if total_time else 0
    print(f"{fmt(t)}  {pct:5.1f}%  {frame_names[fi]}")

# Azure Storage / HTTP paths
print("\n=== Top 30 Azure/Blob/Http frames by INCLUSIVE time ===")
items = sorted(incl_time.items(), key=lambda kv: -kv[1])
for fi, t in [x for x in items if 'Azure' in frame_names[x[0]] or 'Blob' in frame_names[x[0]] or 'Http' in frame_names[x[0]]][:30]:
    pct = 100.0 * t / total_time if total_time else 0
    print(f"{fmt(t)}  {pct:5.1f}%  {frame_names[fi]}")
