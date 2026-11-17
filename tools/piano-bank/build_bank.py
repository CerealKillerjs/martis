#!/usr/bin/env python3
"""Builds src/Martis.Audio/Assets/piano.bank from the VCSL Steinway B samples (CC0).

Source: Versilian Community Sample Library, "Grand Piano, Steinway B", Sus (pedal down),
close mics, velocity layers vl2 and vl3, https://github.com/sgossner/VCSL (CC0 1.0).

Each sample is summed to mono, cut to a length that depends on the register (low notes
ring longer), faded out with a raised cosine over its last 1.5 s and encoded as IMA ADPCM
(4 bits per sample) in blocks that carry their own decoder state.

Usage: build_bank.py <folder with the VCSL .wav files> <output piano.bank>
Needs ffmpeg and numpy. Format: see PianoBank.cs.
"""
import struct
import subprocess
import sys

import numpy as np

RATE = 44100
BLOCK = 2048
NAMES = ["C", "D", "E", "F#", "G#", "A#"]
OFFSETS = {"C": 0, "D": 2, "E": 4, "F#": 6, "G#": 8, "A#": 10}
LAYERS = [2, 3]
LOW, HIGH = 34, 96

STEPS = [7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66,
         73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408,
         449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
         2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630,
         9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767]
INDEX = [-1, -1, -1, -1, 2, 4, 6, 8]


def name(midi):
    octave = midi // 12 - 1
    return f"{NAMES[(midi % 12) // 2]}{octave}"


def load(path, seconds):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(RATE), "-t", str(seconds),
                          "-f", "s16le", "-"], check=True, capture_output=True).stdout
    x = np.frombuffer(raw, dtype=np.int16).astype(np.float64)
    fade = int(1.5 * RATE)
    x[-fade:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, fade))
    return np.clip(np.round(x), -32768, 32767).astype(np.int64).tolist()


def encode_block(samples, predictor, index):
    out = bytearray(struct.pack("<hBB", predictor, index, 0))
    nibbles = []
    for sample in samples:
        step = STEPS[index]
        diff = sample - predictor
        code = 0
        if diff < 0:
            code = 8
            diff = -diff
        delta = step >> 3
        if diff >= step:
            code |= 4
            diff -= step
            delta += step
        if diff >= step >> 1:
            code |= 2
            diff -= step >> 1
            delta += step >> 1
        if diff >= step >> 2:
            code |= 1
            delta += step >> 2
        predictor = max(-32768, min(32767, predictor - delta if code & 8 else predictor + delta))
        index = max(0, min(88, index + INDEX[code & 7]))
        nibbles.append(code)
    nibbles += [0] * (BLOCK - len(nibbles))
    for i in range(0, BLOCK, 2):
        out.append(nibbles[i] | (nibbles[i + 1] << 4))
    return out, predictor, index


def main(folder, output):
    entries = []
    for midi in range(LOW, HIGH + 1):
        if (midi % 12) % 2:
            continue
        seconds = min(9.0, max(4.5, 9.0 - (midi - LOW) * 0.075))
        for layer in LAYERS:
            pcm = load(f"{folder}/JHPiano_Sus_Close_{name(midi)}_vl{layer}_rr1.wav", seconds)
            data = bytearray()
            predictor, index = pcm[0], 0
            for start in range(0, len(pcm), BLOCK):
                block, predictor, index = encode_block(pcm[start:start + BLOCK], predictor, index)
                data += block
            entries.append((midi, layer, len(pcm), data))
            print(name(midi), layer, len(pcm), file=sys.stderr)
    with open(output, "wb") as f:
        f.write(b"MPB1")
        f.write(struct.pack("<iii", RATE, BLOCK, len(entries)))
        for midi, layer, length, data in entries:
            f.write(struct.pack("<BBi", midi, layer, length))
            f.write(data)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
